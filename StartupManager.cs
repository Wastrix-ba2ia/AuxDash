using System;
using System.Diagnostics;
using Microsoft.Win32;
namespace SideScreenMonitor {
 public static class StartupManager {
  const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
  const string ValueName="AuxDash";
  public static bool IsEnabled(){try{using(var key=Registry.CurrentUser.OpenSubKey(KeyPath,false)){var value=key==null?null:key.GetValue(ValueName) as string;return value!=null && value.IndexOf(Process.GetCurrentProcess().MainModule.FileName,StringComparison.OrdinalIgnoreCase)>=0;}}catch{return false;}}
  public static void SetEnabled(bool enabled){
   using(var key=Registry.CurrentUser.CreateSubKey(KeyPath)){
    if(enabled){string exe=Process.GetCurrentProcess().MainModule.FileName;key.SetValue(ValueName,"\""+exe+"\" --autostart",RegistryValueKind.String);}
    else key.DeleteValue(ValueName,false);
   }
  }
 }
}
