using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Web.Script.Serialization;

namespace DesktopSpace {
 internal static class Program {
  [STAThread]static int Main(string[] args){
   if(args.Length>1&&args[0]=="--guard")return WatchDock(args[1]);
   if(args.Contains("--open-tray"))return SendDockCommand(Native.OpenTrayMessage)?0:1;
   if(args.Contains("--open-controls"))return SendDockCommand(Native.OpenControlsMessage)?0:1;
   if(args.Contains("--restore-taskbar")){if(!SendDockCommand(Native.RestoreTaskbarMessage)){TaskbarVisibility.Recover();Native.Taskbar(true);}return 0;}
   return RunShell(args);
  }
  static int WatchDock(string argument){
   int pid;
   if(!Int32.TryParse(argument,out pid))return 0;
   try{using(var process=Process.GetProcessById(pid))process.WaitForExit();}catch{ }
   TaskbarVisibility.Recover();
   Native.Taskbar(true);
   return 0;
  }
  static bool SendDockCommand(uint message){
   IntPtr dock=Native.FindWindow(null,Native.DockTitle);
   return dock!=IntPtr.Zero&&Native.PostMessage(dock,message,IntPtr.Zero,IntPtr.Zero);
  }
  // Keep WPF startup out of the command and crash-recovery process paths.
  [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
  static int RunShell(string[] args){
   bool full=!args.Contains("--launcher-only");
   if(args.Contains("--render")||args.Contains("--self-test")){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Configure(app);using(var host=new ShellHost(false)){
    try{string output=args.Length>1?args[1]:Path.Combine(Store.Root,"self-test.json");if(args[0]=="--render"){Directory.CreateDirectory(output);Ui.Export(host.Launcher.Surface,Path.Combine(output,"native-launcher.png"),776,632);var dock=new DockWindow(host);Ui.Export(dock.Surface,Path.Combine(output,"native-dock.png"),dock.Width,78);dock.Close();var settings=new SettingsWindow(host);Ui.Export((FrameworkElement)settings.Content,Path.Combine(output,"native-settings.png"),420,500);settings.Close();}
    else {var entries=host.Catalog.Desktop();var pinnedSet=new System.Collections.Generic.HashSet<string>(host.Settings.Pinned,StringComparer.OrdinalIgnoreCase);bool pinnedExcluded=!entries.Any(a=>pinnedSet.Contains(a.Source));bool icons=entries.All(a=>a.Icon!=null);bool sources=entries.All(a=>File.Exists(a.Source)||Directory.Exists(a.Source));var report=new{passed=pinnedExcluded&&icons&&sources,entryCount=entries.Count,pinnedExcluded=pinnedExcluded,allIconsPresent=icons,allSourcesExist=sources,openWindowCount=WindowService.Enumerate().Count};File.WriteAllText(output,new JavaScriptSerializer().Serialize(report),System.Text.Encoding.UTF8);return report.passed?0:2;}
    }catch(Exception e){Store.Error("validation",e);return 3;}
   }return 0;}
   if(!full){IntPtr existingFull=Native.FindWindow(null,Native.DockTitle);if(existingFull!=IntPtr.Zero){Native.PostMessage(existingFull,Native.ToggleMessage,IntPtr.Zero,IntPtr.Zero);return 0;}}
   bool created;using(var mutex=new Mutex(true,full?"Local\\ROG.DesktopSpace.Full.v1":"Local\\ROG.DesktopSpace.Launcher.v1",out created)){
    if(!created){
     if(!args.Contains("--startup")){
      IntPtr window=Native.FindWindow(null,full?Native.DockTitle:Native.LauncherHostTitle);
      if(window!=IntPtr.Zero)Native.PostMessage(window,Native.ToggleMessage,IntPtr.Zero,IntPtr.Zero);
     }
     return 0;
    }
    var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Configure(app);ShellHost host=null;
    app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e){Store.Error("ui",e.Exception);if(host!=null&&host.FullMode){host.Systems.StopKeepingTaskbarHidden();Native.Taskbar(true);}MessageBox.Show("桌面空间遇到问题，原任务栏已恢复。\n"+e.Exception.Message,"桌面空间");e.Handled=true;};
    try{host=new ShellHost(full);app.Dispatcher.BeginInvoke(new Action(delegate{host.Start(args.Contains("--show")||!full);}),System.Windows.Threading.DispatcherPriority.Normal);app.Run();return 0;}catch(Exception e){Store.Error("startup",e);if(full)Native.Taskbar(true);MessageBox.Show("桌面空间暂时无法启动：\n"+e.Message,"桌面空间");return 1;}finally{if(host!=null)host.Dispose();}
   }
  }
 }
}

