using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Automation;
using System.Threading;

namespace DesktopSpace {
 public sealed class TaskModule:IShellModule {
  const uint EventSystemForeground=0x0003;
  readonly DispatcherTimer timer=new DispatcherTimer();readonly Dictionary<long,int> order=new Dictionary<long,int>();int sequence;
  readonly Native.WinEventCallback foregroundCallback;
  IntPtr foregroundHook;
  public string Id{get{return "tasks";}}public event Action<List<TaskEntry>> Changed;public event Action ForegroundChanged;
  public TaskModule(){
   timer.Interval=TimeSpan.FromMilliseconds(650);
   timer.Tick+=delegate{Update();};
   foregroundCallback=OnForegroundEvent;
  }
  void OnForegroundEvent(IntPtr hook,uint eventId,IntPtr window,int objectId,int childId,uint thread,uint time){
   if(ForegroundChanged!=null)ForegroundChanged();
  }
  void Update(){try{var items=WindowService.Enumerate();var pids=new HashSet<uint>();foreach(var task in items){pids.Add(task.Pid);if(!order.ContainsKey(task.Handle.ToInt64()))order[task.Handle.ToInt64()]=sequence++;}Native.PruneProcessCache(pids);if(order.Count>items.Count){var open=new HashSet<long>(items.Select(task=>task.Handle.ToInt64()));foreach(var handle in order.Keys.Where(handle=>!open.Contains(handle)).ToArray())order.Remove(handle);}items.Sort((left,right)=>order[left.Handle.ToInt64()].CompareTo(order[right.Handle.ToInt64()]));if(Changed!=null)Changed(items);}catch(Exception e){Store.Error("tasks",e);}}
  public void Start(){
   Update();
   timer.Start();
   foregroundHook=Native.SetWinEventHook(EventSystemForeground,EventSystemForeground,IntPtr.Zero,foregroundCallback,0,0,0);
  }
  public void Dispose(){
   timer.Stop();
   if(foregroundHook!=IntPtr.Zero){Native.UnhookWinEvent(foregroundHook);foregroundHook=IntPtr.Zero;}
  }
 }
 public sealed class ImeModule:IShellModule {
  const int ShiftKey=0x10;
  const int SampleIntervalMs=12;
  static readonly long NormalInterval=Stopwatch.Frequency*40/1000;
  static readonly long QuickRetryWindow=Stopwatch.Frequency*150/1000;
  static readonly long SyncWindow=Stopwatch.Frequency*220/1000;
  static readonly uint ownPid=(uint)Process.GetCurrentProcess().Id;
  readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
  Timer timer;
  string previous="";
  long unknownSince;
  long nextRead,quickReadsUntil;
  IntPtr lastPublishedWindow;
  IntPtr shiftWindow,inputWindow,inputFocus;
  string sharedMode,observedMode;
  long syncUntil;
  bool syncAttempted;
  bool uncertain,shiftWasDown;
  int reading,publishAgain;
  public string Id{get{return "ime";}}public event Action<string,bool> Changed;
  void Poll(object unused){
   if(Interlocked.Exchange(ref reading,1)!=0)return;
   try{
    long now=Stopwatch.GetTimestamp();
    bool shiftDown=Native.GetAsyncKeyState(ShiftKey)<0;
    if(!shiftWasDown&&shiftDown)shiftWindow=Native.GetForegroundWindow();
    if(shiftWasDown&&!shiftDown){
     quickReadsUntil=now+QuickRetryWindow;
     nextRead=now;
    }
    shiftWasDown=shiftDown;
    if(now<nextRead)return;
    bool quick=now<quickReadsUntil;
    nextRead=quick?now:now+NormalInterval;
    IntPtr foreground=Native.GetForegroundWindow();
    uint owner;
    uint thread=Native.GetWindowThreadProcessId(foreground,out owner);
    if(thread==0||(owner==ownPid&&!dispatcher.Invoke(()=>System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase,DispatcherPriority.Input))){unknownSince=0;return;}
    var state=ImeReader.Read(foreground,thread,owner,quick?20u:75u);
    if(foreground!=Native.GetForegroundWindow()||state.KeepPrevious){unknownSince=0;return;}
    if(state.Mode==null){
     if(unknownSince==0)unknownSince=now;
     if(!uncertain&&previous.Length>0&&now-unknownSince>=Stopwatch.Frequency){
      uncertain=true;
      Publish(foreground,previous,false);
     }
     return;
    }
    unknownSince=0;
    bool userSwitch=quick&&foreground==shiftWindow;
    if(!SyncMode(foreground,thread,state,now,userSwitch))return;
    bool missedUpdate=Interlocked.Exchange(ref publishAgain,0)!=0;
    if(state.Mode!=previous||uncertain||foreground!=lastPublishedWindow||missedUpdate){
     uncertain=false;previous=state.Mode;
     Publish(foreground,previous,true);
    }
   }catch{ }
   finally{Interlocked.Exchange(ref reading,0);}
  }
  bool SyncMode(IntPtr foreground,uint thread,ImeReader.State state,long now,bool userSwitch){
   if(foreground!=inputWindow||state.Focus!=inputFocus){
    inputWindow=foreground;
    inputFocus=state.Focus;
    observedMode=state.Mode;
    syncAttempted=false;
    if(sharedMode==null||userSwitch)sharedMode=state.Mode;
    syncUntil=state.CanToggle&&state.Mode!=sharedMode?now+SyncWindow:0;
    return syncUntil==0;
   }
   if(syncUntil!=0){
    if(userSwitch)sharedMode=state.Mode;
    if(state.Mode==sharedMode||now>=syncUntil){
     syncUntil=0;
     observedMode=state.Mode;
     return true;
    }
    if(!syncAttempted&&state.CanToggle){
     // Carry the shared mode into this input context just once and verify the result.
     syncAttempted=ImeReader.TryToggle(foreground,thread,state.Focus);
    }
    return false;
   }
   if(observedMode!=state.Mode){
    observedMode=state.Mode;
    sharedMode=state.Mode;
   }
   return true;
  }
  void Publish(IntPtr foreground,string mode,bool confirmed){
   lastPublishedWindow=foreground;
   dispatcher.BeginInvoke(new Action(delegate{
    if(timer==null)return;
    if(foreground!=Native.GetForegroundWindow()){Interlocked.Exchange(ref publishAgain,1);return;}
    if(Changed!=null)Changed(mode,confirmed);
   }),DispatcherPriority.Input);
  }
  public void Start(){timer=new Timer(Poll,null,0,SampleIntervalMs);}
  public void Dispose(){
   var stopped=timer;
   timer=null;
   if(stopped!=null)stopped.Dispose();
  }
 }
 public sealed class SystemAccess:IShellModule {
  const uint TrayWindowDestroyed=0x8001,TrayWindowMoved=0x800B;
  readonly ShellHost host;
  readonly DispatcherTimer taskbarTimer=new DispatcherTimer();
  readonly DispatcherTimer trayTimer=new DispatcherTimer();
  TaskbarVisibility trayLease;
  TaskbarVisibility trayPopupMask;
  readonly Native.WinEventCallback trayWindowEvent;
  IntPtr trayWindowHook,anchoredTray;
  IntPtr maskedPopup;
  IntPtr trayTaskbar;
  DateTime trayStarted;
  bool trayInvoked;
  bool dismissTrayWhenReady;
  int trayAttempts;
  QuickSettingsWindow quickSettings;
  public string Id{get{return "system";}}
  public SystemAccess(ShellHost h){
   host=h;
   trayWindowEvent=OnTrayWindowEvent;
   taskbarTimer.Interval=TimeSpan.FromMilliseconds(180);
   taskbarTimer.Tick+=delegate{if(trayLease==null)Native.Taskbar(false,host.Settings.HideSecondaryTaskbars);};
   trayTimer.Interval=TimeSpan.FromMilliseconds(50);trayTimer.Tick+=delegate{CheckTray();};
  }
  public void Start(){taskbarTimer.Start();}
  internal void StopKeepingTaskbarHidden(){taskbarTimer.Stop();}
  public void ToggleTray(){
   HideQuickSettings();
   IntPtr popup=Native.FindTrayOverflow();
   if(popup!=IntPtr.Zero){CloseTray(popup);return;}
   if(trayLease!=null){
    if(trayInvoked)dismissTrayWhenReady=!dismissTrayWhenReady;
    else CloseTray(IntPtr.Zero);
    return;
   }
   host.HideLauncher();host.HidePreviews();
   trayTaskbar=Native.FindWindow("Shell_TrayWnd",null);
   if(trayTaskbar==IntPtr.Zero){host.Notify("暂时找不到 Windows 托盘。");return;}
   Native.Taskbar(false,host.Settings.HideSecondaryTaskbars);
   trayLease=TaskbarVisibility.TryOpen(trayTaskbar);
   if(trayLease==null){host.Notify("暂时无法展开托盘，请再试一次。");return;}
   Native.SetForegroundWindow(trayTaskbar);
   trayStarted=DateTime.UtcNow;
   trayInvoked=false;
   dismissTrayWhenReady=false;
   trayAttempts=0;
   trayTimer.Start();
  }

  void CheckTray(){
   IntPtr popup=Native.FindTrayOverflow();
   if(popup!=IntPtr.Zero){
    if(dismissTrayWhenReady){CloseTray(popup);return;}
    FinishTray();
    bool positioned=false;
    try{
     positioned=trayPopupMask!=null&&popup==maskedPopup&&PositionTray(popup);
     if(positioned)WatchTrayPosition(popup);
    }
    catch(Exception error){Store.Error("tray position",error);}
    finally{ReleasePopup(false);}
    RecordSystemAction(positioned?"tray-anchored":"tray-native",true,trayAttempts);
    return;
   }
   if((DateTime.UtcNow-trayStarted).TotalMilliseconds>=1400){
    FinishTray();
    ReleasePopup(true);
    RecordSystemAction("tray",false,trayAttempts);
    if(!dismissTrayWhenReady)host.Notify("Windows 托盘暂时未响应，请再试一次。");
    return;
   }
   if(!trayInvoked){
    if(trayPopupMask==null){
     maskedPopup=Native.FindTrayOverflow(true);
     trayPopupMask=TaskbarVisibility.TryMaskPopup(maskedPopup);
    }
    trayAttempts++;
    trayInvoked=InvokeTrayButton(trayTaskbar);
   }
  }

  void CloseTray(IntPtr popup){
   StopWatchingTray();
   if(popup!=IntPtr.Zero)Native.ShowWindow(popup,0);
   FinishTray();
   ReleasePopup(true);
  }

  bool PositionTray(IntPtr popup){
   var dock=host.Dock;
   if(dock==null)return false;
   var trayAnchor=dock.TrayAnchor;
   if(trayAnchor==null||PresentationSource.FromVisual(trayAnchor)==null)return false;
   Native.RECT bounds;
   if(!Native.GetWindowRect(popup,out bounds))return false;
   int width=bounds.Right-bounds.Left,height=bounds.Bottom-bounds.Top;
   if(width<=0||height<=0)return false;
   Point anchor=trayAnchor.PointToScreen(new Point(trayAnchor.ActualWidth/2,0));
   Point dockTop=dock.PointToScreen(new Point(0,0));
   int screenRight=(int)Math.Round(SystemParameters.PrimaryScreenWidth*Ui.Scale);
   int x=Math.Max(8,Math.Min(screenRight-width-8,(int)Math.Round(anchor.X-width/2.0)));
   int y=Math.Max(8,(int)Math.Round(dockTop.Y-height+4*Ui.Scale));
   if(bounds.Left==x&&bounds.Top==y)return true;
   const uint preserveSizeOrderAndFocus=0x01|0x04|0x10;
   return Native.SetWindowPos(popup,IntPtr.Zero,x,y,0,0,preserveSizeOrderAndFocus);
  }

  void WatchTrayPosition(IntPtr popup){
   StopWatchingTray();
   uint owner;
   if(Native.GetWindowThreadProcessId(popup,out owner)==0)return;
   trayWindowHook=Native.SetWinEventHook(TrayWindowDestroyed,TrayWindowMoved,IntPtr.Zero,trayWindowEvent,owner,0,0);
   if(trayWindowHook!=IntPtr.Zero)anchoredTray=popup;
  }

  void OnTrayWindowEvent(IntPtr hook,uint eventId,IntPtr window,int objectId,int childId,uint thread,uint time){
   if(hook!=trayWindowHook||window!=anchoredTray||objectId!=0||childId!=0)return;
   if(eventId==TrayWindowDestroyed||!Native.IsWindowVisible(window)){StopWatchingTray();return;}
   // Explorer can move its flyout back to the taskbar when the icon grid changes size.
   if(eventId==TrayWindowMoved){
    try{PositionTray(window);}
    catch(Exception error){Store.Error("keep tray anchored",error);}
   }
  }

  void StopWatchingTray(){
   if(trayWindowHook==IntPtr.Zero)return;
   Native.UnhookWinEvent(trayWindowHook);
   trayWindowHook=anchoredTray=IntPtr.Zero;
  }

  void ReleasePopup(bool cancel){
   if(trayPopupMask!=null){
    if(cancel)trayPopupMask.Cancel();else trayPopupMask.Dispose();
    trayPopupMask=null;
   }
   maskedPopup=IntPtr.Zero;
  }

  void FinishTray(){
   trayTimer.Stop();
   if(trayLease==null)return;
   trayLease.Dispose();
   trayLease=null;
  }

  public void ToggleStartMenu(){
   HideQuickSettings();
   host.HideLauncher();
   host.HidePreviews();
   CloseTray(Native.FindTrayOverflow());
   try{WindowsStartMenu.Toggle();}
   catch(Exception error){Store.Error("toggle Windows Start menu",error);host.Notify("暂时无法切换 Windows 开始菜单。");}
  }

  static void RecordSystemAction(string kind,bool success,int attempts){
   try{System.IO.File.WriteAllText(System.IO.Path.Combine(Store.Root,"last-system-action.json"),new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{kind=kind,invoked=success,attempts=attempts,utc=DateTime.UtcNow.ToString("o")}));}catch{ }
  }
  public void OpenQuickSettings(System.Windows.FrameworkElement anchor,string mode="volume"){
   host.HideLauncher();host.HidePreviews();
   if(quickSettings==null)quickSettings=new QuickSettingsWindow(host);
   quickSettings.Open(anchor,mode);
  }
  public void SyncQuickVolume(){
   if(quickSettings!=null)quickSettings.RefreshVolumeDisplay();
  }
  public void HideQuickSettings(){if(quickSettings!=null)quickSettings.Hide();}
  bool InvokeTrayButton(IntPtr bar){try{
   var root=AutomationElement.FromHandle(bar);var all=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
   foreach(AutomationElement element in all){string name=element.Current.Name??"",id=element.Current.AutomationId??"";bool matches=(name.Contains("隐藏")&&name.Contains("图标"))||name.IndexOf("hidden icon",StringComparison.OrdinalIgnoreCase)>=0||id.IndexOf("OverflowButton",StringComparison.OrdinalIgnoreCase)>=0;
    if(!matches)continue;object pattern;if(element.TryGetCurrentPattern(InvokePattern.Pattern,out pattern)){((InvokePattern)pattern).Invoke();return true;}if(element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern,out pattern)){((ExpandCollapsePattern)pattern).Expand();return true;}if(element.TryGetCurrentPattern(TogglePattern.Pattern,out pattern)){var toggle=(TogglePattern)pattern;if(toggle.Current.ToggleState!=ToggleState.On)toggle.Toggle();return true;}
   }
  }catch(Exception e){Store.Error("tray access",e);}return false;}
  public void Dispose(){StopKeepingTaskbarHidden();StopWatchingTray();FinishTray();ReleasePopup(true);if(quickSettings!=null)quickSettings.Close();if(host.FullMode)Native.Taskbar(true);}
 }
}

