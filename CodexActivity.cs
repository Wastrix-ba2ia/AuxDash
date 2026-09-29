using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Net;
using System.Net.NetworkInformation;
namespace SideScreenMonitor {
 // Read-only adapter for local Codex rollout events. Unknown schemas never imply success.
 public sealed class CodexActivity {
  public sealed class Signal {
   public string State="idle", Source="", Turn=""; public DateTime Changed, Seen;
   public string Caption { get {return State=="running"?"我正在认真工作，陪我一会儿吧。":State=="waiting"?"我有个问题，等你来回答呀。":State=="ready"?"我忙完啦，快来看看我的成果。":State=="error"?"我遇到一点问题，陪我看看提示吧。":"";} }
  }
  sealed class Cursor {public long Position;public string Pending="";public Signal Signal=new Signal();}
  readonly Dictionary<string,Cursor> cursors=new Dictionary<string,Cursor>();
  readonly string root; readonly Func<bool> networkProbe; DateTime scanned, nextNetworkCheck, acceptAfter; bool online;
  string[] paths=new string[0];
  public bool Online {get {return online;}}
  public string Diagnostic {get;private set;}
  bool CheckNetwork(DateTime now) {
   if(networkProbe==null && !NetworkInterface.GetIsNetworkAvailable()){online=false;nextNetworkCheck=now;acceptAfter=now;return false;}
   if(now<nextNetworkCheck)return online;
   nextNetworkCheck=now.AddSeconds(10);
   bool connected=false;
   try {
    if(networkProbe!=null)connected=networkProbe();else {
    var request=(HttpWebRequest)WebRequest.Create("http://www.msftconnecttest.com/connecttest.txt");
    request.Timeout=3000;request.ReadWriteTimeout=3000;request.AllowAutoRedirect=false;
    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))
     connected=response.StatusCode==HttpStatusCode.OK && reader.ReadToEnd().Trim()=="Microsoft Connect Test";
    }
   }catch(WebException){}catch(IOException){}
   if(!connected || (connected&&!online))acceptAfter=now;
   online=connected;return online;
  }
  public CodexActivity() {root=Path.Combine(BindingConfig.Load().ResolvedHome,"sessions");}
  public CodexActivity(string sessionRoot,Func<bool> probe){root=sessionRoot;networkProbe=probe;}
  static string Str(Dictionary<string,object> d,string key) {object v;return d!=null&&d.TryGetValue(key,out v)?Convert.ToString(v):"";}
  static Dictionary<string,object> Obj(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)?v as Dictionary<string,object>:null;}
  public static void Consume(Signal s,string line,DateTime now) {
   try {
    var r=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,object>>(line);
    DateTime at;if(!DateTime.TryParse(Str(r,"timestamp"),null,System.Globalization.DateTimeStyles.RoundtripKind,out at))return;at=at.ToUniversalTime();
    if(at>now.AddMinutes(1)||at<now.AddHours(-2))return;
    var p=Obj(r,"payload");if(p==null)return;
    string kind=Str(r,"type"),type=Str(p,"type"),next="",turn=Str(p,"turn_id");
    if(kind=="event_msg") {
     if(type=="task_started")next="running";
     else if(type=="task_complete")next="ready";
     else if(type=="turn_aborted")next="idle";
     else if(type=="task_failed" || type=="turn_failed")next="error";
     else if(type=="error") {
      string message=Str(p,"message").ToLowerInvariant();
      next=message.Contains("network")||message.Contains("reconnect")||message.Contains("stream disconnected")||message.Contains("connection")?"idle":"error";
     }
     else if(type=="user_message" && s.State=="waiting")next="running";
    }
    if(kind=="response_item") {
     if(type=="function_call"||type=="custom_tool_call") {
      string name=Str(p,"name");
      next=name.EndsWith("request_user_input")||name.EndsWith("request_user_input_async")?"waiting":"running";
     } else if(type=="reasoning" || (type=="message" && Str(p,"role")=="assistant" && Str(p,"phase")!="final_answer")) {
      if(s.State!="waiting")next="running";
     } else if(type=="function_call_output"||type=="custom_tool_call_output") {
      // Asynchronous questions return before the user responds, so output alone cannot clear waiting.
      if(s.State!="waiting")next="running";
     }
    }
    if(next.Length==0)return;
    if(turn.Length>0 && type!="task_started" && s.Turn.Length>0 && turn!=s.Turn)return;
    if(at<s.Seen)return;
    if(type=="task_started")s.Turn=turn;
    if(s.State!=next){s.State=next;s.Changed=at;}
    s.Seen=at;
   } catch(ArgumentException){} catch(InvalidOperationException){} catch(NullReferenceException){}
  }
  public static Signal Select(IEnumerable<Signal> signals,DateTime now) {
   return signals.Where(s=>s.Seen<=now.AddMinutes(1) && (s.State=="running"?(now-s.Seen).TotalSeconds<90:s.State=="waiting"?(now-s.Seen).TotalMinutes<20:s.State=="ready"?(now-s.Changed).TotalSeconds<15:s.State=="error"&&(now-s.Changed).TotalSeconds<30))
    .OrderByDescending(s=>s.State=="waiting"?4:s.State=="error"?3:s.State=="ready"?2:1).ThenByDescending(s=>s.Seen).FirstOrDefault()??new Signal();
  }
  public Signal Poll() {
   DateTime now=DateTime.UtcNow;
   if(!CheckNetwork(now)){Diagnostic="local-mode: network unavailable";return new Signal();}
   try {
    if((now-scanned).TotalSeconds>15) {
     scanned=now;
     paths=Directory.Exists(root)?new DirectoryInfo(root).EnumerateFiles("*.jsonl",SearchOption.AllDirectories).Where(f=>f.LastWriteTimeUtc>now.AddHours(-2)).OrderByDescending(f=>f.LastWriteTimeUtc).Take(12).Select(f=>f.FullName).ToArray():new string[0];
     foreach(string old in cursors.Keys.Except(paths).ToArray())cursors.Remove(old);
    }
    foreach(string path in paths)try {
     Cursor c;if(!cursors.TryGetValue(path,out c)){c=new Cursor();c.Signal.Source=Path.GetFileName(path);cursors[path]=c;}
     using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
      bool tail=c.Position==0||c.Position>stream.Length;
      if(tail){c.Position=Math.Max(0,stream.Length-2*1024*1024);c.Pending="";c.Signal=new Signal{Source=Path.GetFileName(path)};}
      long start=c.Position;stream.Position=start;
      int count=(int)Math.Min(2*1024*1024,stream.Length-start);if(count==0)continue;
      byte[] data=new byte[count];int read=stream.Read(data,0,count);c.Position+=read;
      string text=c.Pending+Encoding.UTF8.GetString(data,0,read);int begin=0;
      if(tail&&start>0){int skip=text.IndexOf('\n');if(skip<0)continue;begin=skip+1;}
      int end;while((end=text.IndexOf('\n',begin))>=0){Consume(c.Signal,text.Substring(begin,end-begin),now);begin=end+1;}
      c.Pending=text.Substring(begin);if(c.Pending.Length>4*1024*1024)c.Pending="";
     }
    }catch(IOException){}catch(UnauthorizedAccessException){}
   }catch(IOException){}catch(UnauthorizedAccessException){}
   var selected=Select(cursors.Values.Select(c=>c.Signal).Where(s=>s.Seen>=acceptAfter),now);
   Diagnostic="network=online; state="+selected.State+"; source="+selected.Source;
   return selected;
  }
 }
}
