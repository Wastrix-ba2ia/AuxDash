using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Web.Script.Serialization;
namespace SideScreenMonitor {
 public sealed class CodexQuota {
  public double Remaining; public DateTime Reset, Updated;
  static Dictionary<string,object> Obj(object value){return value as Dictionary<string,object>;}
  public static CodexQuota Parse(Dictionary<string,object> result) {
   object value; Dictionary<string,object> bucket=null;
   if(result.TryGetValue("rateLimitsByLimitId",out value)) {var map=Obj(value);if(map!=null&&map.TryGetValue("codex",out value))bucket=Obj(value);}
   if(bucket==null&&result.TryGetValue("rateLimits",out value))bucket=Obj(value);
   if(bucket==null)throw new InvalidDataException("No Codex quota");
   foreach(string key in new[]{"primary","secondary"}) {
    if(!bucket.TryGetValue(key,out value))continue;var window=Obj(value);if(window==null)continue;
    if(!window.TryGetValue("windowDurationMins",out value)||value==null||Convert.ToInt32(value)!=10080)continue;
    if(!window.TryGetValue("usedPercent",out value)||value==null)continue;
    var quota=new CodexQuota{Remaining=Math.Max(0,Math.Min(100,100-Convert.ToDouble(value))),Updated=DateTime.UtcNow};
    if(window.TryGetValue("resetsAt",out value)&&value!=null)quota.Reset=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(Convert.ToDouble(value));
    return quota;
   }
   throw new InvalidDataException("Weekly quota unavailable");
  }
  public static CodexQuota Fetch() {return Fetch(BindingConfig.Load());}
  public static CodexQuota Fetch(BindingConfig binding) {
   if(!binding.CodexEnabled)throw new InvalidOperationException("Codex binding disabled");
   string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OpenAI","Codex","bin");
   string exe=string.IsNullOrWhiteSpace(binding.Executable)?null:binding.Executable;DateTime latest=DateTime.MinValue;
   if(exe==null && Directory.Exists(root))foreach(string dir in Directory.GetDirectories(root)){string candidate=Path.Combine(dir,"codex.exe");if(File.Exists(candidate)&&File.GetLastWriteTimeUtc(candidate)>latest){exe=candidate;latest=File.GetLastWriteTimeUtc(candidate);}}
   if(exe==null)throw new FileNotFoundException("Codex executable unavailable");
   using(var process=new Process()) using(var ready=new ManualResetEvent(false)) {
    var json=new JavaScriptSerializer();Dictionary<string,object> response=null;
    process.StartInfo=new ProcessStartInfo(exe,"app-server --stdio"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
    process.StartInfo.EnvironmentVariables["CODEX_HOME"]=binding.ResolvedHome;
    process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null)return;try{var r=json.Deserialize<Dictionary<string,object>>(e.Data);object id;if(r.TryGetValue("id",out id)&&(Convert.ToString(id)=="1"||Convert.ToString(id)=="2")){response=r;ready.Set();}}catch{}};
    process.ErrorDataReceived+=delegate{};
    try {
     process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
     process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"side-screen-monitor\",\"version\":\"1.0\"}}}");process.StandardInput.Flush();
     if(!ready.WaitOne(15000)||response.ContainsKey("error"))throw new IOException("Initialize failed");
     ready.Reset();response=null;
     process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
     process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\",\"params\":{}}");process.StandardInput.Flush();
     if(!ready.WaitOne(20000)||response.ContainsKey("error"))throw new IOException("Quota read failed");
     return Parse(Obj(response["result"]));
    }finally{try{if(!process.HasExited)process.Kill();process.WaitForExit(3000);}catch{}}
   }
  }
 }
}
