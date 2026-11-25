using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopSpace {
 public sealed class ShellHost:IDisposable {
  public readonly ShellSettings Settings;public readonly IconService Icons;public readonly CatalogService Catalog;public readonly SystemAccess Systems;
  LauncherWindow launcher;
  PreviewWindow preview;
  ToastWindow toast;
  readonly DispatcherTimer launcherIdleTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(12)};
  public LauncherWindow Launcher{get{return launcher??(launcher=CreateLauncher());}}
  public bool IsLauncherVisible{get{return launcher!=null&&launcher.IsVisible;}}
  internal bool IsPreviewHovered{get{return preview!=null&&preview.IsMouseOver;}}
  internal bool IsPreviewVisible{get{return preview!=null&&preview.IsVisible;}}
  public DockWindow Dock;public readonly bool FullMode;public bool IsShuttingDown;public readonly System.Windows.Media.ImageSource TrayIcon;
  readonly List<IShellModule> modules=new List<IShellModule>();readonly DispatcherTimer refreshTimer=new DispatcherTimer();Window commandHost;IntPtr previousForeground;
  public ShellHost(bool full){FullMode=full;Settings=Store.Load();Icons=new IconService();Catalog=new CatalogService(Settings,Icons);string iconPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tray-expand.png");TrayIcon=File.Exists(iconPath)?Icons.Get(iconPath):Ui.SymbolImage("⌃");Systems=new SystemAccess(this);refreshTimer.Interval=TimeSpan.FromMilliseconds(350);refreshTimer.Tick+=delegate{refreshTimer.Stop();RefreshCatalog();};
   var dispatcher=refreshTimer.Dispatcher;
   Catalog.Changed+=delegate{dispatcher.BeginInvoke(new Action(ScheduleCatalogRefresh));};
   launcherIdleTimer.Tick+=delegate{
    launcherIdleTimer.Stop();
    if(launcher!=null&&!launcher.IsVisible){
     var idleLauncher=launcher;
     launcher=null;
     idleLauncher.Release();
    }
   };
  }
  LauncherWindow CreateLauncher(){
   var window=new LauncherWindow(this);
   window.IsVisibleChanged+=delegate{
    if(launcher!=window||IsShuttingDown)return;
    if(window.IsVisible)launcherIdleTimer.Stop();
    else launcherIdleTimer.Start();
   };
   return window;
  }
  public void Start(bool reveal){
   if(FullMode){Dock=new DockWindow(this);Dock.Show();IntPtr launcherHost=Native.FindWindow(null,Native.LauncherHostTitle);if(launcherHost!=IntPtr.Zero)Native.PostMessage(launcherHost,Native.ExitLauncherMessage,IntPtr.Zero,IntPtr.Zero);Native.Taskbar(false,Settings.HideSecondaryTaskbars);
    var tasks=new TaskModule();tasks.Changed+=delegate(List<TaskEntry> list){Dock.UpdateTasks(list);};tasks.ForegroundChanged+=delegate{Dock.UpdateActiveHighlight();};modules.Add(tasks);var ime=new ImeModule();ime.Changed+=delegate(string mode,bool valid){Dock.SetIme(mode,valid);};modules.Add(ime);modules.Add(Systems);foreach(var module in modules)if(Settings.EnabledModules.Contains(module.Id))module.Start();
    try{Process.Start(new ProcessStartInfo(Store.Exe,"--guard "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}catch(Exception e){Store.Error("taskbar guard",e);}
   }else{commandHost=new Window{Title=Native.LauncherHostTitle,Width=1,Height=1,ShowInTaskbar=false,WindowStyle=WindowStyle.ToolWindow};IntPtr handle=new WindowInteropHelper(commandHost).EnsureHandle();HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr hwnd,int message,IntPtr wp,IntPtr lp,ref bool handled){if((uint)message==Native.ToggleMessage){ToggleLauncher();handled=true;}if((uint)message==Native.ExitLauncherMessage){Exit();handled=true;}return IntPtr.Zero;});}
   if(reveal)ToggleLauncher();
  }
  public void ToggleLauncher(){Systems.HideQuickSettings();var window=Launcher;if(window.IsVisible)window.Dismiss();else if(!window.JustDeactivated){previousForeground=Native.GetForegroundWindow();window.Reveal();}}
  public void ReturnFocus(){if(previousForeground!=IntPtr.Zero&&Native.IsWindow(previousForeground))Native.Activate(previousForeground);}
  internal void ShowPreview(List<TaskEntry> items,FrameworkElement anchor){
   if(preview==null)preview=new PreviewWindow();
   preview.ShowFor(items,anchor);
  }
  public void HidePreviews(){if(preview!=null)preview.HidePreview();}
  public void HideLauncher(){if(IsLauncherVisible)Launcher.Hide();}
  void ScheduleCatalogRefresh(){if(IsShuttingDown)return;if(launcher!=null)launcher.InvalidateCatalog();refreshTimer.Stop();refreshTimer.Start();}
  internal void RefreshCatalog(){Icons.Clear();RefreshLauncher();if(Dock!=null)Dock.ForceRebuild();}
  void RefreshLauncher(){if(IsLauncherVisible)launcher.Refresh();else if(launcher!=null)launcher.InvalidateCatalog();}
  public void Pin(AppEntry item){if(!Settings.Pinned.Contains(item.Source)){Settings.Pinned.Add(item.Source);SavePins();}}
  public void Unpin(string source){if(Settings.Pinned.Remove(source))SavePins();}
  public void SavePins(){Store.Save(Settings);RefreshLauncher();if(Dock!=null)Dock.ForceRebuild();}
  public void Notify(string text){
   if(Application.Current==null||Application.Current.Dispatcher==null)return;
   if(toast==null)toast=new ToastWindow();
   toast.ShowText(text,Dock);
  }
  public void ShowSettings(){Systems.HideQuickSettings();HideLauncher();HidePreviews();var dialog=new SettingsWindow(this);dialog.Show();dialog.Activate();}
  public bool IsStartupEnabled(){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",false)){return key!=null&&key.GetValue("DesktopSpace")!=null;}}
  public void EnableStartup(){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){key.SetValue("DesktopSpace","\""+Store.Exe+"\" --startup");}}
  public void DisableStartup(){using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run",true)){if(key!=null)key.DeleteValue("DesktopSpace",false);}}
  public void Exit(){if(IsShuttingDown)return;Dispose();Application.Current.Shutdown();}
  public void Dispose(){if(IsShuttingDown)return;IsShuttingDown=true;refreshTimer.Stop();launcherIdleTimer.Stop();foreach(var module in modules){try{module.Dispose();}catch{ }}Catalog.Dispose();try{if(launcher!=null)launcher.Release();if(preview!=null)preview.Close();if(toast!=null)toast.Close();if(Dock!=null)Dock.Close();if(commandHost!=null)commandHost.Close();}catch{ }if(FullMode)Native.Taskbar(true);}
 }
 public sealed class SettingsWindow:Window {
  public SettingsWindow(ShellHost host){Ui.PopupWindow(this,"桌面空间设置");Width=420;Height=580;Left=(SystemParameters.PrimaryScreenWidth-Width)/2;Top=(SystemParameters.PrimaryScreenHeight-Height)/2;
   Resources[typeof(Button)]=Ui.ButtonStyle(Ui.LightHover,Ui.LightPressed);
   var body=new StackPanel();var head=new DockPanel();var close=Ui.Button(Ui.Symbol("close",16,Ui.LightInk),Close);DockPanel.SetDock(close,Dock.Right);head.Children.Add(close);var title=Ui.Label("桌面空间设置",20,Ui.LightInk);title.FontWeight=FontWeights.SemiBold;head.Children.Add(title);body.Children.Add(head);
   var label=Ui.Label("每行图标数",14,Ui.LightInk);label.Margin=new Thickness(0,20,0,8);body.Children.Add(label);var columns=new Slider{Minimum=3,Maximum=8,TickFrequency=1,IsSnapToTickEnabled=true,Value=host.Settings.Columns,Margin=new Thickness(0,0,0,12)};body.Children.Add(columns);
   var hideLabel=Ui.Label("Dock 自动隐藏模式",14,Ui.LightInk);hideLabel.Margin=new Thickness(0,2,0,6);body.Children.Add(hideLabel);
   var hideMode=new ComboBox{FontSize=13,Margin=new Thickness(0,0,0,12),Padding=new Thickness(10,6,10,6)};
   hideMode.Items.Add(new ComboBoxItem{Content="智能躲避最大化/分屏窗口",Tag="smart"});
   hideMode.Items.Add(new ComboBoxItem{Content="始终自动隐藏（鼠标移至底边显示）",Tag="always"});
   hideMode.Items.Add(new ComboBoxItem{Content="始终显示 Dock（仅全屏游戏/视频隐藏）",Tag="never"});
   hideMode.SelectedIndex=host.Settings.DockHideMode=="always"?1:(host.Settings.DockHideMode=="never"?2:0);
   body.Children.Add(hideMode);
   var closeAfter=new CheckBox{Content="打开应用后收起抽屉",IsChecked=host.Settings.CloseAfterLaunch,Foreground=Ui.LightInk,FontSize=14,Margin=new Thickness(0,4,0,10)};body.Children.Add(closeAfter);
   var hideSec=new CheckBox{Content="多显示器时隐藏副屏任务栏",IsChecked=host.Settings.HideSecondaryTaskbars,Foreground=Ui.LightInk,FontSize=14,Margin=new Thickness(0,0,0,10)};body.Children.Add(hideSec);
   var middleClose=new CheckBox{Content="中键点击运行中的应用时关闭窗口（默认：新开窗口）",IsChecked=host.Settings.MiddleClickCloses,Foreground=Ui.LightInk,FontSize=14,Margin=new Thickness(0,0,0,14)};body.Children.Add(middleClose);
   Func<string,Action,Button> action=delegate(string text,Action run){var b=Ui.Button(text,run);b.Foreground=Ui.LightInk;b.Background=Ui.LightTile;b.Margin=new Thickness(0,0,0,8);return b;};
   var save=action("保存布局与设置",delegate{
    host.Settings.Columns=(int)columns.Value;
    var selected=hideMode.SelectedItem as ComboBoxItem;
    host.Settings.DockHideMode=selected!=null?(string)selected.Tag:"smart";
    host.Settings.HideForMaximized=host.Settings.DockHideMode!="never";
    host.Settings.HideSecondaryTaskbars=hideSec.IsChecked==true;
    host.Settings.CloseAfterLaunch=closeAfter.IsChecked==true;
    host.Settings.MiddleClickCloses=middleClose.IsChecked==true;
    Store.Save(host.Settings);
    if(host.FullMode){if(!host.Settings.HideSecondaryTaskbars)Native.SecondaryTaskbars(true);else Native.Taskbar(false,true);}
    host.Notify("设置已保存");
    Close();
   });
   save.Background=Ui.LightAccent;save.Foreground=System.Windows.Media.Brushes.White;save.Margin=new Thickness(0,0,0,12);body.Children.Add(save);
   body.Children.Add(action("刷新桌面项目与图标",delegate{host.RefreshCatalog();host.Notify("已刷新桌面项目与图标");}));
   Button startupBtn=null;
   Action refreshStartupText=delegate{startupBtn.Content=host.IsStartupEnabled()?"已开启开机自启（点击取消）":"设为登录后自动启动";};
   startupBtn=action("",delegate{
    if(host.IsStartupEnabled()){host.DisableStartup();host.Notify("已取消登录后自动启动");}
    else{host.EnableStartup();host.Notify("已设为登录后自动启动桌面空间");}
    refreshStartupText();
   });
   refreshStartupText();
   body.Children.Add(startupBtn);
   body.Children.Add(action("退出桌面空间，恢复 Windows 任务栏",host.Exit));
   Content=Ui.LightPanel(body,new Thickness(25));KeyDown+=delegate(object s,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape)Close();};
  }
 }
}

