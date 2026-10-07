using System;
using System.Drawing;
using System.Threading;
using System.Reflection;
using SideScreenMonitor;
class IdleTests {
 static void Main(){
  for(int i=0;i<220;i++){int f=PetVideo.IdleFrameAt(i*.1,121);if(f<42||f>110)throw new Exception("Frame out of range");}
  if(PetVideo.IdleFrameAt(0,121)!=PetVideo.IdleFrameAt(11,121))throw new Exception("Loop seam");
  using(var v=new PetVideo())using(var a=new Bitmap(421,314))using(var b=new Bitmap(421,314)){
   using(var g=Graphics.FromImage(a))v.Draw(g,new RectangleF(0,0,421,314),new PetRules(),false);
   var neutral=(Image)typeof(PetVideo).GetField("neutral",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(v);
   if(neutral.Width!=736||neutral.Height!=486)throw new Exception("Neutral frame must use standard resolution");
   Thread.Sleep(1600);
   using(var g=Graphics.FromImage(b))v.Draw(g,new RectangleF(0,0,421,314),new PetRules(),false);
   var idle=(Image)typeof(PetVideo).GetField("idleFrame",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(v);
   if(idle.Width!=736||idle.Height!=486)throw new Exception("Idle frame must use standard resolution");
   int changed=0;for(int y=0;y<314;y+=7)for(int x=0;x<421;x+=7)if(a.GetPixel(x,y)!=b.GetPixel(x,y))changed++;
   if(changed<50)throw new Exception("Idle isn't moving");
  }
  Console.WriteLine("PASS: idle clip advances without events; frame bounds and loop seam verified");
 }
}
