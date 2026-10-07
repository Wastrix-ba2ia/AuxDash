using System;
using System.Drawing;
using System.IO;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Collections.Generic;
namespace SideScreenMonitor {
    public sealed class PetVideo : IDisposable {
        const int FrameWidth=736, FrameHeight=486;
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","video-interactions","frames");
        Image frame, neutral, idleFrame; string[] idleFiles, idleBaseFiles; string idleFramePath; int idleIndex=-1; string[] files; int index=-1; double started, lastPlayed=-100, actionExitUntil; string previous="";
        public bool Enabled=true, ShowBubbles;
        string codexState="idle";
        public string CodexState {get{return codexState;}set {if(codexState==value)return;codexState=value;
            if(value=="idle" && queuedCodex){queuedClip=null;queuedCodex=false;}
            if(value=="running")QueueWorkAction();
            else if(value=="waiting")QueueAction("歪头",false,40,true);
            else if(value=="ready")QueueAction("小庆祝",false,40,true);
            else if(value=="error")QueueAction("摇头",false,50,true);
        }}
        bool completeHeadIdle, forwardLoop;
        int idleFrameRate=24;
        readonly string actionRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","actions-bedroom");
        readonly Dictionary<string,double> actionTimes=new Dictionary<string,double>();
        string lastWorkClip;
        string queuedClip,activeClip;int queuedPriority;double idleEpoch,idleTransitionUntil;bool modernPlaying,queuedCodex;
        string queuedState,queuedCaptionKey,activeState,activeCaptionKey;
        public string ActiveState {get{return activeState;}}
        public string ActiveCaptionKey {get{return activeCaptionKey;}}
        public int ActionSerial {get;private set;}
        sealed class IdleVariant {public string Name;public string[] Files;public int Fps;public bool ReturnLoop;}
        readonly List<IdleVariant> idleVariants=new List<IdleVariant>();readonly Random idleRandom=new Random();
        int variantIndex;bool idleReturnLoop;
        public int IdleVariantCount {get{return idleVariants.Count;}}
        public string IdleName {get{return idleVariants.Count==0?"default":idleVariants[variantIndex].Name;}}
        public string ActiveClip {get{return activeClip;}}
        public string PendingClip {get{return queuedClip;}}
        public static string[] ActionNames {get{return new[]{"比心","歪头","抱抱","伸懒腰","护眼休息","打哈欠","喝水","擦汗","跳舞","欢快舞蹈","转圈","读书","游戏陪伴","代码","开会","电量告急","充电","带伞提醒","加衣提醒","内存告急","显存告急","磁盘提醒","网络慢","网络飞速","递本子","点头","摇头","叹气","忙碌","电脑打字","打盹","开心","打招呼","摸鱼","蹦跳庆祝","托腮陪伴","爱心灯","窗边回望","捧杯暖手","舒展手臂","挥手加油","俏皮指挥","空气吉他","偶像应援","兔兔跳","慢歌摇摆","小鼓手","可爱拳击舞","麦克风主唱","优雅谢幕"};}}
        int observedEvent=-1; double bubbleUntil; string bubble=""; bool night;
        public PetVideo() { string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","video-trial","robot-front.png"); if(File.Exists(p)) neutral=LoadNormalizedFrame(p);
            string fixedRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","idle-v3");
            string bedroom=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","idle-bedroom");
            if(File.Exists(Path.Combine(bedroom,"ready.txt")) && Directory.Exists(Path.Combine(bedroom,"frames"))) {fixedRoot=bedroom;forwardLoop=true;}
            string seamless=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","idle-bedroom-v3");
            if(File.Exists(Path.Combine(seamless,"ready.txt")) && Directory.Exists(Path.Combine(seamless,"frames"))) {fixedRoot=seamless;forwardLoop=true;idleFrameRate=30;}
            completeHeadIdle=Directory.Exists(Path.Combine(fixedRoot,"frames"));
            if(completeHeadIdle && File.Exists(Path.Combine(fixedRoot,"reference.png"))) {if(neutral!=null)neutral.Dispose();neutral=LoadNormalizedFrame(Path.Combine(fixedRoot,"reference.png"));}
            string idleFolder=completeHeadIdle?Path.Combine(fixedRoot,"frames"):Path.Combine(root,"tilt");
            if(Directory.Exists(idleFolder)) {idleFiles=Directory.GetFiles(idleFolder,"*.jpg");Array.Sort(idleFiles,StringComparer.Ordinal);}
            idleBaseFiles=idleFiles;
            if(idleFiles!=null && idleFiles.Length>0)idleVariants.Add(new IdleVariant{Name="待机招手",Files=idleFiles,Fps=idleFrameRate});
            foreach(string id in new[]{"idle-look","idle-hair","idle-sway","idle-core"}) {
                string folder=Path.Combine(actionRoot,id,"approved-frames");
                if(!File.Exists(Path.Combine(actionRoot,id,"ready.txt")) || !Directory.Exists(folder))continue;
                var variantFiles=Directory.GetFiles(folder,"*.jpg");Array.Sort(variantFiles,StringComparer.Ordinal);
                if(variantFiles.Length>1)idleVariants.Add(new IdleVariant{Name=id,Files=variantFiles,Fps=24,ReturnLoop=true});
            }
        }
        public bool Available { get { return neutral!=null; } }
        static Bitmap LoadNormalizedFrame(string path) {
            using(var source=Image.FromFile(path)) {
                var normalized=new Bitmap(FrameWidth,FrameHeight,PixelFormat.Format24bppRgb);
                using(var g=Graphics.FromImage(normalized)) {
                    g.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    float scale=Math.Max(FrameWidth/(float)source.Width,FrameHeight/(float)source.Height);
                    float width=source.Width*scale,height=source.Height*scale;
                    // Match the existing top-anchored, center-cropped pet viewport.
                    g.DrawImage(source,(FrameWidth-width)*.5f,0,width,height);
                }
                return normalized;
            }
        }
        public static string Clip(string state) {
            switch(state) {
            case "蹦跳庆祝":return "daily-happy-hop-haomao";
            case "护眼休息":return "daily-final-window";
            case "托腮陪伴":return "daily-final-chin";
            case "爱心灯":return "daily-final-heartlight";
            case "窗边回望":return "daily-final-window";
            case "捧杯暖手":return "daily-final-cup";
            case "舒展手臂":return "daily-final-stretch";
            case "挥手加油":return "daily-final-cheer";
            case "俏皮指挥":return "daily-final-conductor";
            case "空气吉他":return "music-guitar";
            case "偶像应援":return "music-support";
            case "兔兔跳":return "music-bunny";
            case "慢歌摇摆":return "music-slow";
            case "小鼓手":return "music-drums";
            case "可爱拳击舞":return "music-boxing";
            case "麦克风主唱":return "music-singer";
            case "优雅谢幕":return "music-bow";
            case "战术出击":return "gaming-tactical-sealed";
            case "电脑打字":return "work-typing";
            case "唤醒舞蹈":return "apt-wake";
            case "小彩蛋":case "比心":return "heart";
            case "歪头":case "wink":case "等待":return "tilt";
            case "抱抱":case "安慰":return "hug";
            case "伸懒腰":case "活动一下":case "运动":return "stretch";
            case "打哈欠":case "早点休息":return "yawn";
            case "喝水":return "sip";case "擦汗":return "wipe";
            case "跳舞":return "dance";case "欢快舞蹈":case "小庆祝":return "dance-party";case "转圈":return "twirl";case "读书":return "read";
            case "游戏陪伴":case "游戏":return "gaming";
            case "代码":case "记笔记":return "coding";case "开会":return "meeting";
            case "电量告急":case "惊讶":return "shock";case "充电":return "charge";
            case "带伞提醒":case "下雨提醒":return "umbrella";case "加衣提醒":case "冷天提醒":return "cold";
            case "内存告急":case "内存提醒":case "过载":return "headhold";
            case "显存告急":return "coreguard";case "磁盘提醒":return "disk";
            case "网络慢":return "lag";case "网络飞速":return "fastnet";
            case "递本子":return "notebook";case "点头":return "nod";
            case "摇头":return "shake";case "叹气":return "sigh";
            case "忙碌":case "专注":return "work";case "打盹":return "sleep";
            case "开心":case "星星眼":return "cheer";
            case "打招呼":return "hello";case "摸鱼":return "phone";
            default:return null; }
        }
        public bool HasAction(string state) {string clip=Clip(state);return clip!=null && File.Exists(Path.Combine(actionRoot,clip,"ready.txt"));}
        bool gameWasRunning,gameIntroPending,musicBlocked;
        public void SetMusicBlocked(bool blocked) {
            musicBlocked=blocked;
            if(!blocked)return;
            if(queuedClip!=null && queuedClip.StartsWith("music-",StringComparison.Ordinal)) {queuedClip=null;queuedState=null;queuedCaptionKey=null;queuedPriority=0;queuedCodex=false;}
            if(activeClip!=null && activeClip.StartsWith("music-",StringComparison.Ordinal)) {
                modernPlaying=false;files=null;activeClip=null;activeState=null;activeCaptionKey=null;idleEpoch=clock.Elapsed.TotalSeconds;
                if(frame!=null){frame.Dispose();frame=null;}index=-1;
            }
        }
        public void CancelMusic(){bool wasBlocked=musicBlocked;SetMusicBlocked(true);musicBlocked=wasBlocked;}
        public void QueueMusic(string state){if(Clip(state)!=null && Clip(state).StartsWith("music-",StringComparison.Ordinal))QueueAction(state,false,25);}
        public void ObserveGame(bool running) {
            if(running && !gameWasRunning)gameIntroPending=true;
            if(!running){gameIntroPending=false;if(queuedState=="战术出击"){queuedClip=null;queuedState=null;queuedPriority=0;}}
            gameWasRunning=running;
            if(gameIntroPending && activeClip==Clip("战术出击"))gameIntroPending=false;
            if(gameIntroPending && Enabled && HasAction("战术出击") && (queuedClip==null || queuedPriority<=70)) {
                QueueAction("战术出击",true,70);
                if(!modernPlaying)idleEpoch=clock.Elapsed.TotalSeconds;
            }
        }
        bool ForwardAction {get{return activeClip=="apt-wake" || activeClip=="gaming-tactical-sealed" || (activeClip!=null && (activeClip.StartsWith("music-",StringComparison.Ordinal) || activeClip.StartsWith("daily-final-",StringComparison.Ordinal) || activeClip=="daily-happy-hop-haomao"));}}
        public void PlayWakeDance() {
            if(!Enabled || !HasAction("唤醒舞蹈"))return;
            modernPlaying=false;files=null;activeClip=null;activeState=null;activeCaptionKey=null;
            QueueAction("唤醒舞蹈",true,100);idleEpoch=clock.Elapsed.TotalSeconds;
        }
        public void PreviewAction(string state) {QueueAction(state,true,100);if(!modernPlaying)idleEpoch=clock.Elapsed.TotalSeconds;}
        public static int NextIdleVariant(int current,int count,int randomValue) {return count<2?0:(current+1+randomValue%(count-1))%count;}
        double IdleSpeed {get{return night?.7:1;}}
        double IdlePeriod {get{return idleFiles==null?12:idleReturnLoop?(idleFiles.Length-1)/24.0*2:idleFiles.Length/(double)idleFrameRate;}}
        void QueueWorkAction() {
            if(queuedClip!=null && queuedPriority>20)return;
            string state=lastWorkClip!="work-typing" && HasAction("电脑打字")?"电脑打字":"忙碌";
            QueueAction(state,true,20,true);
        }
        void QueueAction(string state,bool force,int priority,bool fromCodex=false) {
            if(!force && !fromCodex && codexState!="idle" && priority<60)return;
            string clip=Clip(state);if(clip==null || !HasAction(state) || (musicBlocked && clip.StartsWith("music-",StringComparison.Ordinal)))return;
            double last;if(!force && actionTimes.TryGetValue(clip,out last) && clock.Elapsed.TotalSeconds-last<45)return;
            if(!force && (activeClip==clip || (queuedClip!=null && queuedPriority>priority)))return;
            queuedClip=clip;queuedPriority=priority;queuedCodex=fromCodex;
            queuedState=state;queuedCaptionKey=fromCodex?"Codex/"+codexState:state;
        }
        public static int ActionFrameAt(double seconds,int count) {
            if(count<2)return 0;double duration=(count-1)/24.0*2;
            double phase=Math.Max(0,Math.Min(1,seconds/duration));
            return (int)Math.Round((count-1)*(1-Math.Cos(phase*Math.PI*2))*.5);
        }
        void AdvanceActions() {
            if(!Enabled)return;
            double now=clock.Elapsed.TotalSeconds;
            bool neutralBridge=false;
            if(modernPlaying) {
                double duration=ForwardAction?files.Length/24.0:(files.Length-1)/24.0*2/IdleSpeed;
                if(now-started<duration)return;
                // Hold the final, neutral-matching action frame during a short crossfade.
                // Keep `modernPlaying` and its frame list alive until the fade is complete,
                // so no frame-size/crop jump occurs at the action boundary.
                modernPlaying=false;idleEpoch=now;actionExitUntil=now+.32;idleTransitionUntil=actionExitUntil;
            }
            if(actionExitUntil>0 && now>=actionExitUntil) {
                actionExitUntil=0;files=null;activeClip=null;activeState=null;activeCaptionKey=null;
                // Resume the moving idle loop from the exact neutral frame used by the fade.
                if(idleBaseFiles!=null && idleBaseFiles.Length>0) {idleFiles=idleBaseFiles;idleFrameRate=forwardLoop?30:24;idleReturnLoop=false;idleIndex=-1;}
                idleEpoch=now;
            }
            if(idleTransitionUntil>0) {if(now<idleTransitionUntil)return;idleTransitionUntil=0;neutralBridge=true;}
            if(queuedClip==null && codexState=="running")QueueWorkAction();
            if(queuedClip==null)return;
            // Each prepared action begins at the idle reference and returns along its own path.
            double phase=(now-idleEpoch)*IdleSpeed;
            double period=IdlePeriod;
            if(!neutralBridge && phase%period>.12 && phase%period<period-.12)return;
            string folder=Path.Combine(actionRoot,queuedClip,"approved-frames");
            if(!Directory.Exists(folder)){queuedClip=null;return;}
            var next=Directory.GetFiles(folder,"*.jpg");Array.Sort(next,StringComparer.Ordinal);
            if(next.Length<2){queuedClip=null;return;}
            files=next;activeClip=queuedClip;queuedClip=null;queuedPriority=0;
            activeState=queuedState;activeCaptionKey=queuedCaptionKey;ActionSerial++;
            if(activeState=="战术出击")gameIntroPending=false;
            if(activeClip=="work" || activeClip=="work-typing")lastWorkClip=activeClip;
            modernPlaying=true;started=now;actionTimes[activeClip]=now;index=-1;
            if(frame!=null){frame.Dispose();frame=null;}
        }
        public void Observe(PetRules mood) {
            night=mood.Night;
            if(observedEvent!=mood.EventNumber) {observedEvent=mood.EventNumber; bubble=mood.Message;bubbleUntil=clock.Elapsed.TotalSeconds+3;QueueAction(mood.State,mood.State=="护眼休息",mood.State=="护眼休息"?60:30);}
            Observe(mood.State);
        }
        public void Observe(string state) {
            if(state==previous)return; previous=state;
            if(HasAction(state)) {QueueAction(state,false,state=="过载"||state=="内存告急"||state=="电量告急"?80:state=="内存提醒"||state=="显存告急"?60:10);return;}
            if(state=="过载"||state=="内存告急"||state=="电量告急") { files=null; return; }
            if(completeHeadIdle)return; // Old action clips have a cropped crown; keep them quarantined.
            string clip=Clip(state); if(clip==null || clock.Elapsed.TotalSeconds-lastPlayed<6)return;
            string folder=Path.Combine(root,clip); if(!Directory.Exists(folder))return;
            var next=Directory.GetFiles(folder,"*.jpg"); Array.Sort(next,StringComparer.Ordinal);if(next.Length==0)return;
            files=next; started=lastPlayed=clock.Elapsed.TotalSeconds;index=-1;
        }
        public void Draw(Graphics g,RectangleF bounds,PetRules mood,bool effects) {
            AdvanceActions();
            var panelClip=g.Save();g.SetClip(bounds,System.Drawing.Drawing2D.CombineMode.Intersect);
            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            using(var background=new System.Drawing.Drawing2D.LinearGradientBrush(bounds,Color.FromArgb(139,166,193),Color.FromArgb(211,226,239),90f))g.FillRectangle(background,bounds);
            double speed=(modernPlaying && !ForwardAction)?IdleSpeed:(night?.85:1);
            double elapsed=(clock.Elapsed.TotalSeconds-started)*speed;
            if(modernPlaying && ForwardAction)elapsed=clock.Elapsed.TotalSeconds-started;
            if(!modernPlaying && files!=null && actionExitUntil<=0 && elapsed>=files.Length/24.0) {
                // Legacy clips also use the same neutral crossfade as prepared actions.
                int last=files.Length-1;
                if(last>=0 && index!=last)try {var next=LoadNormalizedFrame(files[last]);if(frame!=null)frame.Dispose();frame=next;index=last;}catch(IOException){}
                actionExitUntil=clock.Elapsed.TotalSeconds+.32;idleTransitionUntil=actionExitUntil;idleEpoch=clock.Elapsed.TotalSeconds;
            }
            bool exitingAction=Enabled && actionExitUntil>clock.Elapsed.TotalSeconds && files!=null && frame!=null;
            bool playing=Enabled && files!=null && (modernPlaying || exitingAction || elapsed<files.Length/24.0);
            Image image=neutral; RectangleF source=new RectangleF(0,0,neutral.Width,neutral.Height);
            if(exitingAction) {
                image=frame;source=new RectangleF(0,0,frame.Width,frame.Height);
            } else if(playing) {
                int wanted=modernPlaying && !ForwardAction?ActionFrameAt(elapsed,files.Length):Math.Min(files.Length-1,(int)(elapsed*24));
                if(index!=wanted) { try { var next=LoadNormalizedFrame(files[wanted]);if(frame!=null)frame.Dispose();frame=next;index=wanted; }catch(IOException){} }
                if(frame!=null) { image=frame;source=new RectangleF(0,0,frame.Width,frame.Height); }
            }
            // Compensate the dashboard's legacy 1110x314 -> 1920x480 scaling.
            float sx,sy; using(var transform=g.Transform) { var e=transform.Elements;sx=Math.Abs(e[0]);sy=Math.Abs(e[3]); }
            float aspectCorrection=sx>0?sy/sx:1;
            float scale=Math.Max(bounds.Width/(source.Width*aspectCorrection),bounds.Height/source.Height);
            float displayWidth=source.Width*scale*aspectCorrection;
            var target=new RectangleF(bounds.X+(bounds.Width-displayWidth)/2,bounds.Y,displayWidth,source.Height*scale);
            if(!playing) {
                DrawIdle(g,bounds,target,source);
            } else if(modernPlaying) {
                g.DrawImage(image,target,source,GraphicsUnit.Pixel);
            } else if(exitingAction) {
                double remaining=Math.Max(0,actionExitUntil-clock.Elapsed.TotalSeconds);
                float opacity=(float)(remaining/.32);
                opacity=opacity*opacity*(3-2*opacity);
                var idleSource=new RectangleF(0,0,neutral.Width,neutral.Height);
                float idleScale=Math.Max(bounds.Width/(idleSource.Width*aspectCorrection),bounds.Height/idleSource.Height);
                float idleWidth=idleSource.Width*idleScale*aspectCorrection;
                var idleTarget=new RectangleF(bounds.X+(bounds.Width-idleWidth)/2,bounds.Y,idleWidth,idleSource.Height*idleScale);
                DrawIdle(g,bounds,idleTarget,idleSource,true);
                using(var attributes=new ImageAttributes()) {
                    var matrix=new ColorMatrix();matrix.Matrix33=opacity;attributes.SetColorMatrix(matrix);
                    g.DrawImage(image,new [] {new PointF(target.Left,target.Top),new PointF(target.Right,target.Top),new PointF(target.Left,target.Bottom)},source,GraphicsUnit.Pixel,attributes);
                }
            } else {
                // Soften entry/exit rather than snapping directly between unrelated poses.
                double duration=files.Length/24.0;
                float opacity=(float)Math.Min(1,Math.Min(elapsed/.4,(duration-elapsed)/.55));
                opacity=Math.Max(0,opacity); opacity=opacity*opacity*(3-2*opacity);
                var idleSource=new RectangleF(0,0,neutral.Width,neutral.Height);
                float idleScale=Math.Max(bounds.Width/(idleSource.Width*aspectCorrection),bounds.Height/idleSource.Height);
                float idleWidth=idleSource.Width*idleScale*aspectCorrection;
                var idleTarget=new RectangleF(bounds.X+(bounds.Width-idleWidth)/2,bounds.Y,idleWidth,idleSource.Height*idleScale);
                DrawIdle(g,bounds,idleTarget,idleSource,true);
                using(var attributes=new ImageAttributes()) {
                    var matrix=new ColorMatrix(); matrix.Matrix33=opacity;attributes.SetColorMatrix(matrix);
                    g.DrawImage(image,new [] {new PointF(target.Left,target.Top),new PointF(target.Right,target.Top),new PointF(target.Left,target.Bottom)},source,GraphicsUnit.Pixel,attributes);
                }
            }
            // A quiet, real-time breathing cue makes the still idle loop feel alive.
            // Keep it off action clips so it never competes with their motion.
            if(!playing && Enabled) DrawIdleLife(g,bounds);
            if(effects) DrawAtmosphere(g,bounds,mood);
            if(forwardLoop && !modernPlaying && CodexState!="idle") DrawCodexLight(g,bounds);
            if(ShowBubbles && clock.Elapsed.TotalSeconds<bubbleUntil && bubble.Length>0) {
                var box=new RectangleF(bounds.X+12,bounds.Bottom-34,bounds.Width-24,26);
                using(var b=new SolidBrush(Color.FromArgb(185,20,28,40)))g.FillRectangle(b,box);
                using(var f=new Font("Microsoft YaHei",11,FontStyle.Regular,GraphicsUnit.Pixel))using(var b=new SolidBrush(Color.White))g.DrawString(bubble,f,b,box);
            }
            g.Restore(panelClip);
        }
        void DrawIdleLife(Graphics g,RectangleF b) {
            double t=clock.Elapsed.TotalSeconds;
            float breath=(float)(.5+.5*Math.Sin(t*(Math.PI*2/4.8)));
            float x=b.Left+b.Width*.515f;
            float y=b.Top+b.Height*.563f+(float)Math.Sin(t*(Math.PI*2/4.8))*1.1f;
            float rx=15f+2.2f*breath, ry=16f+2.4f*breath;
            var saved=g.Save();
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var path=new System.Drawing.Drawing2D.GraphicsPath()) {
                path.AddEllipse(x-rx,y-ry,rx*2,ry*2);
                using(var glow=new System.Drawing.Drawing2D.PathGradientBrush(path)) {
                    glow.CenterColor=Color.FromArgb((int)(18+16*breath),80,225,255);
                    glow.SurroundColors=new[]{Color.FromArgb(0,80,225,255)};
                    g.FillPath(glow,path);
                }
            }
            float ring=10.5f+2.2f*breath;
            using(var pen=new Pen(Color.FromArgb((int)(25+24*breath),130,239,255),1.15f))
                g.DrawEllipse(pen,x-ring,y-ring,ring*2,ring*2);
            g.Restore(saved);
        }
        void DrawCodexLight(Graphics g,RectangleF b) {
            // Small light around the core, instead of adding a frame or covering the room.
            double t=Enabled?clock.Elapsed.TotalSeconds:0;
            Color c=CodexState=="ready"?Color.SpringGreen:CodexState=="waiting"?Color.Gold:CodexState=="error"?Color.Orange:Color.DeepSkyBlue;
            float x=b.Left+b.Width*.515f,y=b.Top+b.Height*.563f;
            float pulse=(float)(.5+.5*Math.Sin(t*(CodexState=="waiting"?3:1.8)));
            using(var path=new System.Drawing.Drawing2D.GraphicsPath()) {
                path.AddEllipse(x-18,y-19,36,38);
                using(var glow=new System.Drawing.Drawing2D.PathGradientBrush(path)) {
                    glow.CenterColor=Color.FromArgb((int)(85+40*pulse),c);glow.SurroundColors=new[]{Color.FromArgb(0,c)};g.FillPath(glow,path);
                }
            }
        }
        void DrawAtmosphere(Graphics g,RectangleF b,PetRules mood) {
            double t=Enabled?clock.Elapsed.TotalSeconds:0;
            float strength=night?.45f:1f;
            Color color=mood.Accent;
            // Draw only in the side margins; keep the head and hand animation unobstructed.
            var saved=g.Save();
            using(var region=new Region(b)) {
                region.Exclude(new RectangleF(b.Left+b.Width*.24f,b.Top,b.Width*.52f,b.Height));
                g.SetClip(region,System.Drawing.Drawing2D.CombineMode.Intersect);
            }
            // Saturated flowing ribbons remain visible against the pale footage.
            for(int side=0;side<2;side++) {
                Color ribbon=side==0?Color.FromArgb(0,215,255):Color.FromArgb(182,50,255);
                var points=new PointF[48];
                for(int i=0;i<points.Length;i++) {
                    float y=b.Top+i*b.Height/(points.Length-1);
                    float x=b.Left+b.Width*(side==0?.10f:.90f)+(float)Math.Sin(i*.13+t*1.3+side*2)*16;
                    points[i]=new PointF(x,y);
                }
                for(int layer=4;layer>=0;layer--)using(var pen=new Pen(Color.FromArgb((int)(strength*(layer==0?220:layer==1?100:25)),ribbon),layer==0?1.5f:layer*4))g.DrawLines(pen,points);
                double travel=(t*.2+side*.5)%1;
                float yy=b.Bottom-(float)travel*b.Height;
                float xx=b.Left+b.Width*(side==0?.10f:.90f)+(float)Math.Sin((yy-b.Top)/b.Height*47*.13+t*1.3+side*2)*16;
                using(var dot=new SolidBrush(Color.FromArgb((int)(strength*240),Color.White)))g.FillEllipse(dot,xx-2.2f,yy-2.2f,4.4f,4.4f);
            }
            for(int side=0;side<2;side++) {
                float cx=b.Left+b.Width*(side==0?.1f:.9f),cy=b.Top+b.Height*.68f;
                using(var path=new System.Drawing.Drawing2D.GraphicsPath()) {
                    path.AddEllipse(cx-65,cy-100,130,200);
                    using(var glow=new System.Drawing.Drawing2D.PathGradientBrush(path)) {
                        glow.CenterColor=Color.FromArgb((int)(strength*(100+30*Math.Sin(t*.7+side))),color);
                        glow.SurroundColors=new[]{Color.FromArgb(0,color)};g.FillPath(glow,path);
                    }
                }
                for(int i=0;i<18;i++) {
                    float x=b.Left+b.Width*(side==0?.035f:.79f)+(i*37%72)+(float)Math.Sin(t*.45+i)*5;
                    double pos=(i*29+t*(5+i%3))% (b.Height+20);
                    float y=b.Bottom+10-(float)pos;
                    float radius=i%4==0?3.3f:1.8f;
                    int alpha=(int)(strength*(135+110*(.5+.5*Math.Sin(t*1.1+i))));
                    using(var halo=new SolidBrush(Color.FromArgb(alpha/4,color)))g.FillEllipse(halo,x-7,y-7,14,14);
                    using(var dot=new SolidBrush(Color.FromArgb(alpha,color)))g.FillEllipse(dot,x-radius,y-radius,radius*2,radius*2);
                    if(i%6==0) using(var star=new Pen(Color.FromArgb(alpha/2,Color.White),.7f)) {
                        g.DrawLine(star,x-4,y,x+4,y);g.DrawLine(star,x,y-4,x,y+4);
                    }
                }
                double phase=(t+side*6)%13;
                if(phase<2.4) {
                    float x=b.Left+b.Width*(side==0?.12f:.88f);
                    float y=b.Top+(float)(phase/2.4)*b.Height;
                    using(var pen=new Pen(Color.FromArgb((int)(strength*65*Math.Sin(phase/2.4*Math.PI)),color),1))g.DrawLine(pen,x-9,y-25,x+9,y+25);
                }
            }
            g.Restore(saved);
        }
        public static int IdleFrameAt(double seconds,int count) {
            // Use the calm hands-down part; cosine slows at both reversal points.
            int first=Math.Min(42,count-1),last=Math.Min(110,count-1);
            double phase=(1-Math.Cos(seconds*Math.PI/5.5))*.5;
            return first+(int)Math.Round((last-first)*phase);
        }
        void DrawIdle(Graphics g,RectangleF bounds,RectangleF target,RectangleF source,bool freezeAtNeutral=false) {
            double t=Enabled?(clock.Elapsed.TotalSeconds-idleEpoch)*IdleSpeed:0;
            if(!freezeAtNeutral && Enabled && t>=IdlePeriod && idleVariants.Count>1 && queuedClip==null) {
                variantIndex=NextIdleVariant(variantIndex,idleVariants.Count,idleRandom.Next(10000));
                var variant=idleVariants[variantIndex];idleFiles=variant.Files;idleFrameRate=variant.Fps;idleReturnLoop=variant.ReturnLoop;
                idleEpoch=clock.Elapsed.TotalSeconds;idleIndex=-1;t=0;
            }
            Image idle=neutral;
            string[] drawFiles=freezeAtNeutral && idleBaseFiles!=null && idleBaseFiles.Length>0?idleBaseFiles:idleFiles;
            if(drawFiles!=null && drawFiles.Length>0) {
                int wanted=freezeAtNeutral?0:idleReturnLoop?ActionFrameAt(t%IdlePeriod,drawFiles.Length):forwardLoop?(int)(t*idleFrameRate)%drawFiles.Length:completeHeadIdle?(int)Math.Round((drawFiles.Length-1)*(1-Math.Cos(t*Math.PI/5.8))*.5):IdleFrameAt(t,drawFiles.Length);
                string path=drawFiles[wanted];
                if(idleFramePath!=path) try {
                    var next=LoadNormalizedFrame(path);if(idleFrame!=null)idleFrame.Dispose();idleFrame=next;
                    idleIndex=wanted;
                    idleFramePath=path;
                }catch(IOException){}
                if(idleFrame!=null) {
                    idle=idleFrame;source=new RectangleF(0,0,idle.Width,idle.Height);
                    float correction;using(var transform=g.Transform){var e=transform.Elements;correction=Math.Abs(e[2])<.001 && Math.Abs(e[0])>.001?Math.Abs(e[3]/e[0]):1;}
                    float scale=Math.Max(bounds.Width/(source.Width*correction),bounds.Height/source.Height);
                    float width=source.Width*scale*correction;
                    target=new RectangleF(bounds.X+(bounds.Width-width)/2,bounds.Y,width,source.Height*scale);
                }
            }
            var state=g.Save();g.SetClip(bounds,System.Drawing.Drawing2D.CombineMode.Intersect);
            g.DrawImage(idle,target,source,GraphicsUnit.Pixel);g.Restore(state);
        }
        public void Dispose(){if(frame!=null)frame.Dispose();if(neutral!=null)neutral.Dispose();if(idleFrame!=null)idleFrame.Dispose();}
    }
}
