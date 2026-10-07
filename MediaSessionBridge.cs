using System;
using System.Diagnostics;
using System.Threading;
using Windows.Media;
using Windows.Media.Control;
class MediaSessionBridge {
 static void Main(string[] args){
  try{
   var op=GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
   var wait=Stopwatch.StartNew();while(op.Status==Windows.Foundation.AsyncStatus.Started && wait.ElapsedMilliseconds<5000)Thread.Sleep(50);if(op.Status!=Windows.Foundation.AsyncStatus.Completed)return;var manager=op.GetResults();
   int parent=args.Length>0?int.Parse(args[0]):0;
   do{
    if(parent>0){try{if(Process.GetProcessById(parent).HasExited)return;}catch{return;}}
    bool music=false;
    try{foreach(var session in manager.GetSessions()){var info=session.GetPlaybackInfo();if(info.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && info.PlaybackType==MediaPlaybackType.Music){music=true;break;}}Console.WriteLine(music?"music":"idle");}catch{Console.WriteLine("unavailable");}
    if(parent==0)return;Thread.Sleep(2000);
   }while(true);
  }catch{Console.WriteLine("unavailable");}
 }
}
