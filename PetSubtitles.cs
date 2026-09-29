using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace SideScreenMonitor {
 public sealed class PetSubtitles {
  readonly Dictionary<string,string[]> lines;
  readonly Dictionary<string,int> counters=new Dictionary<string,int>();
  readonly Random random=new Random();
  string key="",text="";DateTime until,nextRepeat,nextAmbient;int serial=-1;
  public PetSubtitles() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","pet-subtitles.json")) {}
  public PetSubtitles(string path) {
   lines=new Dictionary<string,string[]>();
   try {var loaded=new JavaScriptSerializer().Deserialize<Dictionary<string,string[]>>(File.ReadAllText(path));if(loaded!=null)lines=loaded;}catch(IOException){}catch(ArgumentException){}
  }
  static string Canonical(string state) {
   switch(state){case "小彩蛋":return "比心";case "安慰":return "抱抱";case "活动一下":case "运动":return "伸懒腰";case "小庆祝":return "欢快舞蹈";case "wink":case "等待":return "歪头";case "游戏":return "游戏陪伴";case "记笔记":return "代码";case "下雨提醒":return "带伞提醒";case "冷天提醒":return "加衣提醒";case "星星眼":return "开心";case "惊讶":return "惊讶";default:return state;}
  }
  public bool Has(string state) {string[] values;state=Canonical(state);return state!=null&&lines.TryGetValue(state,out values)&&values!=null&&values.Length>0;}
  string Next(string state) {
   if(!Has(state))return "";
   state=Canonical(state);int previous;string[] choices=lines[state];
   int n=counters.TryGetValue(state,out previous)&&choices.Length>1?(previous+1+random.Next(choices.Length-1))%choices.Length:random.Next(choices.Length);
   counters[state]=n;
   return choices[n%choices.Length]??"";
  }
  public static bool Warning(string state) {return state=="过载"||state=="内存告急"||state=="内存提醒"||state=="显存告急"||state=="磁盘提醒"||state=="电量告急";}
  public string Resolve(string localState,string codexState,string actionKey,int actionSerial,string idleName,bool night,DateTime now) {
   string wanted="";bool repeat=false;double duration=6;
   if(Warning(localState)){wanted=localState;repeat=true;duration=8;}
   else if(codexState=="waiting"||codexState=="error"){wanted="Codex/"+codexState;repeat=true;duration=8;}
   else if(codexState=="running"||codexState=="ready"){wanted="Codex/"+codexState;repeat=codexState=="running";}
   else if(!string.IsNullOrEmpty(actionKey) && !(actionKey.StartsWith("Codex/") && codexState=="idle"))wanted=actionKey.StartsWith("Codex/")?"Codex/"+codexState:actionKey;
   else if(codexState=="running"||codexState=="ready"){wanted="Codex/"+codexState;repeat=codexState=="running";}
   bool newAction=actionSerial!=serial;serial=actionSerial;
   if(wanted.Length>0) {
    // Do not refresh a warning every frame or let a second action restart an unrelated warning.
    bool actionStart=newAction&&wanted==actionKey;
    if(wanted!=key || actionStart || (repeat&&now>=nextRepeat)) {
     key=wanted;text=Next(wanted);until=now.AddSeconds(duration);nextRepeat=now.AddSeconds(45);nextAmbient=now.AddSeconds(night?150:65);
    }
    return now<until?text:"";
   }
   if(key.StartsWith("Codex/") || (key.Length>0 && !key.StartsWith("Idle/"))){key="";text="";until=now;}
   if(now>=nextAmbient) {
    string ambient="Idle/"+idleName;
    key=ambient;text=Next(ambient);if(text.Length==0)text=Next("陪伴");
    until=now.AddSeconds(5);nextAmbient=now.AddSeconds(night?150:65);
   }
   return now<until?text:"";
  }
 }
}
