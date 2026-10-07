using System;
using System.Drawing;
using System.Windows.Forms;
namespace SideScreenMonitor {
 public sealed class WeatherLocationForm:Form {
  readonly TextBox query=new TextBox();readonly ComboBox results=new ComboBox();readonly Label status=new Label();
  public WeatherClient.Location Selected;
  public WeatherLocationForm(WeatherClient.Location current){
   Selected=current;Text="选择天气地区";Width=560;Height=220;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterScreen;
   var label=new Label{Text="城市或区县",Left=18,Top=18,Width=100,AutoSize=true};Controls.Add(label);
   query.SetBounds(120,14,280,28);query.Text=current.Name;Controls.Add(query);
   var search=new Button{Text="搜索",Left=412,Top=12,Width=100,Height=30};search.Click+=Search;Controls.Add(search);
   results.SetBounds(120,52,392,30);results.DropDownStyle=ComboBoxStyle.DropDownList;Controls.Add(results);
   status.SetBounds(120,88,390,30);status.AutoSize=false;Controls.Add(status);
   var save=new Button{Text="保存地区",Left=320,Top=130,Width=90,Height=32};save.Click+=delegate{if(results.SelectedItem==null){status.Text="请先搜索并选择地区";return;}Selected=results.SelectedItem as WeatherClient.Location;DialogResult=DialogResult.OK;Close();};Controls.Add(save);
   var cancel=new Button{Text="取消",Left=420,Top=130,Width=90,Height=32};cancel.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};Controls.Add(cancel);
   AcceptButton=search;CancelButton=cancel;
  }
  void Search(object sender,EventArgs e){
   if(query.Text.Trim().Length<2){status.Text="请输入至少两个字";return;}
   try{results.Items.Clear();foreach(var location in WeatherClient.Search(query.Text.Trim()))results.Items.Add(location);if(results.Items.Count>0){results.SelectedIndex=0;status.Text="请选择正确的城市或区县";}else status.Text="没有找到地区，请换个名称试试";}
   catch(Exception ex){status.Text="搜索失败："+ex.Message;}
  }
 }
}
