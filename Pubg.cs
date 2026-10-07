using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Xml.Linq;

namespace SideScreenMonitor {
    public static class ProfileStorage {
        public static XElement Read(string path) {
            for (int attempt = 0; ; attempt++) {
                try { using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) return XElement.Load(stream); }
                catch (IOException) { if (attempt >= 12) throw; Thread.Sleep(10); }
            }
        }
        public static bool Write(string path, XElement xml) {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { xml.Save(stream); stream.Flush(true); }
                for (int attempt = 0; attempt < 5; attempt++) {
                    try { if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path); return true; }
                    catch (IOException) { if (attempt == 4) return false; Thread.Sleep(30); }
                }
            } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
            return false;
        }
    }
    public sealed class PubgProfile {
        public string Nickname = "", AccountId = "";
        public bool Enabled = true;
        static string PathName { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pubg-profile.xml"); } }
        public static PubgProfile Load() { var p = new PubgProfile(); try { var xml = ProfileStorage.Read(PathName); p.Nickname = (string)xml.Element("Nickname") ?? ""; p.AccountId = (string)xml.Element("AccountId") ?? ""; p.Enabled = (bool?)xml.Element("Enabled") ?? true; } catch { } return p; }
        public bool Save() { return ProfileStorage.Write(PathName, new XElement("PubgProfile", new XElement("Platform", "steam"), new XElement("Nickname", Nickname), new XElement("AccountId", AccountId), new XElement("Enabled", Enabled))); }
    }
    public static class PubgApi {
        static string KeyPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeonSideScreenMonitor", "pubg-key.bin"); } }
        public static string LoadKey() { try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser)); } catch { return ""; } }
        public static void SaveKey(string key) { Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)); File.WriteAllBytes(KeyPath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser)); }
        public static string ParsePlayer(string json, string nickname, out int matchCount) {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var rows = (IList)root["data"]; matchCount = 0;
            foreach (Dictionary<string, object> row in rows) {
                var attrs = (Dictionary<string, object>)row["attributes"];
                if (!string.Equals(Convert.ToString(attrs["name"]), nickname, StringComparison.OrdinalIgnoreCase)) continue;
                var relations = (Dictionary<string, object>)row["relationships"];
                matchCount = ((IList)((Dictionary<string, object>)relations["matches"])["data"]).Count;
                string id = Convert.ToString(row["id"]); if (!id.StartsWith("account.", StringComparison.Ordinal)) throw new InvalidDataException("玩家资料格式异常");
                return id;
            }
            throw new InvalidDataException("没有找到这个 Steam PUBG 昵称，请核对拼写。");
        }
        public static string Verify(string nickname, string key, out int matchCount) {
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("请在本机填写 PUBG API 密钥。");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create("https://api.pubg.com/shards/steam/players?filter%5BplayerNames%5D=" + Uri.EscapeDataString(nickname));
            request.Timeout = 10000; request.ReadWriteTimeout = 10000; request.AllowAutoRedirect = false;
            request.Accept = "application/vnd.api+json"; request.Headers[HttpRequestHeader.Authorization] = "Bearer " + key;
            try { using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream())) return ParsePlayer(reader.ReadToEnd(), nickname, out matchCount); }
            catch (WebException ex) {
                var response = ex.Response as HttpWebResponse;
                if (response != null) { using (response) { int code = (int)response.StatusCode; if (code == 401 || code == 403) throw new InvalidOperationException("API 密钥无效或没有访问权限。"); if (code == 404) throw new InvalidOperationException("未找到玩家，请核对昵称和 Steam 平台。"); if (code == 429) throw new InvalidOperationException("请求过于频繁，请一分钟后重试。"); } }
                throw new InvalidOperationException("PUBG 服务暂时无法连接，请稍后重试。");
            }
        }
        public static bool IsRunning() { var processes = Process.GetProcessesByName("TslGame"); try { return processes.Length > 0; } finally { foreach (var p in processes) p.Dispose(); } }
    }
    public sealed class CompanionState {
        public bool Running, Busy;
        public DateTime SessionStart, LastBubble = DateTime.MinValue, BubbleUntil;
        DateTime highSince = DateTime.MinValue, lowSince = DateTime.MinValue;
        public string Bubble = "", Mood = "安静陪伴";
        public void Say(string text, DateTime now) { Bubble = text; LastBubble = now; BubbleUntil = now.AddSeconds(8); }
        public void Update(bool running, double cpu, DateTime now) {
            if (running != Running) { Running = running; SessionStart = now; Say(running ? "准备出发，稳稳打，我陪你。" : "游戏结束了，放松一下肩膀。", now); }
            if (cpu > 60) { lowSince = DateTime.MinValue; if (highSince == DateTime.MinValue) highSince = now; if (!Busy && (now-highSince).TotalSeconds >= 300) { Busy = true; if (!running && (now-LastBubble).TotalSeconds >= 120) Say("忙起来了，一件一件来。", now); } }
            else if (cpu < 40) { highSince = DateTime.MinValue; if (lowSince == DateTime.MinValue) lowSince = now; if (Busy && (now-lowSince).TotalSeconds >= 8) { Busy = false; if (!running && (now-LastBubble).TotalSeconds >= 120) Say("这一阵忙完了，辛苦啦。", now); } }
            else { highSince = lowSince = DateTime.MinValue; }
            Mood = Busy ? "忙碌" : running ? "游戏陪伴" : "安静陪伴";
        }
    }
    public sealed class PubgSettingsForm : PrimarySettingsForm {
        public readonly PubgProfile Profile;
        readonly TextBox nickname = new TextBox(), key = new TextBox();
        readonly Label status = new Label();
        readonly Button verify = new Button(), save = new Button();
        bool checking;
        public PubgSettingsForm(PubgProfile profile) {
            Profile = profile; Text = "Steam PUBG · 绑定设置"; ClientSize = new Size(540, 280); FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.Manual; MaximizeBox = false; MinimizeBox = false;
            Controls.Add(new Label { Text = "游戏昵称（Steam PUBG）", Left = 20, Top = 18, Width = 400 });
            nickname.SetBounds(20, 45, 495, 26); nickname.Text = profile.Nickname; Controls.Add(nickname);
            Controls.Add(new Label { Text = "PUBG API 密钥（仅保存在本机，留空使用已保存密钥）", Left = 20, Top = 82, Width = 500 });
            key.SetBounds(20, 108, 495, 26); key.UseSystemPasswordChar = true; Controls.Add(key);
            var link = new LinkLabel { Text = "打开 PUBG 官方开发者页面申请 API 密钥", Left = 20, Top = 146, Width = 470 }; link.LinkClicked += delegate { Process.Start("https://developer.pubg.com/"); }; Controls.Add(link);
            status.SetBounds(20, 177, 495, 38); status.Text = "昵称保存后可使用本地陪玩；官方战绩需要验证密钥。"; Controls.Add(status);
            save.Text = "只保存昵称"; save.SetBounds(220, 231, 135, 32); save.Click += delegate { string name = nickname.Text.Trim(); if (name.Length == 0) { status.Text = "请填写游戏昵称。"; return; } if (name != Profile.Nickname) Profile.AccountId = ""; Profile.Nickname = name; if (!Profile.Save()) { status.Text = "配置暂时被占用，尚未保存，请稍后重试。"; return; } DialogResult = DialogResult.OK; Close(); }; Controls.Add(save);
            verify.Text = "验证并绑定"; verify.SetBounds(370, 231, 145, 32); verify.Click += async delegate { if (checking) return; string name = nickname.Text.Trim(); string secret = key.Text.Trim(); if (secret.Length == 0) secret = PubgApi.LoadKey(); if (name.Length == 0 || secret.Length == 0) { status.Text = "请填写昵称和 API 密钥；无需 Steam 密码。"; return; } checking = true; verify.Enabled = save.Enabled = false; nickname.Enabled = key.Enabled = false; status.Text = "正在向 PUBG 官方验证…";
                try { int count = 0; string account = await Task.Run(() => PubgApi.Verify(name, secret, out count)); PubgApi.SaveKey(secret); Profile.Nickname = name; Profile.AccountId = account; if (!Profile.Save()) throw new InvalidOperationException("验证成功，但配置暂时无法保存，请稍后重试。"); key.Clear(); status.Text = "已验证：" + name + "；近期可查询对局 " + count + " 场。"; }
                catch (Exception ex) { status.Text = ex is InvalidOperationException || ex is InvalidDataException ? ex.Message : "验证或本机保存失败，请重试。"; }
                finally { checking = false; verify.Enabled = save.Enabled = true; nickname.Enabled = key.Enabled = true; }
            }; Controls.Add(verify);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (checking) e.Cancel = true; };
        }
    }
}
