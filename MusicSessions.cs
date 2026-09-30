using System;
using System.IO;
using System.Diagnostics;
namespace SideScreenMonitor {
 public sealed class MusicGate {
  DateTime since=DateTime.MinValue,last=DateTime.MinValue;public bool Active{get;private set;}
  public bool Update(bool playing,bool blocked,DateTime now){
   if(blocked){since=last=DateTime.MinValue;Active=false;return false;}
   if(playing){if(since==DateTime.MinValue)since=now;last=now;if((now-since).TotalSeconds>=5)Active=true;}
   else {since=DateTime.MinValue;if((now-last).TotalSeconds>=15)Active=false;}
   return Active;
  }
 }
 public sealed class MusicSessions : IDisposable {
  Process worker;readonly object sync=new object();bool playing;DateTime received,retry;bool disposed;
  public bool Poll(){
   if(disposed)return false;
   if(worker==null || worker.HasExited){
    if(DateTime.UtcNow<retry)return false;retry=DateTime.UtcNow.AddSeconds(30);
    try{
     if(worker!=null){worker.Dispose();worker=null;}
     var p=new Process{StartInfo=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MediaSessionBridgeHost.exe"),Process.GetCurrentProcess().Id.ToString()){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true}};
     p.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){lock(sync){playing=e.Data=="music";received=DateTime.UtcNow;}};
     p.Start();worker=p;p.BeginOutputReadLine();p.BeginErrorReadLine();
    }catch{return false;}
   }
   lock(sync)return playing && (DateTime.UtcNow-received).TotalSeconds<6;
  }
  public void Dispose(){disposed=true;if(worker!=null){try{if(!worker.HasExited)worker.Kill();}catch{}worker.Dispose();worker=null;}}
 }
}

