using System;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;
using SideScreenMonitor;
class ActionLibraryTests {
 static int checks;
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
 static FieldInfo Field(string name){return typeof(PetVideo).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);}
 static double Now(PetVideo v){return ((Stopwatch)Field("clock").GetValue(v)).Elapsed.TotalSeconds;}
 static void Draw(PetVideo v,Bitmap b){using(var g=Graphics.FromImage(b))v.Draw(g,new RectangleF(0,0,b.Width,b.Height),new PetRules(),false);}
 static int Difference(Bitmap a,Bitmap b){int count=0;for(int y=0;y<a.Height;y+=8)for(int x=0;x<a.Width;x+=8)if(a.GetPixel(x,y)!=b.GetPixel(x,y))count++;return count;}
 static void Main(){try{Run();}catch(Exception ex){Console.WriteLine(ex.Message);Environment.Exit(1);}} static void Run(){
  foreach(int count in new[]{31,72,121,145}) {
   double end=(count-1)/24.0*2;
   Check(PetVideo.ActionFrameAt(0,count)==0,"start index");
   Check(PetVideo.ActionFrameAt(end,count)==0,"end index");
   Check(PetVideo.ActionFrameAt(end/2,count)==count-1,"midpoint");
   for(int k=0;k<300;k++){int i=PetVideo.ActionFrameAt(end*k/299,count);Check(i>=0&&i<count,"frame bounds");}
  }
  using(var v=new PetVideo())using(var start=new Bitmap(729,480))using(var middle=new Bitmap(729,480))using(var finish=new Bitmap(729,480)) {
   Check(v.IdleVariantCount==4,"four approved idle variants");
   int installed=0;
   foreach(string state in PetVideo.ActionNames) {
    if(state=="转圈"){Check(!v.HasAction(state),"failed twirl not installed");continue;}
    if(PetVideo.Clip(state).StartsWith("music-") && !v.HasAction(state))continue;
    Check(v.HasAction(state),"missing "+state);installed++;
    v.PreviewAction(state);Draw(v,start);Check(v.ActiveClip==PetVideo.Clip(state),"starts "+state);
    var normalized=(Image)Field("frame").GetValue(v);Check(normalized.Width==736&&normalized.Height==486,"standard frame resolution "+state);
    var files=(string[])Field("files").GetValue(v);bool forward=PetVideo.Clip(state).StartsWith("daily-") || PetVideo.Clip(state).StartsWith("music-");double duration=forward?files.Length/24.0:(files.Length-1)/24.0*2;
    Field("started").SetValue(v,Now(v)-duration/2);Draw(v,middle);
    Check(Difference(start,middle)>20,"visible action "+state);
    Field("started").SetValue(v,Now(v)-duration-.1);Draw(v,finish);
    Check(v.ActiveClip==PetVideo.Clip(state),"holds action through exit crossfade "+state);
    Field("actionExitUntil").SetValue(v,Now(v)-.01);Draw(v,finish);
    Check(v.ActiveClip==null,"ends after exit crossfade "+state);
    Field("idleTransitionUntil").SetValue(v,0.0);
    if(forward) { Check(Math.Abs(duration-12)<.01 || (PetVideo.Clip(state).StartsWith("music-") && Math.Abs(duration-6)<.01),"expected clip duration "+state); using(var first=new Bitmap(files[0]))using(var last=new Bitmap(files[files.Length-1]))Check(Difference(first,last)==0,"matching transition endpoints "+state); } else Check(Difference(start,finish)<20,"returns to neutral "+state);
   }
   Check(installed>=40,"baseline 40 actions plus available music clips");
   for(int i=0;i<12;i++) {
    string old=v.IdleName;Field("idleEpoch").SetValue(v,Now(v)-30);Draw(v,finish);
    Check(v.IdleName!=old,"idle never immediately repeats");
   }
   Field("lastWorkClip").SetValue(v,null);v.CodexState="running";Field("idleEpoch").SetValue(v,Now(v));Draw(v,start);
   Check(v.ActiveClip=="work-typing","Codex starts laptop work");
   var workFiles=(string[])Field("files").GetValue(v);Field("started").SetValue(v,Now(v)-(workFiles.Length-1)/12.0-.1);Draw(v,finish);Field("actionExitUntil").SetValue(v,Now(v)-.01);Field("idleTransitionUntil").SetValue(v,Now(v)-.01);Draw(v,finish);
   Check(v.ActiveClip=="work","Codex alternates to original work");
   workFiles=(string[])Field("files").GetValue(v);Field("started").SetValue(v,Now(v)-(workFiles.Length-1)/12.0-.1);Draw(v,finish);Field("actionExitUntil").SetValue(v,Now(v)-.01);Field("idleTransitionUntil").SetValue(v,Now(v)-.01);Draw(v,finish);
   Check(v.ActiveClip=="work-typing","Codex alternates back to laptop");
   v.CodexState="idle";workFiles=(string[])Field("files").GetValue(v);Field("started").SetValue(v,Now(v)-(workFiles.Length-1)/12.0-.1);Draw(v,finish);Field("actionExitUntil").SetValue(v,Now(v)-.01);Field("idleTransitionUntil").SetValue(v,Now(v)-.01);Draw(v,finish);
   Check(v.ActiveClip==null && v.PendingClip==null,"offline stops work rotation");
   v.CodexState="waiting";v.CodexState="idle";Check(v.PendingClip==null,"offline cancels queued Codex gesture");
  }
  Console.WriteLine(checks+" checks passed: 40 actions, 4 idles, frame bounds, neutral return, random selection, offline queue");
 }
}
