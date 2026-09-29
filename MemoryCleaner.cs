using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
namespace SideScreenMonitor {
 public sealed class MemoryCleaner {
  DateTime highSince=DateTime.MinValue,lastSample=DateTime.MinValue,nextRun=DateTime.MinValue;int busy;
  public bool ShouldRun(double percent,DateTime now,bool enabled) {
   if(!enabled || double.IsNaN(percent) || percent<=80 || percent>100){highSince=DateTime.MinValue;lastSample=now;return false;}
   if(highSince==DateTime.MinValue || (now-lastSample).TotalSeconds>5)highSince=now;
   lastSample=now;
   if((now-highSince).TotalSeconds<30 || now<nextRun)return false;
   highSince=DateTime.MinValue;nextRun=now.AddMinutes(10);return true;
  }
  public void Observe(double percent,bool enabled) {
   if(Volatile.Read(ref busy)!=0 || !ShouldRun(percent,DateTime.UtcNow,enabled))return;
   if(Interlocked.Exchange(ref busy,1)!=0)return;
   Task.Run(delegate {try {Clean();}catch(Exception e){Log("清理未完成："+e.GetType().Name);}finally{Interlocked.Exchange(ref busy,0);}});
  }
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint id);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
  [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int id);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
  [DllImport("psapi.dll",SetLastError=true)] static extern bool EmptyWorkingSet(IntPtr h);
  static void Log(string text){try {File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"memory-clean-status.txt"),DateTime.Now.ToString("s")+" "+text);}catch(IOException){} }
  static void Clean() {
   int successful=0,eligible=0;long reduction=0;
   int session;using(var own=Process.GetCurrentProcess())session=own.SessionId;
   uint foreground;GetWindowThreadProcessId(GetForegroundWindow(),out foreground);
   string foregroundName="";try{using(var f=Process.GetProcessById((int)foreground))foregroundName=f.ProcessName;}catch{}
   foreach(var p in Process.GetProcesses())using(p) {
    try {
     string name=p.ProcessName;
     // Only minimized desktop apps, excluding work tools, shells and games.
     if(p.SessionId!=session || p.Id==foreground || name==foregroundName || p.MainWindowHandle==IntPtr.Zero || !IsIconic(p.MainWindowHandle))continue;
     string n=name.ToLowerInvariant();
     if(n.Contains("codex") || n=="code" || n.Contains("studio") || n.Contains("tslgame") || n.Contains("steam") || n=="explorer" || n.Contains("副屏") || n.Contains("powershell") || n=="cmd" || n.Contains("terminal"))continue;
     long before=p.WorkingSet64;if(before<100L*1024*1024)continue;
     eligible++;GetWindowThreadProcessId(GetForegroundWindow(),out foreground);
     if(p.Id==foreground || !IsIconic(p.MainWindowHandle))continue;
     IntPtr handle=OpenProcess(0x1000|0x0100,false,p.Id);if(handle==IntPtr.Zero)continue;
     try {if(EmptyWorkingSet(handle)){successful++;p.Refresh();reduction+=Math.Max(0,before-p.WorkingSet64);}}finally{CloseHandle(handle);}
     if(successful>=5)break;
    }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
   }
   Log("自动清理完成；符合条件 "+eligible+" 个，成功 "+successful+" 个；工作集合计减少约 "+(reduction/1024/1024)+" MB（非系统实际释放量），冷却 10 分钟。");
  }
 }
}
