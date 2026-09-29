using System;
using System.Drawing;
using System.Runtime.InteropServices;
namespace SideScreenMonitor {
    // Evaluates observations only: never captures keystrokes, clipboard contents or browser pages.
    public sealed class PetSignals {
        public double Cpu, Temperature=-1, Memory, Vram=-1, IdleSeconds=-1, Download;
        public bool Game, OnBattery;
        public double DiskFree=100, Battery=100, Outdoor=99;
        public bool Precipitation; public string App="";
    }
    public sealed class PetRules {
        public bool AutoPerform=true;
        public string State="陪伴", Message="", ColorName="蓝色";
        public Color Accent=Color.DeepSkyBlue;
        double highCpuSeconds, activeSeconds, previousIdle;
        DateTime previous=DateTime.MinValue, reminderAt=DateTime.MinValue;
        string manual=""; DateTime manualUntil;
        bool overloaded, wasBattery;
        double nextYawn=1800,nextSip=2700,nextExercise=3600,nextBreak=7200;
        DateTime nextRandom=DateTime.MinValue,nextWeather=DateTime.MinValue;
        bool greeted; readonly Random random=new Random(); string lastAuto="", manualMessage="";
        DateTime nextCelebration=DateTime.MinValue,nextNight=DateTime.MinValue;
        bool working; double recovered;
        public bool Night {get;private set;}
        public DateTime LastGreetingDay=DateTime.MinValue;
        public int EventNumber {get;private set;}
        public void Request(string action,string message,DateTime now) {Request(action,now);manualMessage=message;}
        public static int RandomDelay(bool night,int value) {return night?180+value%121:45+value%46;}
        [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size, Tick; }
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput input);
        public static double IdleSeconds() {
            var input=new LastInput {Size=8};
            return GetLastInputInfo(ref input) ? unchecked((uint)Environment.TickCount-input.Tick)/1000.0 : -1;
        }
        public void Request(string action,DateTime now) { manual=action; manualMessage=""; manualUntil=now.AddSeconds(8); EventNumber++; }
        void Set(string state,string message,string color) {
            State=state; Message=message; ColorName=color;
            Accent=color=="红色"?Color.OrangeRed:color=="橙色"?Color.Orange:color=="黄色"?Color.Gold:color=="绿色"?Color.SpringGreen:color=="紫色"?Color.MediumPurple:Color.DeepSkyBlue;
        }
        public void Update(PetSignals s,DateTime now) {
            Night=now.Hour>=23 || now.Hour<7;
            double dt=previous==DateTime.MinValue?0:Math.Max(0,Math.Min(5,(now-previous).TotalSeconds)); previous=now;
            highCpuSeconds=s.Cpu>60 ? highCpuSeconds+dt : 0;
            if(highCpuSeconds>=300)working=true;
            recovered=working && s.Cpu<40?recovered+dt:0;
            bool celebrate=working && recovered>=8; if(celebrate)working=false;
            if(s.IdleSeconds>=300) { if(activeSeconds>=3600 && now>=manualUntil) Request("抱抱",now); activeSeconds=0; nextYawn=1800;nextSip=2700;nextExercise=3600;nextBreak=7200; } else if(s.IdleSeconds>=0) activeSeconds+=dt;
            bool wake=previousIdle>=300 && s.IdleSeconds>=0 && s.IdleSeconds<2; previousIdle=s.IdleSeconds;
            bool unplugged=wasBattery; wasBattery=s.OnBattery;
            if(unplugged && !s.OnBattery) Request("充电",now);
            bool wasOverloaded=overloaded;
            overloaded=s.Cpu>90 || s.Temperature>80 || (overloaded && (s.Cpu>85 || s.Temperature>76));
            if(s.OnBattery && s.Battery<10) { Set("电量告急","电量不足 10%，请接通电源。","红色"); return; }
            if(overloaded) { Set("过载","负载或温度偏高，缓一缓。","红色"); return; }
            if(s.Memory>95) { Set("内存告急","内存占用超过 95%，看看是否需要关闭程序。","红色"); return; }
            if(s.Vram>90) { Set("显存告急","显存占用超过 90%。","橙色"); return; }
            if(s.Memory>85) { Set("内存提醒","内存占用超过 85%。","黄色"); return; }
            if(s.DiskFree<10) { Set("磁盘提醒","系统盘剩余不足 10%。","黄色"); return; }
            if(wasOverloaded && now>=manualUntil) Request("擦汗",now);
            if(celebrate && !s.Game && now>=manualUntil && now>=nextCelebration) {nextCelebration=now.AddMinutes(10);Request("小庆祝","这一阵忙完啦，给你挥挥手！",now);}
            if(wake && now>=manualUntil) Request("打招呼","你回来啦，我一直在。",now);
            if(now<manualUntil) {
                string color=manual=="比心"||manual=="开心"?"绿色":manual=="打盹"||manual=="歪头"?"紫色":manual=="摇头"?"红色":manual=="忙碌"?"橙色":manual=="抱抱"||manual=="喝水"||manual=="伸懒腰"||manual=="打哈欠"?"黄色":"蓝色";
                Set(manual,manualMessage.Length>0?manualMessage:manual=="比心"?"送你一颗心。":manual=="抱抱"?"辛苦啦，抱抱。":manual=="伸懒腰"||manual=="活动一下"?"活动一下肩膀吧。":manual=="喝水"?"记得喝口水。":manual=="打招呼"?"你回来啦。":"我陪着你。",color); return;
            }
            if(s.IdleSeconds>=900) { Set("打盹","安静陪你休息。","紫色"); return; }
            if(s.IdleSeconds>=300 && s.Cpu<20) { Set("待机","你忙完了，我在这里。","紫色"); return; }
            if(highCpuSeconds>=300) { Set("忙碌","高负载已持续 5 分钟，一件一件来。","橙色"); return; }
            // Suppress routine reminders during gameplay and separate them by at least 15 minutes.
            if(!s.Game && s.IdleSeconds>=0 && s.IdleSeconds<60 && now>=reminderAt) {
                string reminder="";
                if(activeSeconds>=nextBreak) {reminder="伸懒腰";nextBreak+=7200;}
                else if(activeSeconds>=nextExercise) {reminder="活动一下";nextExercise+=3600;}
                else if(activeSeconds>=nextSip) {reminder="喝水";nextSip+=2700;}
                else if(activeSeconds>=nextYawn) {reminder="打哈欠";nextYawn+=1800;}
                else if(Night && now>=nextNight) {reminder="早点休息";nextNight=now.AddHours(1);}
                if(reminder.Length>0) { reminderAt=now.AddMinutes(5); Request(reminder,now); Set(reminder,"忙了一阵，休息一下吧。","黄色"); return; }
            }
            if(!greeted || LastGreetingDay.Date!=now.Date) {
                greeted=true;nextRandom=now.AddSeconds(Night?180:45);
                if(LastGreetingDay.Date!=now.Date) {LastGreetingDay=now.Date;Request("打招呼",Night?"夜深了，轻轻陪你一会儿。":now.Hour<12?"早上好，今天也一起加油。":"你好呀，今天也陪着你。",now);Set("打招呼",manualMessage,"绿色");return;}
            }
            if(!s.Game && now>=nextWeather && (s.Precipitation||s.Outdoor<0)) {nextWeather=now.AddHours(3);Request(s.Precipitation?"带伞提醒":"加衣提醒",s.Precipitation?"长春有雨雪，出门记得带伞。":"长春气温低于零度，出门多穿一点。",now);Set(s.Precipitation?"带伞提醒":"加衣提醒",s.Precipitation?"长春有雨雪，出门记得带伞。":"长春气温低于零度，出门多穿一点。","黄色");return;}
            if(s.App=="meeting") {Set("开会","安静陪你开会。","蓝色");return;}
            if(s.App=="coding") {Set("代码","专注眼前这一段。","蓝色");return;}
            if(AutoPerform && !s.Game && now>=nextRandom) {
                nextRandom=now.AddSeconds(RandomDelay(Night,random.Next(10000)));
                int roll=random.Next(100);
                string action=roll<(Night?4:12)?"小彩蛋":!Night&&roll<22?"欢快舞蹈":!Night&&roll<32?"跳舞":!Night&&roll<42?"点头":"歪头";
                if(action==lastAuto && action!="歪头")action="歪头";lastAuto=action;
                Request(action,action=="小彩蛋"?"偷偷送你一颗心。":action=="转圈"||action=="欢快舞蹈"?"给你跳一小段，开心一下。":new[]{"忙着呢？","我在呀。","慢慢来。"}[random.Next(3)],now);
                Set(action,manualMessage,action=="歪头"?"紫色":"绿色");return;
            }
            if(s.Game) { Set("游戏陪伴","专注这一局，我陪你。","蓝色"); return; }
            if(s.Download>10*1024*1024) { Set("开心","当前下载速度超过 10 MB/s。","绿色"); return; }
            Set(s.IdleSeconds>=0 && s.IdleSeconds<15?"专注":"陪伴","我在。","蓝色");
        }
    }
}
