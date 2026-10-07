using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Threading.Tasks;
namespace SideScreenMonitor {
 public class PrimarySettingsForm:Form {
  public PrimarySettingsForm(){StartPosition=FormStartPosition.Manual;CenterOnPrimary();}
  void CenterOnPrimary(){var area=Screen.PrimaryScreen.WorkingArea;Location=new Point(area.Left+Math.Max(0,(area.Width-Width)/2),area.Top+Math.Max(0,(area.Height-Height)/2));}
  protected override void OnLoad(EventArgs e){base.OnLoad(e);CenterOnPrimary();}
  protected override void OnShown(EventArgs e){base.OnShown(e);CenterOnPrimary();Activate();}
 }
 public sealed class BindingConfig {
  public bool CodexEnabled=true;
  public string Home="",Executable="";
  static string FileName {get{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bindings.xml");}}
  public string ResolvedHome {get {if(!string.IsNullOrWhiteSpace(Home))return Home;string value=Environment.GetEnvironmentVariable("CODEX_HOME");return string.IsNullOrWhiteSpace(value)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"):value;}}
  public static BindingConfig Load(){var c=new BindingConfig();try {var x=ProfileStorage.Read(FileName);c.Home=(string)x.Element("CodexHome")??"";c.Executable=(string)x.Element("CodexExecutable")??"";c.CodexEnabled=(bool?)x.Element("CodexEnabled")??true;}catch{}return c;}
  public bool Save(){return ProfileStorage.Write(FileName,new XElement("Bindings",new XElement("CodexHome",Home),new XElement("CodexExecutable",Executable),new XElement("CodexEnabled",CodexEnabled)));}
 }
 public sealed class BindingSettingsForm:PrimarySettingsForm {
  public BindingConfig Config;
  readonly TextBox home=new TextBox(),exe=new TextBox();readonly CheckBox enabled=new CheckBox();readonly Label status=new Label();
  public BindingSettingsForm(PubgProfile pubg) {
   Config=BindingConfig.Load();Text="账号与联动绑定";ClientSize=new Size(640,410);AutoScaleMode=AutoScaleMode.Dpi;StartPosition=FormStartPosition.Manual;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;
   enabled.Text="启用 Codex 本机联动与额度显示";enabled.SetBounds(20,16,580,28);enabled.Checked=Config.CodexEnabled;Controls.Add(enabled);
   Controls.Add(new Label {Text="先在官方 Codex 登录。本软件不接收账号密码或登录令牌。",Left=20,Top=50,Width=600,Height=24});
   Controls.Add(new Label {Text="Codex 数据目录（包含 sessions；留空自动查找）",Left=20,Top=82,Width=590});home.SetBounds(20,108,510,26);home.Text=Config.Home;Controls.Add(home);
   var browse=new Button {Text="选择目录",Left=538,Top=106,Width=82};browse.Click+=delegate {using(var d=new FolderBrowserDialog()){if(d.ShowDialog(this)==DialogResult.OK)home.Text=d.SelectedPath;}};Controls.Add(browse);
   Controls.Add(new Label {Text="codex.exe（留空自动查找桌面版安装目录）",Left=20,Top=145,Width=590});exe.SetBounds(20,170,510,26);exe.Text=Config.Executable;Controls.Add(exe);
   var executable=new Button {Text="选择程序",Left=538,Top=168,Width=82};executable.Click+=delegate {using(var d=new OpenFileDialog {Filter="Codex 程序|codex.exe"}){if(d.ShowDialog(this)==DialogResult.OK)exe.Text=d.FileName;}};Controls.Add(executable);
   var test=new Button {Text="检测 Codex 连接",Left=20,Top=212,Width=150};
   test.Click+=async delegate {var c=Read();test.Enabled=false;status.Text="检测目录与官方额度接口…";try {string result=await Task.Run(()=>{bool sessions=Directory.Exists(Path.Combine(c.ResolvedHome,"sessions"));var q=CodexQuota.Fetch(c);return (sessions?"会话目录存在；":"会话目录未找到；")+"周剩余额度 "+q.Remaining.ToString("0")+"%。";});if(!IsDisposed)status.Text=result;}catch {if(!IsDisposed)status.Text="未连接：检查数据目录、codex.exe、官方登录及网络。";}finally{if(!IsDisposed)test.Enabled=true;}};Controls.Add(test);
   status.SetBounds(20,247,600,44);status.Text="任务状态读取本机事件文件；该格式随 Codex 版本可能变化。";Controls.Add(status);
   var game=new Button {Text="PUBG：昵称 / API 密钥绑定…",Left=20,Top=300,Width=300};game.Click+=delegate {using(var d=new PubgSettingsForm(pubg))d.ShowDialog(this);};Controls.Add(game);
   var save=new Button {Text="保存并应用",Left=475,Top=360,Width=145};save.Click+=delegate {var c=Read();if(c.Home.Length>0&&!Directory.Exists(c.Home)){status.Text="数据目录不存在。";return;}if(c.Executable.Length>0&&!File.Exists(c.Executable)){status.Text="程序路径不存在。";return;}if(!c.Save()){status.Text="保存失败，请检查文件夹写入权限。";return;}Config=c;DialogResult=DialogResult.OK;Close();};Controls.Add(save);
  }
  BindingConfig Read(){return new BindingConfig {Home=home.Text.Trim(),Executable=exe.Text.Trim(),CodexEnabled=enabled.Checked};}
 }
}
