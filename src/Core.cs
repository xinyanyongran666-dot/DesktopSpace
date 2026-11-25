using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Media;

namespace DesktopSpace {
 public class ShellSettings {
  public int Version=1;
  public int Columns=6;
  public int Rows=3;
  public bool CloseAfterLaunch=true;
  public bool HideForMaximized=true;
  public string DockHideMode="smart";
  public bool HideSecondaryTaskbars=false;
  public bool MiddleClickCloses=false;
  public List<string> Pinned=new List<string>();
  public List<string> ExcludedNames=new List<string>();
  public Dictionary<string,string> Categories=new Dictionary<string,string>();
  public List<string> ItemOrder=new List<string>();
  public List<string> EnabledModules=new List<string>{"launcher","tasks","tray","ime","system"};
 }
 public static class Store {
  public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DesktopSpace");
  public static readonly string SettingsFile=Path.Combine(Root,"settings.json");
  public static readonly string Exe=Path.Combine(Root,"DesktopSpace.exe");
  public static ShellSettings Load(){using(var gate=new System.Threading.Mutex(false,"Local\\ROG.DesktopSpace.Settings.v1")){try{gate.WaitOne();}catch(System.Threading.AbandonedMutexException){ }try{return LoadUnsafe();}finally{gate.ReleaseMutex();}}}
  static ShellSettings LoadUnsafe() {
   Directory.CreateDirectory(Root);
   ShellSettings result=null;
   try {if(File.Exists(SettingsFile))result=new JavaScriptSerializer().Deserialize<ShellSettings>(File.ReadAllText(SettingsFile));}catch{ }
   if(result==null){result=new ShellSettings();
    string user=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), common=Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
    result.Pinned.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"explorer.exe"));
    foreach(string name in new[]{"Google Chrome.lnk","Microsoft Edge.lnk","Visual Studio Code.lnk","WeChat.lnk","微信.lnk","QQ.lnk","网易云音乐.lnk","Steam.lnk"}) {
     string p=Path.Combine(common,name);if(!File.Exists(p))p=Path.Combine(user,name);if(File.Exists(p))result.Pinned.Add(p);
    }
    SaveUnsafe(result);
   }
   result.Columns=Math.Max(3,Math.Min(8,result.Columns));result.Rows=Math.Max(2,Math.Min(5,result.Rows));
   if(String.IsNullOrEmpty(result.DockHideMode))result.DockHideMode=result.HideForMaximized?"smart":"never";
   if(result.Categories==null)result.Categories=new Dictionary<string,string>();
   if(result.ItemOrder==null)result.ItemOrder=new List<string>();
   return result;
  }
  public static void Save(ShellSettings settings){using(var gate=new System.Threading.Mutex(false,"Local\\ROG.DesktopSpace.Settings.v1")){try{gate.WaitOne();}catch(System.Threading.AbandonedMutexException){ }try{SaveUnsafe(settings);}finally{gate.ReleaseMutex();}}}
  static void SaveUnsafe(ShellSettings settings){Directory.CreateDirectory(Root);string temp=SettingsFile+".new";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(settings),System.Text.Encoding.UTF8);if(File.Exists(SettingsFile))File.Copy(SettingsFile,SettingsFile+".bak",true);File.Copy(temp,SettingsFile,true);File.Delete(temp);}
  public static void Error(string module,Exception error){try{File.WriteAllText(Path.Combine(Root,"last-error.txt"),DateTime.Now.ToString("s")+" "+module+"\r\n"+error.ToString());}catch{ }}
 }
 public class AppEntry {
  public string Id,Name,Source,Category,Target,AppUserModelId;
  public bool IsFolder;
  public ImageSource Icon;
 }
 public class TaskEntry {
  public IntPtr Handle;
  public uint Pid;
  public string Title,Executable;
  public ImageSource Icon;
 }
 public interface IShellModule:IDisposable {string Id{get;}void Start();}
 public static class LauncherService {
  public static void Launch(AppEntry item){if(!File.Exists(item.Source)&&!Directory.Exists(item.Source))throw new FileNotFoundException("原项目已经移动或删除。",item.Source);Process.Start(new ProcessStartInfo(item.Source){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(item.Source)});}
  public static void LaunchAsAdmin(AppEntry item){if(!File.Exists(item.Source))throw new FileNotFoundException("原项目已经移动或删除。",item.Source);Process.Start(new ProcessStartInfo(item.Source){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(item.Source)});}
  public static void OpenLocation(AppEntry item){string target=File.Exists(item.Source)||Directory.Exists(item.Source)?item.Source:item.Target;if(!String.IsNullOrEmpty(target)&&(File.Exists(target)||Directory.Exists(target)))Process.Start("explorer.exe","/select,\""+target+"\"");}
  public static void OpenUri(string uri){Process.Start(new ProcessStartInfo(uri){UseShellExecute=true});}
 }
}
