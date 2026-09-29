using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;

namespace SideScreenMonitor {
    public sealed class Reading {
        public double Cpu, Memory, Disk, Upload, Download;
        public double CpuClock, CpuTemp = -1, CpuPower = -1;
        public double Gpu = -1, GpuClock = -1, GpuTemp = -1, GpuPower = -1;
        public double VramUsed = -1, VramTotal = -1;
        public double ComponentPower { get { return CpuPower >= 0 && GpuPower >= 0 ? CpuPower + GpuPower : -1; } }
        public double VramPercent { get { return VramUsed >= 0 && VramTotal > 0 ? Math.Min(100, 100.0 * VramUsed / VramTotal) : -1; } }
        public ulong FreeMemory;
        public long DiskFree;
        public string CpuName = "CPU", GpuName = "GPU", MemorySpec = "MEMORY", Adapter = "NETWORK";
        public DateTime Timestamp = DateTime.Now;
        public bool PubgRunning;
    }
    public sealed class Sensors {
        [StructLayout(LayoutKind.Sequential)] struct FILETIME { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }
        [StructLayout(LayoutKind.Sequential)] class MEMORY { public uint Length = (uint)Marshal.SizeOf(typeof(MEMORY)); public uint Load; public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
        [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORY memory);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        ulong oldIdle, oldTotal;
        long oldSent, oldReceived;
        DateTime oldTime = DateTime.MinValue;
        string adapterId = "";
        string cpu = "CPU", gpu = "GPU", memorySpec = "MEMORY";
        double cpuClock;
        bool initialized;
        int polls;
        readonly string gpuExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        static IEnumerable<ManagementBaseObject> Query(string path, string query) {
            var scope = new ManagementScope(path, new ConnectionOptions { Timeout = TimeSpan.FromSeconds(2) });
            var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = true };
            using (var search = new ManagementObjectSearcher(scope, new ObjectQuery(query), options))
            using (var rows = search.Get()) { foreach (ManagementBaseObject row in rows) yield return row; }
        }
        void Init() {
            try { foreach (var p in Query("root\\cimv2", "SELECT Name,CurrentClockSpeed FROM Win32_Processor")) { cpu = Convert.ToString(p["Name"]).Trim(); cpuClock = Convert.ToDouble(p["CurrentClockSpeed"]) / 1000.0; break; } } catch { }
            try { foreach (var p in Query("root\\cimv2", "SELECT Name FROM Win32_VideoController")) { gpu = Convert.ToString(p["Name"]); if (gpu.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0) break; } } catch { }
            try { ulong capacity = 0; uint speed = 0, kind = 0; foreach (var p in Query("root\\cimv2", "SELECT Capacity,ConfiguredClockSpeed,SMBIOSMemoryType FROM Win32_PhysicalMemory")) { capacity += Convert.ToUInt64(p["Capacity"]); speed = Convert.ToUInt32(p["ConfiguredClockSpeed"]); kind = Convert.ToUInt32(p["SMBIOSMemoryType"]); } memorySpec = (kind == 34 ? "DDR5" : kind == 26 ? "DDR4" : "RAM") + "-" + speed + "  " + (capacity / 1073741824UL) + " GB"; } catch { }
            initialized = true;
        }
        static double Number(string value) { double result; return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result) ? result : -1; }
        public Reading Poll() {
            if (!initialized) Init();
            var r = new Reading { CpuName = cpu, GpuName = gpu, MemorySpec = memorySpec, CpuClock = cpuClock };
            FILETIME i, k, u;
            if (GetSystemTimes(out i, out k, out u)) { ulong total = k.Value + u.Value; if (oldTotal != 0 && total > oldTotal) r.Cpu = Math.Max(0, Math.Min(100, 100.0 * (1.0 - (double)(i.Value - oldIdle) / (total - oldTotal)))); oldIdle = i.Value; oldTotal = total; }
            var m = new MEMORY(); if (GlobalMemoryStatusEx(m)) { r.Memory = m.Load; r.FreeMemory = m.Available / 1048576UL; }
            try { var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)); r.Disk = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize; r.DiskFree = d.TotalFreeSpace / 1073741824L; } catch { }
            try {
                NetworkInterface chosen = null; long best = -1;
                foreach (var n in NetworkInterface.GetAllNetworkInterfaces()) {
                    if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback || n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    if (n.GetIPProperties().GatewayAddresses.Count == 0) continue;
                    var s = n.GetIPv4Statistics(); long volume = s.BytesReceived + s.BytesSent;
                    if (volume > best) { best = volume; chosen = n; }
                }
                if (chosen != null) { var s = chosen.GetIPv4Statistics(); var now = DateTime.UtcNow; double seconds = (now - oldTime).TotalSeconds;
                    if (adapterId == chosen.Id && seconds > 0 && oldTime != DateTime.MinValue) { r.Upload = Math.Max(0, s.BytesSent - oldSent) / seconds; r.Download = Math.Max(0, s.BytesReceived - oldReceived) / seconds; }
                    adapterId = chosen.Id; oldSent = s.BytesSent; oldReceived = s.BytesReceived; oldTime = now; r.Adapter = chosen.Name;
                }
            } catch { }
            if (File.Exists(gpuExe)) {
                try { var psi = new ProcessStartInfo(gpuExe, "--query-gpu=name,utilization.gpu,clocks.gr,temperature.gpu,power.draw,memory.used,memory.total --format=csv,noheader,nounits") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var p = Process.Start(psi)) { var output = p.StandardOutput.ReadToEndAsync(); var errors = p.StandardError.ReadToEndAsync(); if (p.WaitForExit(1800)) { var lines = output.Result.Trim().Split('\n'); if (lines.Length > 0) { var parts = lines[0].Split(','); if (parts.Length >= 5) { r.GpuName = parts[0].Trim(); r.Gpu = Number(parts[1]); r.GpuClock = Number(parts[2]); r.GpuTemp = Number(parts[3]); r.GpuPower = Number(parts[4]); if (parts.Length >= 7) { r.VramUsed = Number(parts[5]); r.VramTotal = Number(parts[6]); } } } } else { try { p.Kill(); } catch { } } }
                } catch { }
            }
            // Optional sensor providers: consume their WMI data only when already running.
            if (polls++ % 5 == 0) ReadOptionalSensors();
            r.CpuTemp = optionalCpuTemp; r.CpuPower = optionalCpuPower;
            try {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensors", "LibreHardwareMonitor", "cpu-readings.xml");
                var data = XElement.Load(path);
                DateTime timestamp = DateTime.Parse((string)data.Element("TimestampUtc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                double age = (DateTime.UtcNow - timestamp.ToUniversalTime()).TotalSeconds;
                if (age >= 0 && age < 8) { r.CpuTemp = (double?)data.Element("Temperature") ?? -1; r.CpuPower = (double?)data.Element("Power") ?? -1; }
            } catch { }
            r.Timestamp = DateTime.Now;
            try { r.PubgRunning = PubgApi.IsRunning(); } catch { }
            return r;
        }
        double optionalCpuTemp = -1, optionalCpuPower = -1;
        void ReadOptionalSensors() {
            optionalCpuTemp = optionalCpuPower = -1;
            foreach (string provider in new [] { "LibreHardwareMonitor", "OpenHardwareMonitor" }) {
                try { foreach (var s in Query("root\\" + provider, "SELECT Name,SensorType,Value,Parent FROM Sensor")) {
                    string name = Convert.ToString(s["Name"]), parent = Convert.ToString(s["Parent"]), type = Convert.ToString(s["SensorType"]);
                    if (parent.IndexOf("cpu", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    double v = Convert.ToDouble(s["Value"]);
                    if (type == "Temperature" && (name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0)) optionalCpuTemp = v;
                    if (type == "Power" && name.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0) optionalCpuPower = v;
                } } catch { }
            }
        }
    }
    public sealed class Settings {
        public string Monitor = "", Art = "";
        public bool Topmost = true, PetBubbles;
        public bool AutoMemoryClean=true;
        public DateTime GreetingDay=DateTime.MinValue;
        static string FileName { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml"); } }
        public static Settings Load() { var s = new Settings(); try { var r = XElement.Load(FileName); s.Monitor = (string)r.Element("Monitor") ?? ""; s.Art = (string)r.Element("Art") ?? ""; s.Topmost = (bool?)r.Element("Topmost") ?? true; s.AutoMemoryClean=(bool?)r.Element("AutoMemoryClean")??true;s.PetBubbles=(bool?)r.Element("PetBubbles")??false;s.GreetingDay=(DateTime?)r.Element("GreetingDay")??DateTime.MinValue; } catch { } return s; }
        public void Save() { try { new XElement("Settings", new XElement("AutoMemoryClean",AutoMemoryClean),new XElement("Monitor", Monitor), new XElement("Art", Art), new XElement("PetBubbles",PetBubbles),new XElement("GreetingDay",GreetingDay),new XElement("Topmost", Topmost)).Save(FileName); } catch { } }
    }
    public sealed class Dashboard : Form {
        readonly Settings settings;
        NotifyIcon trayIcon;
        Icon robotIcon;
        readonly Sensors sensors = new Sensors();
        readonly MemoryCleaner memoryCleaner=new MemoryCleaner();
        Reading reading = new Reading();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer animationTimer = new System.Windows.Forms.Timer();
        readonly CompanionAnimation animation = new CompanionAnimation(true);
        readonly List<double> cpuHistory = new List<double>(), gpuHistory = new List<double>(), netHistory = new List<double>();
        Image reference, customArt;
        int busy, ticks;
        bool fullscreen;
        Rectangle windowBounds;
        DateTime lastGood = DateTime.MinValue;
        readonly bool preview;
        WeatherReading weather = WeatherClient.LoadCache();
        int weatherBusy, quotaBusy;
        CodexQuota quota; DateTime nextQuota=DateTime.MinValue; bool quotaFailed;
        DateTime nextWeather = DateTime.MinValue;
        readonly PubgProfile pubg = PubgProfile.Load();
        readonly CompanionState companion = new CompanionState();
        readonly PetRules petRules = new PetRules();
        readonly PetVideo petVideo = new PetVideo();
        readonly PetSubtitles petSubtitles=new PetSubtitles();
        CodexActivity codexActivity = new CodexActivity();
        int activityBusy; bool activityEnabled=true; string activityDiagnostic="";
        void BeginActivity() {
            if(!activityEnabled || Interlocked.Exchange(ref activityBusy,1)==1)return;
            var currentActivity=codexActivity;
            Task.Run(delegate {try {var state=currentActivity.Poll();
                if(activityDiagnostic!=codexActivity.Diagnostic){activityDiagnostic=codexActivity.Diagnostic;try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"codex-link-status.txt"),DateTime.Now.ToString("s")+" "+activityDiagnostic);}catch(IOException){}}
                if(!IsDisposed && IsHandleCreated)BeginInvoke((Action)delegate {
                if(IsDisposed || !activityEnabled || currentActivity!=codexActivity)return;
                if(petVideo.CodexState!=state.State){petVideo.CodexState=state.State;Invalidate();}
            });}catch{}finally{Interlocked.Exchange(ref activityBusy,0);}});
        }
        public Dashboard(bool previewMode) {
            preview = previewMode; activityEnabled=BindingConfig.Load().CodexEnabled; animation.Effects=false; settings = Settings.Load();petVideo.ShowBubbles=settings.PetBubbles;petRules.LastGreetingDay=settings.GreetingDay;
            ShowInTaskbar=false;
            Text = "NEON / 副屏监控"; BackColor = Color.FromArgb(24, 20, 38);
            DoubleBuffered = true; ResizeRedraw = true; KeyPreview = true; AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual; ClientSize = new Size(1440, 360); MinimumSize = new Size(740, 225);
            using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("reference.png")) using (var img = Image.FromStream(stream)) reference = new Bitmap(img);
            if (File.Exists(settings.Art)) customArt = LoadImage(settings.Art);
            TopMost = settings.Topmost;
            using(var iconStream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("robot-app.ico"))
            using(var embeddedIcon=new Icon(iconStream))robotIcon=(Icon)embeddedIcon.Clone();
            Icon=robotIcon;
            ContextMenuStrip = BuildMenu();
            if(!preview) {
                ContextMenuStrip.Items.Insert(0,new ToolStripMenuItem("显示副屏",null,delegate { RestoreDashboard(); }));
                ContextMenuStrip.Items.Insert(1,new ToolStripSeparator());
                trayIcon=new NotifyIcon {Text="副屏监控",Icon=robotIcon,ContextMenuStrip=ContextMenuStrip,Visible=true};
                trayIcon.DoubleClick+=delegate {RestoreDashboard();};
            }
            timer.Interval = 1000; timer.Tick += delegate { ticks++; BeginQuota(); BeginActivity(); KeepOnSecondaryScreen(); BeginSample(); BeginWeather(); Invalidate(); };
            animationTimer.Interval = 33; animationTimer.Tick += delegate { if (animation.Enabled && (petVideo.Available || animation.Available) && !displayWasOff && WindowState != FormWindowState.Minimized) Invalidate(new Rectangle(0, 0, (int)Math.Ceiling(ClientSize.Width * 421f / 1110f), ClientSize.Height)); };
            if (!preview) animationTimer.Start();
            Shown += delegate { if (!preview) { MoveToPreferredScreen(); timer.Start(); BeginSample(); BeginInvoke((Action)SaveWindowDiagnostics); BeginInvoke((Action)StartCpuSensors); BeginInvoke((Action)animation.Greet); } };
            KeyDown += OnKey;
            MouseDown += delegate(object sender, MouseEventArgs e) { if(e.Button==MouseButtons.Left && !fullscreen) { ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };
            // Fullscreen changes are explicit via F11/menu; accidental double-clicks cannot move the dashboard.
            FormClosed += delegate { timer.Stop(); settings.Save(); };
        }
        void RestoreDashboard() {
            Show();WindowState=FormWindowState.Normal;MoveToPreferredScreen();Activate();
        }
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT rect);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
        void SaveWindowDiagnostics() { try { RECT r; GetWindowRect(Handle, out r); new XElement("Window", new XElement("ShowInTaskbar",ShowInTaskbar),new XElement("TrayVisible",trayIcon!=null && trayIcon.Visible),new XElement("Display", PreferredScreen().DeviceName), new XElement("X", r.Left), new XElement("Y", r.Top), new XElement("Width", r.Right-r.Left), new XElement("Height", r.Bottom-r.Top), new XElement("DPI", GetDpiForWindow(Handle)), new XElement("ClientWidth", ClientSize.Width), new XElement("ClientHeight", ClientSize.Height)).Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window-check.xml")); } catch { } }
        static Image LoadImage(string path) { using (var img = Image.FromFile(path)) return new Bitmap(img); }
        protected override void Dispose(bool disposing) { if (disposing) { if(trayIcon!=null){trayIcon.Visible=false;trayIcon.Dispose();trayIcon=null;} if(robotIcon!=null){robotIcon.Dispose();robotIcon=null;} timer.Dispose(); animationTimer.Dispose(); animation.Dispose(); petVideo.Dispose(); if (reference != null) reference.Dispose(); if (customArt != null) customArt.Dispose(); } base.Dispose(disposing); }
        DateTime lastWakeDance=DateTime.MinValue;
        IntPtr displayPowerNotification;
        bool displayWasOff;
        static readonly Guid DisplayStatusGuid=new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient,ref Guid setting,int flags);
        [DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(IntPtr handle);
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);
            if(!preview) {Guid setting=DisplayStatusGuid;displayPowerNotification=RegisterPowerSettingNotification(Handle,ref setting,0);}
        }
        protected override void OnHandleDestroyed(EventArgs e) {
            if(displayPowerNotification!=IntPtr.Zero){UnregisterPowerSettingNotification(displayPowerNotification);displayPowerNotification=IntPtr.Zero;}
            base.OnHandleDestroyed(e);
        }
        void TriggerWakeDance() {
            if(preview || IsDisposed || !IsHandleCreated || (DateTime.UtcNow-lastWakeDance).TotalSeconds<=30)return;
            lastWakeDance=DateTime.UtcNow;
            BeginInvoke((Action)delegate {if(IsDisposed)return;KeepOnSecondaryScreen();petVideo.PlayWakeDance();Invalidate();});
        }
        void ObserveDisplayPower(int state) {
            if(state==0){displayWasOff=true;animationTimer.Stop();timer.Interval=10000;}
            else if(state==1){bool wasOff=displayWasOff;displayWasOff=false;timer.Interval=1000;if(!preview)animationTimer.Start();if(wasOff)TriggerWakeDance();}
        }
        protected override void WndProc(ref Message m) {
            int message=m.Msg; long powerEvent=message==0x0218?m.WParam.ToInt64():0;
            if(message==0x0218 && powerEvent==0x8013 && m.LParam!=IntPtr.Zero) {
                Guid setting=(Guid)Marshal.PtrToStructure(m.LParam,typeof(Guid));
                if(setting==DisplayStatusGuid && Marshal.ReadInt32(m.LParam,16)==4)ObserveDisplayPower(Marshal.ReadInt32(m.LParam,20));
            }
            // Automatic resume may happen while the display stays off. Wait for visible wake.
            if(message==0x0218 && powerEvent==0x0007 && !displayWasOff)TriggerWakeDance();
            base.WndProc(ref m);
            if((message==0x02E0 || message==0x007E) && fullscreen && IsHandleCreated && !IsDisposed) BeginInvoke((Action)delegate { if(!IsDisposed && fullscreen) { Bounds=PreferredScreen().Bounds;SaveWindowDiagnostics();Invalidate(); } });
        }
        void KeepOnSecondaryScreen() {
            if(!fullscreen || preview || WindowState==FormWindowState.Minimized)return;
            var target=PreferredScreen().Bounds;
            if(FormBorderStyle!=FormBorderStyle.None)FormBorderStyle=FormBorderStyle.None;
            if(Bounds!=target)Bounds=target;
        }
        Screen PreferredScreen() { foreach (var s in Screen.AllScreens) if (s.DeviceName == settings.Monitor) return s; foreach (var s in Screen.AllScreens) if (!s.Primary) return s; return Screen.PrimaryScreen; }
        void MoveToPreferredScreen() { var screen = PreferredScreen(); settings.Monitor = screen.DeviceName; windowBounds = new Rectangle(screen.WorkingArea.Location, new Size(Math.Min(1440, screen.WorkingArea.Width), Math.Min(400, screen.WorkingArea.Height))); SetFullscreen(true); }
        void SetFullscreen(bool on) { if (on) { if (!fullscreen) windowBounds = Bounds; fullscreen = true; FormBorderStyle = FormBorderStyle.None; Bounds = PreferredScreen().Bounds; } else { fullscreen = false; FormBorderStyle = FormBorderStyle.Sizable; Bounds = windowBounds.Width > 0 ? windowBounds : new Rectangle(100, 100, 1440, 400); } Invalidate(); }
        void OnKey(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) SetFullscreen(false); if (e.KeyCode == Keys.F11) SetFullscreen(!fullscreen); if (e.KeyCode == Keys.F2) ChangeArt(); if (e.Control && e.KeyCode == Keys.Q) Close(); }
        ContextMenuStrip BuildMenu() {
            var menu = new ContextMenuStrip();
            menu.Items.Add("全屏 / 窗口    F11", null, delegate { SetFullscreen(!fullscreen); });
            var displays = new ToolStripMenuItem("移动到显示器");
            foreach (var display in Screen.AllScreens) { var captured = display; displays.DropDownItems.Add(display.DeviceName + "  " + display.Bounds.Width + " × " + display.Bounds.Height + (display.Primary ? "  主屏" : "  副屏"), null, delegate { settings.Monitor = captured.DeviceName; settings.Save(); if (!fullscreen) windowBounds = new Rectangle(captured.WorkingArea.Location, Size); SetFullscreen(true); }); }
            menu.Items.Add(displays);
            var top = new ToolStripMenuItem("始终置顶") { Checked = settings.Topmost, CheckOnClick = true }; top.CheckedChanged += delegate { TopMost = settings.Topmost = top.Checked; settings.Save(); }; menu.Items.Add(top);
            menu.Items.Add(new ToolStripSeparator());
            var moves = new ToolStripMenuItem("机器人动作");
            var animate = new ToolStripMenuItem("播放动画") { Checked = true, CheckOnClick = true }; animate.CheckedChanged += delegate { animation.Enabled = animate.Checked; Invalidate(); }; moves.DropDownItems.Add(animate);
            moves.DropDownItems.Add("挥手打招呼", null, delegate { petRules.Request("打招呼",DateTime.Now); });
            var effects = new ToolStripMenuItem("霓虹能量特效") { Checked = animation.Effects, CheckOnClick = true }; effects.CheckedChanged += delegate { animation.Effects = effects.Checked; Invalidate(); }; moves.DropDownItems.Add(effects);
            var codexLink=new ToolStripMenuItem("Codex 任务状态联动") {Checked=activityEnabled,CheckOnClick=true};
            codexLink.CheckedChanged+=delegate {activityEnabled=codexLink.Checked;var binding=BindingConfig.Load();binding.CodexEnabled=activityEnabled;binding.Save();if(!activityEnabled){petVideo.CodexState="idle";SubtitleText="";}Invalidate();};moves.DropDownItems.Add(codexLink);
            moves.DropDownItems.Add("歪头看看你", null, delegate { petRules.Request("歪头",DateTime.Now); });
            moves.DropDownItems.Add("张开手臂抱抱", null, delegate { petRules.Request("抱抱",DateTime.Now); });
            moves.DropDownItems.Add("伸懒腰", null, delegate { petRules.Request("伸懒腰",DateTime.Now); });
            var autoMoves = new ToolStripMenuItem("副屏自动互动（无需点击）") { Checked = animation.AutoPerform, CheckOnClick = true };
            autoMoves.CheckedChanged += delegate { animation.AutoPerform = autoMoves.Checked; petRules.AutoPerform=autoMoves.Checked; };
            moves.DropDownItems.Add(autoMoves);
            moves.DropDownItems.Add("忙碌情绪提示", null, delegate { petRules.Request("忙碌",DateTime.Now); });
            var bubbles=new ToolStripMenuItem("短气泡（3 秒自动消失）") {Checked=settings.PetBubbles,CheckOnClick=true};
            bubbles.CheckedChanged+=delegate {petVideo.ShowBubbles=settings.PetBubbles=bubbles.Checked;settings.Save();};moves.DropDownItems.Add(bubbles);
            menu.Items.Add(moves);
            menu.Items.Add("绑定设置 · Codex / PUBG…",null,delegate {
                using(var dialog=new BindingSettingsForm(pubg))if(dialog.ShowDialog(this)==DialogResult.OK){
                    codexActivity=new CodexActivity();activityEnabled=dialog.Config.CodexEnabled;codexLink.Checked=activityEnabled;
                    petVideo.CodexState="idle";quota=null;quotaFailed=false;nextQuota=DateTime.MinValue;BeginQuota();Invalidate();
                }
            });
            var interactions=new ToolStripMenuItem("动作预览");
            foreach(string name in PetVideo.ActionNames) {
                if(!petVideo.HasAction(name))continue;
                string action=name; interactions.DropDownItems.Add(action,null,delegate { petVideo.PreviewAction(action); });
            }
            menu.Items.Add(interactions);
            var pubgMenu = new ToolStripMenuItem("PUBG 陪玩 · " + pubg.Nickname);
            var enabled = new ToolStripMenuItem("显示陪玩状态") { Checked = pubg.Enabled, CheckOnClick = true };
            enabled.CheckedChanged += delegate { pubg.Enabled = enabled.Checked; if (!pubg.Save()) MessageBox.Show(this, "本次设置已生效，但配置暂时被占用，未能保存到磁盘。", "配置保存提示"); Invalidate(); }; pubgMenu.DropDownItems.Add(enabled);
            pubgMenu.DropDownItems.Add("昵称 / 官方接口绑定…", null, delegate { using (var dialog = new PubgSettingsForm(pubg)) dialog.ShowDialog(this); pubgMenu.Text = "PUBG 陪玩 · " + pubg.Nickname; Invalidate(); });
            pubgMenu.DropDownItems.Add("鼓励我", null, delegate { petRules.Request("打招呼",DateTime.Now); companion.Say("稳住节奏，先做好眼前这一步。", DateTime.Now); Invalidate(); });
            pubgMenu.DropDownItems.Add("庆祝一下（手动）", null, delegate { petRules.Request("比心",DateTime.Now); companion.Say("这一下漂亮！为你庆祝。", DateTime.Now); Invalidate(); });
            pubgMenu.DropDownItems.Add("这局可惜了（手动）", null, delegate { companion.Say("可惜了，休息一下，下局再来。", DateTime.Now); Invalidate(); });
            menu.Items.Add(pubgMenu);
            var cleanMemory=new ToolStripMenuItem("内存超过 80% 自动清理") {Checked=settings.AutoMemoryClean,CheckOnClick=true};
            cleanMemory.CheckedChanged+=delegate {settings.AutoMemoryClean=cleanMemory.Checked;settings.Save();};menu.Items.Add(cleanMemory);
            menu.Items.Add("刷新长春天气", null, delegate { nextWeather = DateTime.MinValue; BeginWeather(); });
            menu.Items.Add("启用 / 重试 CPU 温度采集", null, delegate { StartCpuSensors(); });
            menu.Items.Add("更换左侧图片    F2", null, delegate { ChangeArt(); });
            menu.Items.Add("恢复机器人动画", null, delegate { if (customArt != null) { customArt.Dispose(); customArt = null; } settings.Art = ""; settings.Save(); Invalidate(); });
            menu.Items.Add("使用说明", null, delegate { MessageBox.Show(this, "F11：切换全屏\nEsc：退出全屏\nF2：更换人物图片\n右键：选择显示器、置顶或退出\nCtrl+Q：退出\n\n数据每秒采样。CPU 时钟为系统报告值。\nGPU 数据来自 NVIDIA 驱动。CPU 温度和封装功耗由随附采集器读取，需要管理员授权。\n磁盘显示系统盘容量使用率。\n显存百分比为 NVIDIA 报告的已用显存 / 总显存。", "副屏监控 · 使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information); });
            menu.Items.Add("退出    Ctrl+Q", null, delegate { Close(); }); return menu;
        }
        void ChangeArt() { using (var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif", Title = "选择左侧图片" }) { if (dialog.ShowDialog(this) != DialogResult.OK) return; try { Image next = LoadImage(dialog.FileName); if (customArt != null) customArt.Dispose(); customArt = next; settings.Art = dialog.FileName; settings.Save(); Invalidate(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "图片读取失败"); } } }
        Process cpuBridge;
        void StartCpuSensors() {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sensors", "LibreHardwareMonitor", "CpuSensorBridge.exe");
            if (!File.Exists(path)) return;
            try { if (cpuBridge != null && !cpuBridge.HasExited) return; } catch { }
            try { cpuBridge = Process.Start(new ProcessStartInfo(path, Process.GetCurrentProcess().Id.ToString()) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Path.GetDirectoryName(path) }); }
            catch (Exception ex) { try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cpu-sensor-status.txt"), "CPU sensor start: " + ex.Message); } catch { } }
        }
        void BeginSample() {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            Task.Run(delegate { try { var next = sensors.Poll(); if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { if (IsDisposed) return; reading = next; lastGood = DateTime.Now; companion.Update(next.PubgRunning, next.Cpu, DateTime.Now); UpdatePet(next); memoryCleaner.Observe(next.Memory,settings.AutoMemoryClean); Push(cpuHistory, next.Cpu); Push(gpuHistory, Math.Max(0, next.Gpu)); Push(netHistory, next.Upload + next.Download); Invalidate(); }); } catch { } finally { Interlocked.Exchange(ref busy, 0); } });
        }
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
        void UpdatePet(Reading next) {
            var signal=new PetSignals { Cpu=next.Cpu, Temperature=next.CpuTemp, Memory=next.Memory, Vram=next.VramPercent, IdleSeconds=PetRules.IdleSeconds(), Download=next.Download, Game=next.PubgRunning };
            try { var drive=new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory));signal.DiskFree=100.0*drive.AvailableFreeSpace/drive.TotalSize; }catch{}
            var power=SystemInformation.PowerStatus;
            signal.OnBattery=power.PowerLineStatus==PowerLineStatus.Offline && (power.BatteryChargeStatus&BatteryChargeStatus.NoSystemBattery)==0;
            if(power.BatteryLifePercent>=0)signal.Battery=power.BatteryLifePercent*100;
            try { uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);using(var process=Process.GetProcessById((int)pid)) {
                string name=process.ProcessName.ToLowerInvariant();
                if(name=="code"||name=="devenv"||name=="idea64"||name=="pycharm64")signal.App="coding";
                if(name=="zoom"||name=="wemeetapp"||name=="ms-teams")signal.App="meeting";
            }}catch{}
            if(weather!=null && (DateTime.UtcNow-weather.Received).TotalHours<2 && weather.Day.Date==DateTime.Now.Date) {
                signal.Outdoor=weather.Temperature;signal.Precipitation=weather.Code>=51 && weather.Code<=99;
            }
            petRules.Update(signal,DateTime.Now);petVideo.Observe(petRules);if(settings.GreetingDay!=petRules.LastGreetingDay){settings.GreetingDay=petRules.LastGreetingDay;settings.Save();}
        }
        void BeginQuota() {
            if(!activityEnabled || DateTime.UtcNow<nextQuota || Interlocked.Exchange(ref quotaBusy,1)==1)return;
            nextQuota=DateTime.UtcNow.AddMinutes(5);
            Task.Run(delegate {
                try {var result=CodexQuota.Fetch();if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate{quota=result;quotaFailed=false;Invalidate();});}
                catch {if(!IsDisposed&&IsHandleCreated)BeginInvoke((Action)delegate{quotaFailed=true;Invalidate();});}
                finally{Interlocked.Exchange(ref quotaBusy,0);}
            });
        }
        void BeginWeather() {
            if (DateTime.UtcNow < nextWeather || Interlocked.Exchange(ref weatherBusy, 1) == 1) return;
            nextWeather = DateTime.UtcNow.AddMinutes(15);
            Task.Run(delegate {
                try { var next = WeatherClient.Fetch(); if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { weather = next; Invalidate(); }); }
                catch { if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)delegate { if (weather != null) weather.Cached = true; nextWeather = DateTime.UtcNow.AddMinutes(2); Invalidate(); }); }
                finally { Interlocked.Exchange(ref weatherBusy, 0); }
            });
        }
        static void Push(List<double> list, double v) { list.Add(v); if (list.Count > 48) list.RemoveAt(0); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); Render(e.Graphics, ClientSize.Width, ClientSize.Height,e.ClipRectangle.Right>ClientSize.Width*421f/1110f+2); }
        static Color Ink = Color.FromArgb(14, 18, 23);
        static GraphicsPath Rounded(float x, float y, float w, float h, float radius) { var p = new GraphicsPath(); float d = radius * 2; p.AddArc(x, y, d, d, 180, 90); p.AddArc(x + w - d, y, d, d, 270, 90); p.AddArc(x + w - d, y + h - d, d, d, 0, 90); p.AddArc(x, y + h - d, d, d, 90, 90); p.CloseFigure(); return p; }
        void Card(Graphics g, float x, float y, float w, float h) { using (var p = Rounded(x, y, w, h, 14)) using (var b = new LinearGradientBrush(new RectangleF(x, y, w, h), Color.FromArgb(228, 209, 222, 226), Color.FromArgb(231, 205, 202, 206), 90)) { g.FillPath(b, p); using (var pen = new Pen(Color.FromArgb(130, 76, 215, 246), 1.2f)) g.DrawPath(pen, p); using (var glow = new Pen(Color.FromArgb(25, 125, 98, 255), 4f)) g.DrawPath(glow, p); using (var line = new Pen(Color.FromArgb(190, 57, 187, 228), 2f)) g.DrawLine(line, x + 15, y + 1, x + Math.Min(w - 15, 66), y + 1); } }
        static void TextAt(Graphics g, string text, float x, float y, float size, Color color, string family, FontStyle style) { using (var f = new Font(family, size, style, GraphicsUnit.Pixel)) using (var b = new SolidBrush(color)) g.DrawString(text, f, b, x, y, StringFormat.GenericTypographic); }
        static void CenterLabel(Graphics g,string text,RectangleF box,float size,string family,Color color) {
            var clip=g.Save();g.SetClip(box,System.Drawing.Drawing2D.CombineMode.Intersect);
            using(var font=new Font(family,size,FontStyle.Regular,GraphicsUnit.Pixel))using(var brush=new SolidBrush(color))
            using(var format=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})g.DrawString(text,font,brush,box,format);
            g.Restore(clip);
        }
        static void T(Graphics g, string text, float x, float y, float size) { TextAt(g, text, x, y, size, Ink, "Bahnschrift", FontStyle.Regular); }
        static void Fit(Graphics g, string text, float x, float y, float size, float maxWidth, string family) { using (var f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel)) { float width = g.MeasureString(text, f).Width; var state = g.Save(); g.TranslateTransform(x, y); if (width > maxWidth) g.ScaleTransform(maxWidth / width, 1); using (var b = new SolidBrush(Ink)) g.DrawString(text, f, b, 0, 0, StringFormat.GenericTypographic); g.Restore(state); } }
        static string Value(double number, string suffix) { return number < 0 ? "—" : number.ToString("0", CultureInfo.InvariantCulture) + suffix; }
        static string Rate(double bytes) { if (bytes >= 1048576) return (bytes / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " MB/S"; return (bytes / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " KB/S"; }
        void Graph(Graphics g, RectangleF r, List<double> history, double max) {
            using (var pen = new Pen(Color.FromArgb(58, 42, 65, 72), .55f)) { for (float x = r.Left; x <= r.Right; x += 5) g.DrawLine(pen, x, r.Top, x, r.Bottom); for (float y = r.Top; y <= r.Bottom; y += 5) g.DrawLine(pen, r.Left, y, r.Right, y); }
            if (history.Count < 2) return; var points = new PointF[history.Count]; float step = r.Width / 47;
            for (int j = 0; j < history.Count; j++) points[j] = new PointF(r.Right - (history.Count - 1 - j) * step, r.Bottom - (float)Math.Min(1, Math.Max(0, history[j] / max)) * r.Height);
            using (var pen = new Pen(Color.FromArgb(195, 163, 71, 206), 1.5f)) g.DrawLines(pen, points);
        }
        void Fan(Graphics g, float x, float y, float radius) { var state = g.Save(); g.TranslateTransform(x, y); g.RotateTransform(ticks * 24); using (var b = new SolidBrush(Color.FromArgb(190, 44, 48, 48))) { for (int j = 0; j < 9; j++) { g.RotateTransform(40); using (var p = new GraphicsPath()) { p.AddBezier(3, -2, radius, -radius, radius + 3, -4, 8, 7); p.AddLine(8, 7, 3, -2); g.FillPath(b, p); } } g.FillEllipse(b, -7, -7, 14, 14); } g.Restore(state); }
        public string SubtitleText = "";
        void DrawSubtitle(Graphics g,int width,int height,int barHeight) {
            string subtitle=string.IsNullOrEmpty(SubtitleText)?petSubtitles.Resolve(petRules.State,petVideo.CodexState,petVideo.ActiveCaptionKey,petVideo.ActionSerial,petVideo.IdleName,petRules.Night,DateTime.UtcNow):SubtitleText;
            if(string.IsNullOrEmpty(subtitle))return;
            // Plain video subtitles over the character; no panel or reserved space.
            float leftWidth=width*421f/1110f;
            float size=Math.Max(16,height*.0583f);
            var box=new RectangleF(18,height-size*2.05f,leftWidth-36,size*1.6f);
            using(var font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel))
            using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap}) {
                using(var path=new GraphicsPath()) {
                    path.AddString(subtitle,font.FontFamily,(int)FontStyle.Bold,size,box,format);
                    using(var shadow=(GraphicsPath)path.Clone())using(var shift=new Matrix()) {
                        shift.Translate(0,size*.09f);shadow.Transform(shift);
                        using(var halo=new Pen(Color.FromArgb(80,0,0,0),size*.28f){LineJoin=LineJoin.Round})g.DrawPath(halo,shadow);
                    }
                    using(var outline=new Pen(Color.FromArgb(245,10,12,22),size*.14f){LineJoin=LineJoin.Round})g.DrawPath(outline,path);
                    using(var text=new SolidBrush(Color.White))g.FillPath(text,path);
                }
            }
        }
        public void Render(Graphics g, int width, int height,bool repaintDashboard=true) {
            g.Clear(Color.FromArgb(29, 22, 45)); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int subtitleHeight=0;
            var root = g.Save(); g.ScaleTransform(width / 1110f, (height-subtitleHeight) / 314f);
            if (repaintDashboard && reference != null) g.DrawImage(reference, new Rectangle(0, 0, 1110, 314));
            if(repaintDashboard) using (var b = new LinearGradientBrush(new Rectangle(422, 29, 688, 285), Color.FromArgb(53, 39, 65), Color.FromArgb(112, 63, 101), 90f)) g.FillRectangle(b, 422, 29, 688, 285);
            if (customArt != null) { var clip = g.Save(); g.SetClip(new Rectangle(0, 0, 421, 314)); float scale = Math.Max(421f / customArt.Width, 314f / customArt.Height); float w = customArt.Width * scale, h = customArt.Height * scale; g.DrawImage(customArt, (421 - w) / 2, (314 - h) / 2, w, h); g.Restore(clip); }
            else if (petVideo.Available) { petVideo.Enabled=animation.Enabled; petVideo.Draw(g,new RectangleF(0,0,421,314),petRules,animation.Effects); }
            else if (animation.Available) { animation.Observe(companion.Running); animation.Draw(g, new RectangleF(0, 0, 421, 314), companion.Busy, companion.Running, reading.CpuTemp >= 80); }
            if(repaintDashboard) {
            using (var b = new LinearGradientBrush(new Rectangle(422, 0, 688, 31), Color.FromArgb(0, 153, 206), Color.FromArgb(5, 49, 85), 90)) g.FillRectangle(b, 422, 0, 688, 31);
            using (var p = new Pen(Color.FromArgb(110, 73, 232, 255), 2)) g.DrawLine(p, 432, 27, 1104, 27);
            DateTime now = DateTime.Now;
            TextAt(g, now.ToString("HH:mm:ss"), 436, 2, 22, Color.White, "Bahnschrift", FontStyle.Bold);
            TextAt(g, now.ToString("yyyy年MM月dd日") + "  " + ChineseDate.Weekday(now), 552, 6, 14, Color.White, "Microsoft YaHei UI", FontStyle.Regular);
            TextAt(g, ChineseDate.Lunar(now), 757, 6, 14, Color.FromArgb(219, 247, 255), "Microsoft YaHei UI", FontStyle.Regular);
            bool quotaStale=quota!=null && (quotaFailed||(DateTime.UtcNow-quota.Updated).TotalMinutes>10||(quota.Reset!=DateTime.MinValue&&DateTime.UtcNow>=quota.Reset));
            string quotaText=!activityEnabled?"codex 未启用联动":quota==null?(quotaFailed?"codex 周额度未连接":"codex 周额度读取中"):"codex 周"+quota.Remaining.ToString("0")+"%"+(quota.Reset==DateTime.MinValue?"":" "+quota.Reset.ToLocalTime().ToString("MM-dd HH:mm")+"重置")+(quotaStale?" · 旧数据":"");
            TextAt(g,quotaText,899,8,10,quotaStale?Color.LightGray:Color.White,"Microsoft YaHei UI",FontStyle.Regular);
            using (var p = Rounded(420, 29, 683, 258, 22)) using (var b = new SolidBrush(Color.FromArgb(235, 33, 32, 38))) g.FillPath(b, p);
            Card(g, 429, 37, 318, 116); Card(g, 429, 162, 318, 125); Card(g, 757, 37, 119, 116); Card(g, 757, 162, 119, 125); Card(g, 885, 37, 210, 116); Card(g, 885, 162, 210, 125);
            ComputeCard(g, false); ComputeCard(g, true);
            T(g, "P O W E R", 775, 45, 18);
            T(g, "CPU + GPU", 782, 69, 13);
            Fit(g, Value(reading.ComponentPower, " W"), 774, 87, 39, 91, "Impact");
            T(g, "COMPONENT TOTAL", 772, 133, 9);
            T(g, "MEM UTILISATION", 897, 45, 19); Fit(g, reading.MemorySpec, 898, 66, 9, 180, "Bahnschrift");
            Fit(g, reading.Memory.ToString("0") + "%", 899, 78, 64, 111, "Impact"); T(g, "MEMORY", 1009, 88, 9); Fit(g, reading.MemorySpec.Split(' ')[0], 1009, 99, 20, 76, "Bahnschrift"); T(g, "FREE MEM", 1009, 120, 9); Fit(g, reading.FreeMemory + " MB", 1009, 131, 18, 78, "Bahnschrift");
            Fit(g, "N E T W O R K", 771, 171, 17, 96, "Bahnschrift"); double netMax = 1024; foreach (double value in netHistory) netMax = Math.Max(netMax, value * 1.1); Graph(g, new RectangleF(770, 194, 94, 29), netHistory, netMax); T(g, "UPLOAD", 782, 235, 10); Fit(g, Rate(reading.Upload), 783, 248, 22, 81, "Bahnschrift");
            T(g, "VRAM UTILISATION", 897, 170, 18);
            CenterLabel(g,Value(reading.VramPercent,"%"),new RectangleF(898,190,184,43),38,"Impact",Ink);
            T(g,"REFRESH RATE",899,234,9);
            CenterLabel(g,RefreshRate()+" HZ",new RectangleF(1030,231,53,18),17,"Bahnschrift",Ink);
            using(var pill=Rounded(895,252,190,29,12))using(var fill=new SolidBrush(Color.FromArgb(241,240,243,245)))g.FillPath(fill,pill);
            using(var divider=new Pen(Color.FromArgb(130,154,166,176),.7f))g.DrawLine(divider,990,257,990,277);
            CenterLabel(g,"CPU OUTPUT",new RectangleF(899,254,88,10),8,"Bahnschrift",Ink);
            CenterLabel(g,"GPU OUTPUT",new RectangleF(994,254,87,10),8,"Bahnschrift",Ink);
            CenterLabel(g,Value(reading.CpuPower," W"),new RectangleF(899,264,88,16),14,"Bahnschrift",Ink);
            CenterLabel(g,Value(reading.GpuPower," W"),new RectangleF(994,264,87,16),14,"Bahnschrift",Ink);
            using (var b = new LinearGradientBrush(new Rectangle(422, 291, 688, 23), Color.FromArgb(89, 49, 92), Color.FromArgb(26, 35, 50), 0f)) g.FillRectangle(b, 422, 291, 688, 23);
            var weatherClip=g.Save();g.SetClip(new RectangleF(429,292,548,22));
            DateTime weatherNow=DateTime.UtcNow.AddHours(8);
            bool currentWeather=weather!=null && weather.Day.Date==weatherNow.Date && (weatherNow-weather.Observed).TotalHours<=2 && weather.Observed<=weatherNow.AddMinutes(30);
            DrawWeatherIcon(g,432,294,currentWeather?weather.Code:-1);
            TextAt(g, weather == null ? "吉林长春  天气正在连接…" : weather.Summary(), 457, 295, 12, Color.FromArgb(244, 239, 250), "Microsoft YaHei UI", FontStyle.Regular);
            g.Restore(weatherClip);
            string state = lastGood == DateTime.MinValue ? "CONNECTING" : (DateTime.Now - lastGood).TotalSeconds > 8 ? "STALE DATA" : "LIVE  /  " + reading.Timestamp.ToString("HH:mm:ss");
            TextAt(g, "Open-Meteo" + (weather == null ? "" : " " + weather.Observed.ToString("HH:mm")), 991, 294, 8, Color.FromArgb(217, 239, 245), "Bahnschrift", FontStyle.Regular);
            TextAt(g, state, 991, 304, 7, Color.FromArgb(217, 239, 245), "Bahnschrift", FontStyle.Regular);
            }
            g.Restore(root);
            DrawVideoWindowFrame(g,width,height);
            DrawSubtitle(g,width,height,subtitleHeight);
        }
        static void DrawVideoWindowFrame(Graphics g,int width,int height) {
            float scale=height/480f, leftWidth=width*421f/1110f;
            // A narrow metal window rim overlays only the outermost pixels of the video.
            var saved=g.Save();g.SetClip(new RectangleF(0,0,leftWidth,height));
            using(var rim=new Pen(Color.FromArgb(18,29,40),5*scale))
                g.DrawRectangle(rim,2.5f*scale,2.5f*scale,leftWidth-5*scale,height-5*scale);
            using(var edge=new Pen(Color.FromArgb(175,107,200,222),scale))
                g.DrawRectangle(edge,5.5f*scale,5.5f*scale,leftWidth-11*scale,height-11*scale);
            using(var sheen=new Pen(Color.FromArgb(145,164,190,205),scale)) {
                g.DrawLine(sheen,scale,scale,leftWidth-scale,scale);
                g.DrawLine(sheen,scale,scale,scale,height-scale);
            }
            g.Restore(saved);
        }
        void ComputeCard(Graphics g, bool gpu) { float y = gpu ? 162 : 37; T(g, gpu ? "GPU UTILISATION" : "CPU UTILISATION", 440, y + 8, 19); Fit(g, gpu ? reading.GpuName.ToUpperInvariant() : reading.CpuName.ToUpperInvariant(), 440, y + 29, 9, 146, "Bahnschrift"); Graph(g, new RectangleF(592, y + 8, 144, 34), gpu ? gpuHistory : cpuHistory, 100); Fit(g, Value(gpu ? reading.Gpu : reading.Cpu, "%"), 475, y + 39, 69, 110, "Impact"); T(g, "CORE CLOCK", 592, y + 50, 9); T(g, gpu ? Value(reading.GpuClock, " MHZ") : reading.CpuClock > 0 ? reading.CpuClock.ToString("0.00") + " GHZ" : "—", 592, y + 61, 20); T(g, "CORE TEMP", 592, y + 82, 9); T(g, Value(gpu ? reading.GpuTemp : reading.CpuTemp, "°"), 607, y + 94, 20); Fan(g, 708, y + 78, 24); }
        string refreshRateCache="—";DateTime nextRefreshRate=DateTime.MinValue;
        string RefreshRate() { if(DateTime.UtcNow<nextRefreshRate)return refreshRateCache;nextRefreshRate=DateTime.UtcNow.AddSeconds(30);try { DEVMODE dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)); if (EnumDisplaySettings(PreferredScreen().DeviceName, -1, ref dm) && dm.dmDisplayFrequency > 1) return refreshRateCache=dm.dmDisplayFrequency.ToString(); } catch { } return "—"; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DEVMODE { [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmDeviceName; public short dmSpecVersion,dmDriverVersion,dmSize,dmDriverExtra; public int dmFields,dmPositionX,dmPositionY,dmDisplayOrientation,dmDisplayFixedOutput; public short dmColor,dmDuplex,dmYResolution,dmTTOption,dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel,dmPelsWidth,dmPelsHeight,dmDisplayFlags,dmDisplayFrequency,dmICMMethod,dmICMIntent,dmMediaType,dmDitherType,dmReserved1,dmReserved2,dmPanningWidth,dmPanningHeight; }
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string name, int mode, ref DEVMODE dm);
        static void DrawWeatherIcon(Graphics g,float x,float y,int code) {
            var saved=g.Save();g.TranslateTransform(x,y);
            using(var sun=new SolidBrush(Color.FromArgb(255,218,98)))
            using(var cloud=new SolidBrush(Color.FromArgb(231,242,255)))
            using(var ray=new Pen(Color.FromArgb(255,218,98),1.3f))
            using(var water=new Pen(Color.FromArgb(108,224,255),1.4f)) {
                ray.StartCap=ray.EndCap=water.StartCap=water.EndCap=LineCap.Round;
                if(code<0 || WeatherClient.Condition(code)=="天气未知") {
                    g.DrawArc(water,4,3,12,12,25,280);g.DrawLine(water,17,3,17,7);g.DrawLine(water,17,7,13,7);
                } else {
                    if(code<=2) {
                        float cx=code==0?10:7,cy=code==0?9:6;
                        for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(ray,cx+(float)Math.Cos(a)*5.5f,cy+(float)Math.Sin(a)*5.5f,cx+(float)Math.Cos(a)*7.5f,cy+(float)Math.Sin(a)*7.5f);}
                        g.FillEllipse(sun,cx-4,cy-4,8,8);
                    }
                    if(code>0) {
                        g.FillEllipse(cloud,3,7,9,7);g.FillEllipse(cloud,7,3,10,11);g.FillEllipse(cloud,13,7,8,7);g.FillRectangle(cloud,7,9,10,5);
                        if(code==45 || code==48){g.DrawLine(water,3,16,19,16);g.DrawLine(water,6,19,17,19);}
                        else if((code>=71&&code<=77)||code==85||code==86){
                            for(int i=0;i<2;i++){float cx=7+i*9;g.DrawLine(water,cx-2,17,cx+2,17);g.DrawLine(water,cx,15,cx,19);g.DrawLine(water,cx-1.5f,15.5f,cx+1.5f,18.5f);}
                        } else if(code>=95){g.FillPolygon(sun,new[]{new PointF(11,12),new PointF(7,17),new PointF(11,17),new PointF(9,21),new PointF(16,15),new PointF(12,15)});}
                        else if(code>=51){for(int i=0;i<3;i++)g.DrawLine(water,6+i*6,16,4+i*6,19);}
                    }
                }
            }
            g.Restore(saved);
        }
        public void Snapshot(string path) { try { quota=CodexQuota.Fetch(); } catch {quotaFailed=true;} try { weather = WeatherClient.Fetch(); } catch { } reading = sensors.Poll(); Thread.Sleep(1100); reading = sensors.Poll(); lastGood = DateTime.Now; Push(cpuHistory, reading.Cpu); Push(gpuHistory, Math.Max(0, reading.Gpu)); using (var b = new Bitmap(1920, 480)) { using (var g = Graphics.FromImage(b)) Render(g, 1920, 480); b.Save(path, ImageFormat.Png); } var xml = new XElement("Diagnostics", new XElement("CPU", reading.CpuName), new XElement("CPULoad", reading.Cpu), new XElement("CPUTemperature", reading.CpuTemp), new XElement("CPUPackagePower", reading.CpuPower), new XElement("GPU", reading.GpuName), new XElement("GPULoad", reading.Gpu), new XElement("GPUTemperature", reading.GpuTemp), new XElement("GPUPower", reading.GpuPower), new XElement("VRAMUsedMiB", reading.VramUsed), new XElement("VRAMTotalMiB", reading.VramTotal), new XElement("VRAMPercent", reading.VramPercent), new XElement("MemoryLoad", reading.Memory), new XElement("MemorySpec", reading.MemorySpec), new XElement("DiskLoad", reading.Disk), new XElement("Adapter", reading.Adapter), new XElement("UploadBytesPerSecond", reading.Upload), new XElement("DownloadBytesPerSecond", reading.Download), new XElement("Display", PreferredScreen().DeviceName), new XElement("RefreshHz", RefreshRate())); xml.Save(Path.ChangeExtension(path, ".xml")); }
    }
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);
        [STAThread] static void Main(string[] args) {
            try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDpiAwareness(2); } catch { try { SetProcessDpiAwareness(2); } catch { SetProcessDPIAware(); } }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try { if (args.Length >= 2 && args[0] == "--snapshot") { using (var f = new Dashboard(true)) f.Snapshot(Path.GetFullPath(args[1])); return; }
                bool first; using (var mutex = new Mutex(true, "Local\\NeonSideScreenMonitor", out first)) { if (!first) { MessageBox.Show("副屏监控已在运行，请查看副屏。", "副屏监控"); return; } Application.Run(new Dashboard(false)); }
            } catch (Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), ex.ToString()); MessageBox.Show(ex.Message, "副屏监控启动失败"); }
        }
    }
}
