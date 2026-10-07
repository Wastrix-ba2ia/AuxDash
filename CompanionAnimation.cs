using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SideScreenMonitor {
    public sealed class CompanionAnimation : IDisposable {
        readonly Image atlas;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Bitmap[] poses = new Bitmap[50];
        readonly byte[][] pixels = new byte[50][];
        Bitmap blended;
        byte[] blendedPixels, transitionPixels;
        int poseWidth, poseHeight, lastMode = -1;
        double transitionStart;
        double greetingUntil, forcedBusyUntil, groomingUntil, greetingStart, groomingStart, busyStart;
        bool previousBusy;
        int performance;
        double performanceStart, performanceUntil, nextPerformance = 18;
        int automaticSequence;
        public bool AutoPerform = true;
        public bool PerformanceAvailable { get; private set; }
        public void Turn() { StartPerformance(1); }
        public void Dance() { StartPerformance(2); }
        void StartPerformance(int action) {
            if (!PerformanceAvailable) return;
            performance=action; performanceStart=clock.Elapsed.TotalSeconds;
            performanceUntil=performanceStart+(action==1 ? 4.8 : 8.4);
            greetingUntil=groomingUntil=0;
            nextPerformance=performanceUntil+20;
        }
        public static void SamplePerformance(double elapsed, bool dance, out int a, out int b, out float blend) {
            // The generated first row forms the usable turn. Dance reverses before returning home.
            int[] sequence = dance ? new [] {34,35,36,37,38,39,40,41,40,39,38,37,36,35,34} : new [] {18,19,20,21,22,23,24,25,18};
            double duration=dance ? .6 : .6;
            double position=Math.Max(0,elapsed)/duration;
            int index=Math.Min(sequence.Length-2,(int)position);
            a=sequence[index]; b=sequence[index+1];
            double t=Math.Max(0,Math.Min(1,position-index));
            // Hold most of each pose; use a short transition to limit double contours.
            t=Math.Max(0,(t-.7)/.3); blend=(float)(t*t*(3-2*t));
        }
        bool wasRunning;
        public bool Enabled = true;
        public bool Effects = true;
        public bool Available { get { return atlas != null; } }
        public CompanionAnimation(bool preferVideo=false) {
            if(preferVideo && File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","idle-bedroom-v3","reference.png")) && Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","idle-bedroom-v3","frames")))return;
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "robot-actions.png");
            if (File.Exists(path)) {
                using (var image = Image.FromFile(path)) atlas = new Bitmap(image);
                poseWidth = atlas.Width / 6; poseHeight = atlas.Height / 3;
                for (int i = 0; i < 18; i++) {
                    poses[i] = new Bitmap(poseWidth, poseHeight, PixelFormat.Format32bppPArgb);
                    using (var g = Graphics.FromImage(poses[i])) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(atlas,new Rectangle(0,0,poseWidth,poseHeight),new RectangleF(i%6*atlas.Width/6f,i/6*atlas.Height/3f,atlas.Width/6f,atlas.Height/3f),GraphicsUnit.Pixel); }
                    pixels[i] = ReadPixels(poses[i]);
                }
                string performancePath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","robot-performance.png");
                if(File.Exists(performancePath)) using(var extra=Image.FromFile(performancePath)) {
                    for(int i=0;i<32;i++) {
                        poses[18+i]=new Bitmap(poseWidth,poseHeight,PixelFormat.Format32bppPArgb);
                        using(var g=Graphics.FromImage(poses[18+i])) {
                            g.CompositingMode=CompositingMode.SourceCopy;
                            // Fit new cells without changing their aspect ratio.
                            float w=extra.Width/8f,h=extra.Height/4f;
                            float scale=Math.Min(poseWidth/w,poseHeight/h);
                            g.DrawImage(extra,new RectangleF((poseWidth-w*scale)/2,poseHeight-h*scale,w*scale,h*scale),new RectangleF(i%8*w,i/8*h,w,h),GraphicsUnit.Pixel);
                        }
                        pixels[18+i]=ReadPixels(poses[18+i]);
                    }
                    PerformanceAvailable=true;
                }
                blended = new Bitmap(poseWidth,poseHeight,PixelFormat.Format32bppPArgb);
                blendedPixels = new byte[poseWidth*poseHeight*4];
            }
        }
        public void Greet() { performanceUntil=0; greetingStart = clock.Elapsed.TotalSeconds; greetingUntil = greetingStart + 3.0; }
        public void BusyPreview() { forcedBusyUntil = clock.Elapsed.TotalSeconds + 8; }
        public void Groom() { performanceUntil=0; groomingStart = clock.Elapsed.TotalSeconds; groomingUntil = groomingStart + 3; }
        public void Observe(bool running) { if (running && !wasRunning) Greet(); wasRunning = running; }
        public static int SelectFrame(double seconds, bool busy, bool greeting, bool grooming, out int row) {
            if (greeting) { row = 2; return (int)(seconds / .50) % 6; }
            if (busy) { row = 1; int n = (int)(seconds / .24) % 10; return n <= 5 ? n : 10-n; }
            row = 0;
            if (grooming) return (int)(seconds / .50) % 6;
            double cycle = seconds % 9;
            return cycle < 4.8 ? 0 : Math.Min(5, (int)((cycle - 4.8) / (.7)));
        }
        public static void Sample(double seconds, bool busy, bool greeting, bool grooming, out int a, out int b, out float blend) {
            int row; int frame = SelectFrame(seconds,busy,greeting,grooming,out row); a=row*6+frame;
            double duration, local;
            if (greeting) { duration=.5; local=seconds%.5; }
            else if (busy) { duration=.24; local=seconds%.24; }
            else if (grooming) { duration=.5; local=seconds%.5; }
            else { double cycle=seconds%9; if(cycle<4.8) { b=a; blend=0; return; } duration=.7; local=(cycle-4.8)%.7; }
            int nextRow; int next=SelectFrame(seconds+duration-local+.00001,busy,greeting,grooming,out nextRow);
            b=nextRow*6+next;
            if(greeting && frame==5) b=0;
            // Short eased transitions soften sparse poses without lingering double silhouettes.
            double fade=Math.Min(duration,.18); double t=Math.Max(0,Math.Min(1,(local-(duration-fade))/fade));
            blend=(float)(t*t*(3-2*t));
        }
        static byte[] ReadPixels(Bitmap bitmap) { var data=bitmap.LockBits(new Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb); try { byte[] bytes=new byte[bitmap.Width*bitmap.Height*4]; Marshal.Copy(data.Scan0,bytes,0,bytes.Length); return bytes; } finally { bitmap.UnlockBits(data); } }
        void BlendPose(int a,int b,float amount,int mode,double seconds) {
            if(lastMode != mode && lastMode >= 0) { transitionPixels=(byte[])blendedPixels.Clone(); transitionStart=seconds; }
            lastMode=mode;
            double cross=Math.Max(0,Math.Min(1,(seconds-transitionStart)/.28)); cross=cross*cross*(3-2*cross);
            int mix=(int)(amount*256), stateMix=(int)(cross*256);
            for(int i=0;i<blendedPixels.Length;i++) { int value=(pixels[a][i]*(256-mix)+pixels[b][i]*mix+128)>>8; if(transitionPixels!=null && stateMix<256) value=(transitionPixels[i]*(256-stateMix)+value*stateMix+128)>>8; blendedPixels[i]=(byte)value; }
            if(stateMix==256) transitionPixels=null;
            var data=blended.LockBits(new Rectangle(0,0,poseWidth,poseHeight),ImageLockMode.WriteOnly,PixelFormat.Format32bppPArgb); try { Marshal.Copy(blendedPixels,0,data.Scan0,blendedPixels.Length); } finally { blended.UnlockBits(data); }
        }
        public void Draw(Graphics g, RectangleF bounds, bool busy, bool running, bool hot) {
            if (atlas == null) return;
            double t = Enabled ? clock.Elapsed.TotalSeconds : 0;
            bool isBusy = Enabled && (busy || t < forcedBusyUntil) && t >= groomingUntil, greet = Enabled && t < greetingUntil, groom = Enabled && t < groomingUntil;
            if (isBusy && !previousBusy) busyStart = t;
            previousBusy = isBusy;
            if(Enabled && AutoPerform && !busy && !running && !hot && !isBusy && !greet && !groom && t>=nextPerformance) {
                StartPerformance((automaticSequence++%2)+1);
            }
            int action=Enabled && t<performanceUntil ? performance : 0;
            if(action!=0) { isBusy=false; greet=false; groom=false; }
            double phase = greet ? t - greetingStart : isBusy ? t - busyStart : groom ? t - groomingStart : t;
            DrawAt(g, bounds, t, isBusy, greet, groom, hot, phase, action, t-performanceStart);
        }
        public void DrawAt(Graphics g, RectangleF bounds, double seconds, bool busy, bool greeting, bool grooming, bool hot, double phase = -1, int action = 0, double actionPhase = 0) {
            var saved = g.Save(); g.SetClip(bounds);
            using (var b = new LinearGradientBrush(bounds, Color.FromArgb(17, 22, 44), Color.FromArgb(68, 30, 75), 30f)) g.FillRectangle(b, bounds);
            Color accent = hot ? Color.FromArgb(255, 169, 99) : Color.FromArgb(80, 225, 250);
            using (var p = new Pen(Color.FromArgb(33, accent), .7f)) {
                for (float x = bounds.Left; x < bounds.Right; x += 30) g.DrawLine(p, x, bounds.Top, x, bounds.Bottom);
                for (float y = bounds.Top; y < bounds.Bottom; y += 30) g.DrawLine(p, bounds.Left, y, bounds.Right, y);
            }
            float pulse = (float)(Math.Sin(seconds * (busy ? 5 : 1.4)) * .5 + .5);
            using (var p = new Pen(Color.FromArgb((int)(35 + pulse * 40), accent), 1.4f)) g.DrawEllipse(p, bounds.Left + 70, bounds.Top + 25, 280, 280);
            if (Effects) DrawEffects(g, bounds, seconds, accent, busy, greeting);
            Text(g, "N O V A", bounds.Left + 16, bounds.Top + 17, 15, Color.FromArgb(205, 224, 244));
            Text(g, action==1 ? "TURN AROUND" : action==2 ? "DANCE" : greeting ? "HELLO" : busy ? "PROCESSING" : "COMPANION", bounds.Left + 16, bounds.Top + 40, 8, accent);
            int poseA,poseB; float mix; Sample(phase < 0 ? seconds : phase,busy,greeting,grooming,out poseA,out poseB,out mix);
            if(action!=0 && PerformanceAvailable) SamplePerformance(actionPhase,action==2,out poseA,out poseB,out mix);
            BlendPose(poseA,poseB,mix,action!=0?4+action:greeting?2:busy?1:grooming?3:0,seconds);
            float cellWidth = atlas.Width / 6f, cellHeight = atlas.Height / 3f;
            // Original source geometry is preserved. All motion is whole-frame positioning,
            // avoiding facial warps or synthetic eye/mouth replacements.
            float height = bounds.Height * .96f;
            float width = height * cellWidth / cellHeight;
            float sway = (float)Math.Sin(seconds * .9) * 2.2f;
            float breathe = (float)Math.Sin(seconds * 1.7) * 1.1f;
            var target = new RectangleF(bounds.Left + (bounds.Width - width) / 2 + 15 + sway, bounds.Top + 4 + breathe, width, height);
            g.DrawImage(blended,target);
            if (busy) {
                using (var fill = new SolidBrush(Color.FromArgb(35, accent))) g.FillPolygon(fill, new [] { new PointF(bounds.Left+165,bounds.Top+214), new PointF(bounds.Left+305,bounds.Top+214), new PointF(bounds.Left+327,bounds.Top+239), new PointF(bounds.Left+143,bounds.Top+239) });
                using (var p = new Pen(Color.FromArgb(120, accent), 1)) {
                    g.DrawPolygon(p, new [] { new PointF(bounds.Left+165, bounds.Top+214), new PointF(bounds.Left+305,bounds.Top+214), new PointF(bounds.Left+327,bounds.Top+239), new PointF(bounds.Left+143,bounds.Top+239) });
                    float scan = bounds.Left + 165 + (float)((seconds * 35) % 130); g.DrawLine(p, scan, bounds.Top+215, scan, bounds.Top+238);
                    for (int i = 0; i < 9; i++) { float xx = bounds.Left + 155 + i * 17; g.DrawLine(p, xx, bounds.Top+223, xx+8, bounds.Top+223); g.DrawLine(p, xx-3, bounds.Top+231, xx+5, bounds.Top+231); }
                }
            }
            g.Restore(saved);
        }
        void DrawEffects(Graphics g, RectangleF b, double t, Color cyan, bool busy, bool greeting) {
            Color purple = Color.FromArgb(194, 91, 255);
            float cx = b.Left + 231, cy = b.Top + 152;
            float scanY = b.Top + (float)((t * (busy ? 45 : 17)) % b.Height);
            using (var brush = new LinearGradientBrush(new RectangleF(b.Left, scanY, b.Width, 16), Color.Transparent, Color.FromArgb(28, cyan), 90f)) g.FillRectangle(brush, b.Left, scanY, b.Width, 16);
            // Layered translucent arcs simulate bloom without full-window blur buffers.
            for (int ring = 0; ring < 3; ring++) {
                float radius = 104 + ring * 22;
                var circle = new RectangleF(cx-radius, cy-radius, radius*2, radius*2);
                float angle = (float)(t * (busy ? 36 : 13) * (ring % 2 == 0 ? 1 : -1)) + ring * 100;
                Color c = ring == 1 ? purple : cyan;
                for (int layer = 3; layer >= 1; layer--) using (var pen = new Pen(Color.FromArgb(layer == 1 ? 165 : 13, c), layer == 1 ? 1.5f : layer * 3f)) { g.DrawArc(pen,circle,angle,83); g.DrawArc(pen,circle,angle+180,47); }
            }
            using (var pen = new Pen(Color.FromArgb(65, cyan), .7f)) {
                for (int i = 0; i < 48; i++) { double a = i*Math.PI/24 + t*.015; float r = 143; float r2 = r + (i%4==0 ? 6 : 2); g.DrawLine(pen,cx+(float)Math.Cos(a)*r,cy+(float)Math.Sin(a)*r,cx+(float)Math.Cos(a)*r2,cy+(float)Math.Sin(a)*r2); }
                for (int i = -4; i <= 4; i++) g.DrawLine(pen,b.Left+210+i*20,b.Top+248,b.Left+210+i*80,b.Bottom);
                for (int i = 0; i < 5; i++) { float yy = b.Top+250+(float)((i*15+t*8)%65); g.DrawLine(pen,b.Left,yy,b.Right,yy); }
            }
            for (int i = 0; i < 25; i++) {
                float x = b.Left + (i*83 % 407) + (float)Math.Sin(t*.45+i)*7;
                float y = b.Top + (float)((i*47+320-t*(busy ? 18 : 6)%320)%320);
                int alpha = (int)(45+45*(Math.Sin(t*1.4+i)*.5+.5));
                using (var brush = new SolidBrush(Color.FromArgb(alpha, i%3==0 ? purple : cyan))) g.FillEllipse(brush,x,y,i%5==0?3:1.6f,i%5==0?3:1.6f);
            }
            // Side HUD widgets stay outside the face and do not pretend to be telemetry.
            using (var pen = new Pen(Color.FromArgb(110, cyan), 1)) {
                g.DrawLines(pen,new [] {new PointF(b.Left+18,b.Top+74),new PointF(b.Left+18,b.Top+63),new PointF(b.Left+70,b.Top+63)});
                g.DrawLines(pen,new [] {new PointF(b.Right-18,b.Top+77),new PointF(b.Right-18,b.Top+64),new PointF(b.Right-52,b.Top+64)});
                for (int i=0;i<8;i++) { float length=6+(float)(Math.Sin(t*2+i)*.5+.5)*25; g.DrawLine(pen,b.Left+20,b.Top+185+i*5,b.Left+20+length,b.Top+185+i*5); }
            }
            using (var pen = new Pen(Color.FromArgb(105,purple),1.3f)) g.DrawLine(pen,b.Left+18,b.Top+52,b.Left+96,b.Top+52);
            if (greeting) {
                float radius = 30+(float)(t%1.8)/1.8f*120;
                using(var pen=new Pen(Color.FromArgb((int)(100*(1-t%1.8/1.8)),cyan),2)) g.DrawEllipse(pen,cx-radius,cy-radius,radius*2,radius*2);
            }
        }
        static void Text(Graphics g, string s, float x, float y, float size, Color color) { using (var font = new Font("Bahnschrift", size, FontStyle.Regular, GraphicsUnit.Pixel)) using (var brush = new SolidBrush(color)) g.DrawString(s, font, brush, x, y); }
        public void Dispose() { if (atlas != null) atlas.Dispose(); foreach(var pose in poses) if(pose!=null) pose.Dispose(); if(blended!=null) blended.Dispose(); }
    }
}
