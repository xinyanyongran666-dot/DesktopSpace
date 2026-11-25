using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace DesktopSpace {
 public sealed class DockWindow:Window {
  static readonly Brush Ink=Ui.Brush("#27384C");
  static readonly Brush DividerBrush=Ui.Brush("#34486383");
  static readonly Brush ActiveBrush=Ui.Brush("#31517DA8");
  static readonly Brush RunningDot=Ui.Brush("#41688D");
  static readonly Brush ActivePillBrush=Ui.Brush("#2B5B8C");
  static readonly Brush GlassFill=GlassGradient(Color.FromArgb(222,248,252,255),Color.FromArgb(190,213,229,248));
  static readonly Brush GlassEdge=GlassGradient(Color.FromArgb(244,255,255,255),Color.FromArgb(120,158,185,216));
  static readonly Brush ShadowEdge=Ui.Brush("#29324861");
  readonly ShellHost host;readonly StackPanel row=new StackPanel{Orientation=Orientation.Horizontal};readonly TextBlock ime=Ui.Label("",15,Ink),clock=Ui.Label("",12,Ink);readonly Dictionary<Button,List<TaskEntry>> taskButtons=new Dictionary<Button,List<TaskEntry>>();
  readonly Dictionary<Button,Border> indicatorDots=new Dictionary<Button,Border>();
  readonly StackPanel pinnedRow=new StackPanel{Orientation=Orientation.Horizontal};
  readonly StackPanel runningRow=new StackPanel{Orientation=Orientation.Horizontal};
  string signature="";List<TaskEntry> tasks=new List<TaskEntry>();readonly System.Windows.Threading.DispatcherTimer clockTimer=new System.Windows.Threading.DispatcherTimer();readonly System.Windows.Threading.DispatcherTimer visibilityTimer=new System.Windows.Threading.DispatcherTimer();readonly System.Windows.Threading.DispatcherTimer leaveTimer=new System.Windows.Threading.DispatcherTimer();readonly System.Windows.Threading.DispatcherTimer hoverTimer=new System.Windows.Threading.DispatcherTimer();DateTime lastWake=DateTime.MinValue;bool dockHidden;
  List<TaskEntry> pendingPreviewTasks;
  Button pendingPreviewAnchor;
  Button volumeButton;
  FrameworkElement volumeIcon,muteIcon;
  bool volumeMuted;
  int volumePollTicks;
  CalendarWindow calendar;
  TextBlock volumeBadge;
  readonly System.Windows.Threading.DispatcherTimer volumeBadgeTimer=new System.Windows.Threading.DispatcherTimer();
  readonly System.Windows.Threading.DispatcherTimer holdTimer=new System.Windows.Threading.DispatcherTimer();
  DateTime holdStarted;
  Button holdingButton;
  System.Windows.Shapes.Path holdingRing;
  ScaleTransform holdingScale;
  TranslateTransform holdingTranslate;
  string holdingSource;
  Point holdMouseStart;
  Point dragLastInRow;
  bool pinReorderReady,pinReorderMoved;
  public FrameworkElement Surface{get;private set;}
  Button trayButton;
  const long NoActivateStyle=0x08000000, ClickThroughStyle=0x20;
  IntPtr dockHandle;
  DockWinKey winKey;
  DateTime keyboardWakeUntil;
  bool keyboardAwake;
  bool ignoreHoverUntilLeave;
  IntPtr clickForeground,fullscreenWakeOrigin;
  internal FrameworkElement TrayAnchor{get{return trayButton;}}
  public DockWindow(ShellHost h){host=h;Ui.PopupWindow(this,Native.DockTitle);ShowActivated=false;Height=78;Width=850;
   Resources[typeof(Button)]=Ui.ButtonStyle(Ui.Brush("#246087B3"),Ui.Brush("#395478A1"),fadeHover:true);
   var shadow=new Border{
    BorderBrush=ShadowEdge,BorderThickness=new Thickness(1,0,1,2),
    CornerRadius=new CornerRadius(22),Margin=new Thickness(5,7,5,3),IsHitTestVisible=false
   };
   var border=Ui.Panel(row,new Thickness(12,6,12,6));
   border.Background=GlassFill;border.BorderBrush=GlassEdge;border.CornerRadius=new CornerRadius(21);border.Margin=new Thickness(6);
   Surface=new Grid{Children={shadow,border}};
   Content=Surface;
   SourceInitialized+=delegate{
    dockHandle=new WindowInteropHelper(this).Handle;
    long style=Native.GetWindowLongPtr(dockHandle,-20).ToInt64();
    Native.SetWindowLongPtr(dockHandle,-20,new IntPtr(style|0x80|NoActivateStyle));
    var source=HwndSource.FromHwnd(dockHandle);
    if(source.CompositionTarget!=null)Ui.Scale=source.CompositionTarget.TransformToDevice.M11;
    source.AddHook(Hook);
    if(host.FullMode){winKey=new DockWinKey(dockHandle);Native.RegisterHotKey(dockHandle,1,0x0001|0x4000,0x20);}
   };
   Microsoft.Win32.SystemEvents.DisplaySettingsChanged+=OnDisplayChanged;
   Closing+=delegate{Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=OnDisplayChanged;if(dockHandle!=IntPtr.Zero)Native.UnregisterHotKey(dockHandle,1);if(winKey!=null)winKey.Dispose();clockTimer.Stop();visibilityTimer.Stop();leaveTimer.Stop();hoverTimer.Stop();holdTimer.Stop();volumeBadgeTimer.Stop();};clockTimer.Interval=TimeSpan.FromMilliseconds(1000);clockTimer.Tick+=delegate{UpdateClock();if(++volumePollTicks>=2){volumePollTicks=0;UpdateVolumeGlyph();}};clockTimer.Start();UpdateClock();visibilityTimer.Interval=TimeSpan.FromMilliseconds(90);visibilityTimer.Tick+=delegate{UpdateVisibility();};visibilityTimer.Start();
   holdTimer.Interval=TimeSpan.FromMilliseconds(16);holdTimer.Tick+=delegate{UpdatePinHold();};
   volumeBadgeTimer.Interval=TimeSpan.FromMilliseconds(1200);volumeBadgeTimer.Tick+=delegate{volumeBadgeTimer.Stop();if(volumeBadge!=null)volumeBadge.Visibility=Visibility.Collapsed;ApplyVolumeGlyph();};
   hoverTimer.Interval=TimeSpan.FromMilliseconds(220);hoverTimer.Tick+=delegate{hoverTimer.Stop();if(pendingPreviewTasks!=null&&pendingPreviewAnchor!=null&&pendingPreviewAnchor.IsMouseOver&&!host.IsLauncherVisible)host.ShowPreview(pendingPreviewTasks,pendingPreviewAnchor);};
   leaveTimer.Interval=TimeSpan.FromMilliseconds(200);leaveTimer.Tick+=delegate{if(!IsMouseOver&&!host.IsPreviewHovered){hoverTimer.Stop();host.HidePreviews();}leaveTimer.Stop();};MouseLeave+=delegate{hoverTimer.Stop();leaveTimer.Start();};
   AllowDrop=true;
   DragOver+=delegate(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Link:DragDropEffects.None;e.Handled=true;};
   Drop+=delegate(object sender,DragEventArgs e){HandleFileDrop(e,-1);};
   var menu=new ContextMenu();Add(menu,"全部应用",h.ToggleLauncher);Add(menu,"布局设置",h.ShowSettings);menu.Items.Add(new Separator());Add(menu,"退出并恢复任务栏",h.Exit);ContextMenu=menu;
   BuildFixedControls();
   Rebuild(tasks);
  }
  void UpdateClock(){
   var now=DateTime.Now;
   clock.Text=now.ToString("HH:mm");
   clock.ToolTip=now.ToString("yyyy年M月d日 dddd")+"（点击查看日历）";
  }
  void UpdateVolumeGlyph(){
   try{volumeMuted=AudioVolume.GetMute()||AudioVolume.GetPercent()==0;}catch{ }
   ApplyVolumeGlyph();
  }
  void ApplyVolumeGlyph(){
   if(volumeIcon==null||muteIcon==null)return;
   bool badge=volumeBadge!=null&&volumeBadge.Visibility==Visibility.Visible;
   volumeIcon.Visibility=!badge&&!volumeMuted?Visibility.Visible:Visibility.Collapsed;
   muteIcon.Visibility=!badge&&volumeMuted?Visibility.Visible:Visibility.Collapsed;
  }
  void ShowVolumeBadge(string text){
   if(volumeBadge==null)return;
   volumeBadge.Text=text;
   volumeBadge.Visibility=Visibility.Visible;
   ApplyVolumeGlyph();
   volumeBadgeTimer.Stop();
   volumeBadgeTimer.Start();
  }
  void ToggleCalendar(){
   host.HideLauncher();host.HidePreviews();host.Systems.HideQuickSettings();
   if(calendar==null)calendar=new CalendarWindow();
   calendar.Toggle(clock,Top);
  }
  void OnDisplayChanged(object sender,EventArgs e){
   Dispatcher.BeginInvoke(new Action(delegate{
    if(dockHandle!=IntPtr.Zero){
     var source=HwndSource.FromHwnd(dockHandle);
     if(source!=null&&source.CompositionTarget!=null)Ui.Scale=source.CompositionTarget.TransformToDevice.M11;
    }
    Rebuild(tasks);
   }));
  }
  static Brush GlassGradient(Color top,Color bottom){
   var brush=new LinearGradientBrush(top,bottom,new Point(0,0),new Point(0,1));
   brush.Freeze();
   return brush;
  }
  IntPtr Hook(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled){
   if((uint)message==Native.ToggleMessage){host.ToggleLauncher();handled=true;}
   if((uint)message==Native.RestoreTaskbarMessage){Dispatcher.BeginInvoke(new Action(host.Exit));handled=true;}
   if((uint)message==Native.OpenTrayMessage){host.Systems.ToggleTray();handled=true;}
   if((uint)message==Native.OpenControlsMessage){host.Systems.OpenQuickSettings(null);handled=true;}
   if((uint)message==DockWinKey.WakeMessage){WakeFromWin(wparam);handled=true;}
   if((uint)message==DockWinKey.OutsideClickMessage){EndKeyboardWake();handled=true;}
   if((uint)message==DockWinKey.DockWheelMessage){OnDockWheel(wparam.ToInt32());handled=true;}
   if(message==0x0312&&wparam.ToInt32()==1){host.ToggleLauncher();handled=true;}
   if(message==0x21){
    if(keyboardAwake&&(lparam.ToInt64()>>16)==0x201)clickForeground=Native.GetForegroundWindow();
    handled=true;
    return new IntPtr(keyboardAwake?1:3);
   }
   return IntPtr.Zero;
  }
  void WakeFromWin(IntPtr foreground){
   if(host.IsShuttingDown)return;
   clickForeground=IntPtr.Zero;
   ignoreHoverUntilLeave=false;
   fullscreenWakeOrigin=Native.IsFullScreen(foreground)?foreground:IntPtr.Zero;
   keyboardWakeUntil=DateTime.UtcNow.AddSeconds(6);
   Native.SetWindowPos(dockHandle,new IntPtr(-1),0,0,0,0,0x13);
   if(foreground==Native.GetForegroundWindow()&&!Native.CanClickDock(dockHandle,foreground))
    Native.ShowWindow(foreground,6);
   UpdateVisibility();
  }
  void EndKeyboardWake(){
   if(!keyboardAwake)return;
   keyboardWakeUntil=lastWake=DateTime.MinValue;
   ignoreHoverUntilLeave=true;
   clickForeground=fullscreenWakeOrigin=IntPtr.Zero;
   UpdateVisibility();
  }
  void UpdateVisibility(){if(!host.FullMode)return;bool hidden=false;IntPtr foreground=Native.GetForegroundWindow();Native.POINT pointer;Native.GetCursorPos(out pointer);double x=pointer.X/Ui.Scale,y=pointer.Y/Ui.Scale;bool edge=y>=SystemParameters.PrimaryScreenHeight-3&&x>=Left-15&&x<=Left+ActualWidth+15;var now=DateTime.UtcNow;bool interacting=IsMouseOver||host.IsPreviewHovered;
   if(!interacting&&!edge)ignoreHoverUntilLeave=false;
   if(!ignoreHoverUntilLeave&&(edge||interacting||host.IsLauncherVisible))lastWake=now;
   if(keyboardAwake&&interacting&&!ignoreHoverUntilLeave)keyboardWakeUntil=now.AddSeconds(6);
   bool awake=now<keyboardWakeUntil;
   if(awake!=keyboardAwake){
    keyboardAwake=awake;
    long style=Native.GetWindowLongPtr(dockHandle,-20).ToInt64();
    Native.SetWindowLongPtr(dockHandle,-20,new IntPtr(awake?style&~NoActivateStyle:style|NoActivateStyle));
    if(winKey!=null)winKey.WatchOutsideClicks(awake);
   }
   if(!awake&&!host.IsLauncherVisible){
    string mode=host.Settings.DockHideMode??(host.Settings.HideForMaximized?"smart":"never");
    bool stale=(now-lastWake).TotalMilliseconds>850;
    if(Native.IsFullScreen(foreground))hidden=true;
    else if(mode=="always")hidden=stale&&!edge&&!interacting;
    else if(mode=="smart")hidden=stale&&(Native.IsZoomed(foreground)||Native.CoversDockInSplitView(foreground,dockHandle,(int)Math.Ceiling(12*Ui.Scale)));
   }
   if(hidden!=dockHidden){
    dockHidden=hidden;
    IsHitTestVisible=!hidden;
    long style=Native.GetWindowLongPtr(dockHandle,-20).ToInt64();
    Native.SetWindowLongPtr(dockHandle,-20,new IntPtr(hidden?style|ClickThroughStyle:style&~ClickThroughStyle));
    BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(hidden?0:1,TimeSpan.FromMilliseconds(110)));
    if(hidden)host.HidePreviews();
   }
  }
  static void Add(ContextMenu menu,string name,Action action){var item=new MenuItem{Header=name};item.Click+=delegate{action();};menu.Items.Add(item);}
  FrameworkElement Divider(){return new Border{Width=1,Height=28,Background=DividerBrush,Margin=new Thickness(7,0,7,0)};}
  bool Matches(AppEntry app,TaskEntry task){
   if(!String.IsNullOrEmpty(app.AppUserModelId))return String.Equals(app.AppUserModelId,Native.ProcessAppId(task.Pid),StringComparison.OrdinalIgnoreCase);
   string target=app.Target;if(String.IsNullOrEmpty(target))target=app.Source;return !String.IsNullOrEmpty(task.Executable)&&String.Equals(target,task.Executable,StringComparison.OrdinalIgnoreCase);
  }
  public void UpdateTasks(List<TaskEntry> next){tasks=next;if(holdingButton!=null)return;string nextSignature=String.Join("|",host.Settings.Pinned.Concat(next.Select(t=>t.Handle.ToInt64().ToString()+":"+t.Executable).OrderBy(s=>s,StringComparer.Ordinal)));if(signature!=nextSignature){signature=nextSignature;Rebuild(next);}else{UpdateActiveHighlight();}}
  public void UpdateActiveHighlight(){
   IntPtr foreground=Native.GetForegroundWindow();
   foreach(var pair in taskButtons){
    bool active=pair.Value.Any(t=>t.Handle==foreground);
    pair.Key.Background=active?ActiveBrush:Brushes.Transparent;
    Border dot;
    if(indicatorDots.TryGetValue(pair.Key,out dot)){
     double targetWidth=active?14:(pair.Value.Count>1?8:4);
     dot.Background=active?ActivePillBrush:RunningDot;
     if(Math.Abs(dot.Width-targetWidth)>0.5)dot.Width=targetWidth;
    }
   }
  }
  public void ForceRebuild(){signature="";UpdateTasks(tasks);}
  void BuildFixedControls(){
   var start=Ui.Button(Ui.Symbol("windows",27,Ink),host.Systems.ToggleStartMenu);start.Width=42;start.Height=48;start.ToolTip="Windows 开始菜单（右键系统快捷菜单）";System.Windows.Automation.AutomationProperties.SetName(start,"Windows 开始菜单");
   var startMenu=new ContextMenu();
   Add(startMenu,"任务管理器",delegate{RunSystemTool("taskmgr.exe","");});
   Add(startMenu,"Windows 终端",delegate{try{RunSystemTool("wt.exe","");}catch{RunSystemTool("powershell.exe","");}});
   Add(startMenu,"系统设置",delegate{LauncherService.OpenUri("ms-settings:");});
   startMenu.Items.Add(new Separator());
   Add(startMenu,"锁定屏幕",delegate{Native.LockWorkStation();});
   Add(startMenu,"睡眠",delegate{Native.SetSuspendState(false,false,false);});
   Add(startMenu,"重启",delegate{RunSystemTool("shutdown.exe","/r /t 0");});
   Add(startMenu,"关机",delegate{RunSystemTool("shutdown.exe","/s /t 0");});
   start.ContextMenu=startMenu;
   row.Children.Add(start);
   row.Children.Add(pinnedRow);
   row.Children.Add(Divider());var launcher=Ui.Button(Ui.Symbol("grid",27,Ink),host.ToggleLauncher);launcher.Width=48;launcher.Height=48;launcher.ToolTip="全部应用";row.Children.Add(launcher);
   row.Children.Add(runningRow);
   var statusDivider=Divider();statusDivider.Margin=new Thickness(11,0,10,0);row.Children.Add(statusDivider);
   var trayGlyph=new Rectangle{Width=27,Height=27,Fill=Ink,OpacityMask=new ImageBrush(host.TrayIcon)};
   trayButton=AddStatusButton(trayGlyph,host.Systems.ToggleTray,"后台应用 / 系统托盘");
   trayButton.Padding=new Thickness(4);
   AddQuickSettingsButton("wifi","网络与连接");
   volumeBadge=Ui.Label("",11,ActivePillBrush);
   volumeBadge.FontWeight=FontWeights.Bold;
   volumeBadge.HorizontalAlignment=HorizontalAlignment.Center;
   volumeBadge.VerticalAlignment=VerticalAlignment.Center;
   volumeBadge.Visibility=Visibility.Collapsed;
   volumeIcon=Ui.Symbol("volume",18,Ink);
   muteIcon=Ui.Symbol("mute",18,Ink);
   muteIcon.Visibility=Visibility.Collapsed;
   var volCell=new Grid();
   volCell.Children.Add(volumeIcon);
   volCell.Children.Add(muteIcon);
   volCell.Children.Add(volumeBadge);
   volumeButton=AddStatusButton(volCell,delegate{host.Systems.OpenQuickSettings(volumeButton,"volume");},"声音与音量（滚轮调音量 · 中键静音）");
   volumeButton.Padding=new Thickness(4);
   volumeButton.PreviewMouseWheel+=delegate(object sender,System.Windows.Input.MouseWheelEventArgs e){
    AdjustVolumeByDelta(e.Delta);
    e.Handled=true;
   };
   volumeButton.PreviewMouseDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){
    if(e.ChangedButton!=System.Windows.Input.MouseButton.Middle)return;
    try{
     bool muted=AudioVolume.ToggleMute();
     volumeMuted=muted||AudioVolume.GetPercent()==0;
     ShowVolumeBadge(muted?"静音":AudioVolume.GetPercent()+"%");
     host.Systems.SyncQuickVolume();
    }catch{ }
    e.Handled=true;
   };
   UpdateVolumeGlyph();
   ime.MinWidth=24;ime.TextAlignment=TextAlignment.Center;ime.ToolTip="当前输入框的实际中英状态（点击切换）";ime.Margin=new Thickness(8,0,9,0);ime.Cursor=System.Windows.Input.Cursors.Hand;
   ime.MouseLeftButtonUp+=delegate{Native.TryTapShift();};
   row.Children.Add(ime);
   clock.MinWidth=43;clock.TextAlignment=TextAlignment.Center;clock.Margin=new Thickness(9,0,9,0);clock.Cursor=System.Windows.Input.Cursors.Hand;clock.Background=Brushes.Transparent;
   clock.MouseLeftButtonUp+=delegate{ToggleCalendar();};
   row.Children.Add(clock);
   AddStatusButton(Ui.Symbol("settings",17,Ink),host.ShowSettings,"桌面空间设置");
  }
  void Rebuild(List<TaskEntry> current){
   pinnedRow.Children.Clear();runningRow.Children.Clear();taskButtons.Clear();indicatorDots.Clear();
   var represented=new HashSet<long>();foreach(string path in host.Settings.Pinned){if(!File.Exists(path)&&!Directory.Exists(path))continue;AppEntry app;try{app=host.Catalog.Entry(path);}catch{continue;}if(System.IO.Path.GetFileName(path).Equals("explorer.exe",StringComparison.OrdinalIgnoreCase))app.Name="文件";var matches=current.Where(t=>Matches(app,t)).ToList();foreach(var task in matches)represented.Add(task.Handle.ToInt64());pinnedRow.Children.Add(TaskButton(app,matches,true));}
   var extras=current.Where(t=>!represented.Contains(t.Handle.ToInt64())).GroupBy(t=>!String.IsNullOrEmpty(t.Executable)&&!t.Executable.EndsWith("ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase)?t.Executable:t.Handle.ToInt64().ToString(),StringComparer.OrdinalIgnoreCase);
   foreach(var group in extras){var first=group.First();var app=new AppEntry{Id=first.Executable,Name=first.Title,Source=first.Executable,Target=first.Executable,Icon=host.Icons.TaskIcon(first)};runningRow.Children.Add(TaskButton(app,group.ToList(),false));}
   UpdateActiveHighlight();
   row.Measure(new Size(Double.PositiveInfinity,70));double target=Math.Min(SystemParameters.PrimaryScreenWidth-20,row.DesiredSize.Width+38);double left=(SystemParameters.PrimaryScreenWidth-target)/2;Top=SystemParameters.PrimaryScreenHeight-Height-4;
   if(IsVisible&&Math.Abs(Width-target)>1){BeginAnimation(FrameworkElement.WidthProperty,new DoubleAnimation(Width,target,TimeSpan.FromMilliseconds(170)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});BeginAnimation(Window.LeftProperty,new DoubleAnimation(Left,left,TimeSpan.FromMilliseconds(170)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});}else{Width=target;Left=left;}
  }
  Button AddStatusButton(FrameworkElement icon,Action action,string description){
   var button=Ui.Button(icon,action);
   button.Width=38;button.Height=42;button.Margin=new Thickness(3,0,3,0);button.ToolTip=description;
   row.Children.Add(button);
   return button;
  }
  Button AddQuickSettingsButton(string icon,string description){
   Button anchor=null;
   anchor=AddStatusButton(Ui.Symbol(icon,18,Ink),delegate{host.Systems.OpenQuickSettings(anchor,icon);},description);
   return anchor;
  }
  Button TaskButton(AppEntry app,List<TaskEntry> windows,bool pinned){var cell=new Grid{Width=45,Height=49};var icon=Ui.Image(app.Icon??(windows.Count>0?host.Icons.TaskIcon(windows[0]):Ui.SymbolImage("▦")),40);icon.VerticalAlignment=VerticalAlignment.Top;icon.Margin=new Thickness(1,1,1,0);cell.Children.Add(icon);
   System.Windows.Shapes.Path ring=null;
   if(pinned){
    ring=new System.Windows.Shapes.Path{
     Width=44,Height=44,
     VerticalAlignment=VerticalAlignment.Top,
     HorizontalAlignment=HorizontalAlignment.Center,
     Stroke=ActivePillBrush,
     StrokeThickness=2.6,
     StrokeStartLineCap=PenLineCap.Round,
     StrokeEndLineCap=PenLineCap.Round,
     IsHitTestVisible=false,
     Visibility=Visibility.Collapsed
    };
    cell.Children.Add(ring);
   }
   Border dot=null;
   if(windows.Count>0){dot=new Border{Width=windows.Count>1?8:4,Height=4,CornerRadius=new CornerRadius(2),Background=RunningDot,VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Center};cell.Children.Add(dot);}
   var scale=new ScaleTransform(1,1);
   var translate=new TranslateTransform(0,0);
   Button button=null;button=Ui.Button(cell,delegate{if(pinReorderReady||pinReorderMoved)return;SelectTask(button,app,windows);});button.Padding=new Thickness(3,1,3,1);button.Margin=new Thickness(2,0,2,0);button.ToolTip=windows.Count>1?app.Name+" ("+windows.Count+" 个窗口)":app.Name;
   button.RenderTransformOrigin=new Point(0.5,0.5);
   button.RenderTransform=new TransformGroup{Children={scale,translate}};
   System.Windows.Automation.AutomationProperties.SetName(button,app.Name);
   if(windows.Count>0){
    taskButtons[button]=windows;
    if(dot!=null)indicatorDots[button]=dot;
    button.MouseEnter+=delegate{
     if(host.IsLauncherVisible||holdingButton!=null)return;
     pendingPreviewTasks=windows;
     pendingPreviewAnchor=button;
     if(host.IsPreviewVisible)host.ShowPreview(pendingPreviewTasks,button);
     else{hoverTimer.Stop();hoverTimer.Start();}
    };
    button.MouseLeave+=delegate{hoverTimer.Stop();leaveTimer.Start();};
   }
   var lift=new TranslateTransform(0,0);
   icon.RenderTransform=lift;
   var liftEase=new CubicEase{EasingMode=EasingMode.EaseOut};
   button.MouseEnter+=delegate{if(holdingButton==null)lift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(-2.5,TimeSpan.FromMilliseconds(140)){EasingFunction=liftEase});};
   button.MouseLeave+=delegate{lift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,TimeSpan.FromMilliseconds(180)){EasingFunction=liftEase});};
   button.PreviewMouseDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){
    if(e.ChangedButton!=System.Windows.Input.MouseButton.Middle)return;
    e.Handled=true;
    hoverTimer.Stop();host.HidePreviews();
    if(windows.Count>0&&host.Settings.MiddleClickCloses){
     IntPtr fg=Native.GetForegroundWindow();
     var target=windows.FirstOrDefault(w=>w.Handle==fg)??windows[0];
     Native.PostMessage(target.Handle,0x10,IntPtr.Zero,IntPtr.Zero);
     return;
    }
    if(String.IsNullOrEmpty(app.Source)||app.Source.EndsWith("ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase))return;
    try{LauncherService.Launch(app);}
    catch(Exception error){Store.Error("dock middle launch",error);}
   };
   if(pinned){
    button.PreviewMouseLeftButtonDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){
     StartPinHold(button,ring,scale,translate,app,windows,e.GetPosition(this));
    };
    button.PreviewMouseMove+=delegate(object sender,System.Windows.Input.MouseEventArgs e){
     if(holdingButton==button)MovePinHold(e);
    };
    button.PreviewMouseLeftButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){
     if(holdingButton==button){
      bool wasReorder=pinReorderReady||pinReorderMoved;
      EndPinHold(true);
      e.Handled=true;
      if(!wasReorder)SelectTask(button,app,windows);
     }
    };
    button.LostMouseCapture+=delegate{
     if(holdingButton==button)EndPinHold(true);
    };
    button.AllowDrop=true;
    button.DragOver+=delegate(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Link:DragDropEffects.None;e.Handled=true;};
    button.Drop+=delegate(object sender,DragEventArgs e){
     int idx=host.Settings.Pinned.FindIndex(p=>String.Equals(p,app.Source,StringComparison.OrdinalIgnoreCase));
     HandleFileDrop(e,idx);
    };
   }
   var menu=new ContextMenu();
   if(windows.Count>0){foreach(var task in windows){var t=task;Add(menu,t.Title,delegate{Native.Activate(t.Handle);});}menu.Items.Add(new Separator());}
   bool hasSource=!String.IsNullOrEmpty(app.Source)&&!app.Source.EndsWith("ApplicationFrameHost.exe",StringComparison.OrdinalIgnoreCase)&&(File.Exists(app.Source)||Directory.Exists(app.Source));
   if(hasSource){
    Add(menu,"新开一个窗口",delegate{try{LauncherService.Launch(app);}catch(Exception err){host.Notify("无法打开："+err.Message);}});
    if(File.Exists(app.Source)&&!Directory.Exists(app.Source))
     Add(menu,"以管理员身份运行",delegate{try{LauncherService.LaunchAsAdmin(app);}catch{ }});
    Add(menu,"打开文件所在位置",delegate{try{LauncherService.OpenLocation(app);}catch{ }});
    menu.Items.Add(new Separator());
   }
   if(pinned)Add(menu,"取消固定",delegate{host.Unpin(app.Source);});else if(hasSource)Add(menu,"固定到 Dock",delegate{host.Pin(app);});
   if(windows.Count>0)Add(menu,"关闭窗口",delegate{foreach(var task in windows)Native.PostMessage(task.Handle,0x10,IntPtr.Zero,IntPtr.Zero);});button.ContextMenu=menu;return button;
  }
  static void RunSystemTool(string file,string args){
   System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file,args){UseShellExecute=true});
  }
  void HandleFileDrop(DragEventArgs e,int insertIndex){
   var files=e.Data.GetData(DataFormats.FileDrop) as string[];
   if(files==null||files.Length==0)return;
   int added=0;
   string lastName="";
   foreach(string path in files){
    if(!File.Exists(path)&&!Directory.Exists(path))continue;
    if(host.Settings.Pinned.Any(p=>String.Equals(p,path,StringComparison.OrdinalIgnoreCase)))continue;
    if(insertIndex>=0&&insertIndex<=host.Settings.Pinned.Count){host.Settings.Pinned.Insert(insertIndex,path);insertIndex++;}
    else host.Settings.Pinned.Add(path);
    lastName=System.IO.Path.GetFileNameWithoutExtension(path);
    if(String.IsNullOrEmpty(lastName))lastName=System.IO.Path.GetFileName(path);
    added++;
   }
   if(added>0){
    host.SavePins();
    host.Notify(added==1?"已固定到 Dock: "+lastName:"已固定 "+added+" 个项目到 Dock");
   }else{
    host.Notify("该项目已在 Dock 中");
   }
   e.Handled=true;
  }
  void OnDockWheel(int delta){
   if(dockHidden)return;
   try{
    Point screenPt=GetCursorScreenPoint();
    if(volumeButton!=null&&volumeButton.IsVisible){
     Point rel=volumeButton.PointFromScreen(screenPt);
     if((rel.X>=-10&&rel.X<=volumeButton.ActualWidth+10&&rel.Y>=-12&&rel.Y<=volumeButton.ActualHeight+12)||volumeButton.IsMouseOver){
      AdjustVolumeByDelta(delta);
      return;
     }
    }
    foreach(var pair in taskButtons){
     var btn=pair.Key;
     var wins=pair.Value;
     if(wins.Count<=1||!btn.IsVisible)continue;
     Point rel=btn.PointFromScreen(screenPt);
     if((rel.X>=-4&&rel.X<=btn.ActualWidth+4&&rel.Y>=-8&&rel.Y<=btn.ActualHeight+8)||btn.IsMouseOver){
      IntPtr fg=Native.GetForegroundWindow();
      int idx=wins.FindIndex(w=>w.Handle==fg);
      int next=idx<0?0:(delta<0?(idx+1)%wins.Count:(idx-1+wins.Count)%wins.Count);
      ActivateFromDock(wins[next].Handle);
      return;
     }
    }
   }catch{ }
  }
  static Point GetCursorScreenPoint(){
   Native.POINT pt;
   Native.GetCursorPos(out pt);
   return new Point(pt.X,pt.Y);
  }
  void AdjustVolumeByDelta(int delta){
   try{
    int nextVol=Math.Max(0,Math.Min(100,AudioVolume.GetPercent()+(delta>0?4:-4)));
    AudioVolume.SetPercent(nextVol);
    if(volumeButton!=null)volumeButton.ToolTip="音量: "+nextVol+"%（滚轮调音量 · 中键静音）";
    volumeMuted=nextVol==0;
    ShowVolumeBadge(nextVol+"%");
    host.Systems.SyncQuickVolume();
   }catch{ }
  }
  void StartPinHold(Button button,System.Windows.Shapes.Path ring,ScaleTransform scale,TranslateTransform translate,AppEntry app,List<TaskEntry> windows,Point startPos){
   EndPinHold(false);
   holdingButton=button;
   holdingRing=ring;
   holdingScale=scale;
   holdingTranslate=translate;
   holdingSource=app.Source;
   holdMouseStart=startPos;
   holdStarted=DateTime.UtcNow;
   pinReorderReady=false;
   pinReorderMoved=false;
   if(holdingRing!=null){
    holdingRing.Data=null;
    holdingRing.Visibility=Visibility.Visible;
   }
   holdTimer.Start();
  }
  void UpdatePinHold(){
   if(holdingButton==null||System.Windows.Input.Mouse.LeftButton!=System.Windows.Input.MouseButtonState.Pressed){
    EndPinHold(true);
    return;
   }
   if(pinReorderReady)return;
   double progress=(DateTime.UtcNow-holdStarted).TotalMilliseconds/1000.0;
   if(progress>=0.12&&holdingRing!=null){
    double ringProgress=Math.Min(1.0,(progress-0.12)/0.88);
    holdingRing.Data=BuildHoldRingGeometry(ringProgress);
   }
   if(progress>=1.0){
    pinReorderReady=true;
    holdTimer.Stop();
    hoverTimer.Stop();
    host.HidePreviews();
    if(holdingRing!=null)holdingRing.Data=BuildHoldRingGeometry(1.0);
    if(holdingScale!=null){holdingScale.ScaleX=1.1;holdingScale.ScaleY=1.1;}
    Panel.SetZIndex(holdingButton,10);
    dragLastInRow=System.Windows.Input.Mouse.GetPosition(pinnedRow);
    holdingButton.CaptureMouse();
   }
  }
  void MovePinHold(System.Windows.Input.MouseEventArgs e){
   if(holdingButton==null)return;
   if(e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed){
    EndPinHold(true);
    return;
   }
   if(!pinReorderReady){
    if((e.GetPosition(this)-holdMouseStart).Length>12)EndPinHold(false);
    return;
   }
   Point currentInRow=e.GetPosition(pinnedRow);
   double dx=currentInRow.X-dragLastInRow.X;
   dragLastInRow=currentInRow;
   if(holdingTranslate!=null)holdingTranslate.X+=dx;
   if(Math.Abs(holdingTranslate.X)>2)pinReorderMoved=true;
   int currentIndex=pinnedRow.Children.IndexOf(holdingButton);
   if(currentIndex<0)return;
   double step=holdingButton.ActualWidth+4;
   if(step<30)step=51;
   while(holdingTranslate.X>step*0.55&&currentIndex<pinnedRow.Children.Count-1){
    pinnedRow.Children.RemoveAt(currentIndex);
    pinnedRow.Children.Insert(currentIndex+1,holdingButton);
    holdingTranslate.X-=step;
    SwapPinnedSetting(holdingSource,1);
    currentIndex++;
    pinReorderMoved=true;
   }
   while(holdingTranslate.X<-step*0.55&&currentIndex>0){
    pinnedRow.Children.RemoveAt(currentIndex);
    pinnedRow.Children.Insert(currentIndex-1,holdingButton);
    holdingTranslate.X+=step;
    SwapPinnedSetting(holdingSource,-1);
    currentIndex--;
    pinReorderMoved=true;
   }
  }
  void SwapPinnedSetting(string source,int direction){
   int index=host.Settings.Pinned.FindIndex(p=>String.Equals(p,source,StringComparison.OrdinalIgnoreCase));
   int target=index+direction;
   if(index>=0&&target>=0&&target<host.Settings.Pinned.Count){
    string item=host.Settings.Pinned[index];
    host.Settings.Pinned.RemoveAt(index);
    host.Settings.Pinned.Insert(target,item);
   }
  }
  void EndPinHold(bool saveIfMoved){
   holdTimer.Stop();
   var btn=holdingButton;
   var ring=holdingRing;
   var scale=holdingScale;
   var translate=holdingTranslate;
   bool shouldSave=saveIfMoved&&pinReorderMoved;
   holdingButton=null;
   holdingRing=null;
   holdingScale=null;
   holdingTranslate=null;
   holdingSource=null;
   pinReorderReady=false;
   pinReorderMoved=false;
   if(btn!=null){
    Panel.SetZIndex(btn,0);
    if(btn.IsMouseCaptured)btn.ReleaseMouseCapture();
   }
   if(ring!=null){
    ring.Visibility=Visibility.Collapsed;
    ring.Data=null;
   }
   if(scale!=null){scale.ScaleX=1;scale.ScaleY=1;}
   if(translate!=null)translate.X=0;
   if(shouldSave){
    Store.Save(host.Settings);
    signature=String.Join("|",host.Settings.Pinned.Concat(tasks.Select(t=>t.Handle.ToInt64().ToString()+":"+t.Executable).OrderBy(s=>s,StringComparer.Ordinal)));
   }
  }
  static Geometry BuildHoldRingGeometry(double progress){
   const double cx=22,cy=22,r=19.5;
   if(progress>=0.999){
    var full=new EllipseGeometry(new Point(cx,cy),r,r);
    full.Freeze();
    return full;
   }
   double angle=Math.Max(1,progress*360.0);
   double rad=(angle-90.0)*Math.PI/180.0;
   var start=new Point(cx,cy-r);
   var end=new Point(cx+r*Math.Cos(rad),cy+r*Math.Sin(rad));
   var seg=new ArcSegment(end,new Size(r,r),0,angle>180.0,SweepDirection.Clockwise,true);
   var fig=new PathFigure(start,new PathSegment[]{seg},false);
   var geom=new PathGeometry(new[]{fig});
   geom.Freeze();
   return geom;
  }
  void SelectTask(Button button,AppEntry app,List<TaskEntry> windows){
   IntPtr foreground=Native.GetForegroundWindow();
   if(keyboardAwake&&winKey!=null){
    IntPtr beforeClick=winKey.TakeDockClickForeground();
    if(foreground==dockHandle)foreground=beforeClick!=IntPtr.Zero?beforeClick:clickForeground;
   }
   clickForeground=IntPtr.Zero;
   bool dismissedStart=false;
   try{dismissedStart=WindowsStartMenu.DismissIfVisible();}
   catch(Exception error){Store.Error("dismiss Windows Start menu",error);}
   host.HideLauncher();host.HidePreviews();
   if(windows.Count==0){
    try{LauncherService.Launch(app);}
    catch(Exception error){Store.Error("dock launch",error);host.Notify("无法打开“"+app.Name+"”。");}
    return;
   }
   var activeWindow=windows.FirstOrDefault(task=>task.Handle==foreground);
   if(!dismissedStart&&activeWindow!=null&&activeWindow.Handle!=fullscreenWakeOrigin&&!Native.IsIconic(activeWindow.Handle)){
    Native.ShowWindow(activeWindow.Handle,6);
    return;
   }
   if(windows.Count==1)ActivateFromDock(windows[0].Handle);
   else ShowWindowMenu(button,windows);
  }
  void ActivateFromDock(IntPtr target){
   Native.Activate(target);
   if(keyboardAwake&&(target==fullscreenWakeOrigin||Native.IsFullScreen(target)))EndKeyboardWake();
  }
  void ShowWindowMenu(Button anchor,List<TaskEntry> windows){var menu=new ContextMenu{PlacementTarget=anchor,Placement=System.Windows.Controls.Primitives.PlacementMode.Top};foreach(var task in windows){var selected=task;Add(menu,selected.Title,delegate{ActivateFromDock(selected.Handle);});}menu.IsOpen=true;}
  public void SetIme(string value,bool confirmed){ime.Text=value;ime.Opacity=confirmed?1:.55;}
 }
}


