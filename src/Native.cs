using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopSpace {
 internal static class Native {
  internal delegate bool EnumCallback(IntPtr hwnd,IntPtr state);
  internal delegate void WinEventCallback(IntPtr hook,uint eventId,IntPtr window,int objectId,int childId,uint thread,uint time);
  [StructLayout(LayoutKind.Sequential)]internal struct RECT{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]internal struct POINT{public int X,Y;}
  [DllImport("user32.dll")]internal static extern bool EnumWindows(EnumCallback callback,IntPtr state);
  [DllImport("user32.dll")]internal static extern bool EnumChildWindows(IntPtr parent,EnumCallback callback,IntPtr state);
  [DllImport("user32.dll")]internal static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")]internal static extern bool IsWindow(IntPtr hwnd);
  [DllImport("user32.dll")]internal static extern bool IsIconic(IntPtr hwnd);
  [DllImport("user32.dll")]internal static extern bool IsZoomed(IntPtr hwnd);
  [DllImport("user32.dll")]internal static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
  [DllImport("user32.dll")]internal static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")]internal static extern IntPtr SetWinEventHook(uint firstEvent,uint lastEvent,IntPtr module,WinEventCallback callback,uint process,uint thread,uint flags);
  [DllImport("user32.dll")]internal static extern bool UnhookWinEvent(IntPtr hook);
  [DllImport("user32.dll")]internal static extern bool GetCursorPos(out POINT point);
  [DllImport("user32.dll")]internal static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]internal static extern short GetAsyncKeyState(int virtualKey);
  [DllImport("user32.dll")]static extern bool GetClipCursor(out RECT bounds);
  [DllImport("user32.dll")]static extern bool GetCursorInfo(ref CursorInfo info);
  [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
  [DllImport("shell32.dll")]static extern int SHQueryUserNotificationState(out int state);
  [StructLayout(LayoutKind.Sequential)]struct CursorInfo{public int Size,Flags; public IntPtr Cursor;public POINT Position;}
  [StructLayout(LayoutKind.Sequential)]struct MonitorInfo{public int Size;public RECT Monitor,Work;public int Flags;}
  [StructLayout(LayoutKind.Explicit,Size=40)]
  struct KeyboardInput{
   [FieldOffset(0)]public uint Type;
   [FieldOffset(8)]public ushort Key;
   [FieldOffset(12)]public uint Flags;
  }
  [DllImport("user32.dll")]static extern uint SendInput(uint count,KeyboardInput[] inputs,int size);
  internal static bool TryTapShift(){
   const int shift=0x10,control=0x11,alt=0x12,leftWin=0x5B,rightWin=0x5C;
   if(GetAsyncKeyState(shift)<0||GetAsyncKeyState(control)<0||GetAsyncKeyState(alt)<0||
      GetAsyncKeyState(leftWin)<0||GetAsyncKeyState(rightWin)<0||
      GetAsyncKeyState(0x01)<0||GetAsyncKeyState(0x02)<0)return false;
   var keys=new[]{new KeyboardInput{Type=1,Key=shift},new KeyboardInput{Type=1,Key=shift,Flags=2}};
   uint sent=SendInput((uint)keys.Length,keys,40);
   if(sent==1)SendInput(1,new[]{keys[1]},40);
   return sent==keys.Length;
  }
  internal static bool SendWinCombo(ushort key){
   const ushort win=0x5B;
   var inputs=new[]{
    new KeyboardInput{Type=1,Key=win},
    new KeyboardInput{Type=1,Key=key},
    new KeyboardInput{Type=1,Key=key,Flags=2},
    new KeyboardInput{Type=1,Key=win,Flags=2}
   };
   return SendInput((uint)inputs.Length,inputs,40)==inputs.Length;
  }
  internal static bool ReleaseWinWithoutStart(ushort key){
   // An unassigned key consumes the pending Start gesture before Win goes up.
   const ushort mask=0xE8;
   var inputs=new[]{
    new KeyboardInput{Type=1,Key=mask},
    new KeyboardInput{Type=1,Key=mask,Flags=2},
    new KeyboardInput{Type=1,Key=key,Flags=3}
   };
   uint sent=SendInput(3,inputs,40);
   if(sent==1)sent+=SendInput(2,new[]{inputs[1],inputs[2]},40);
   if(sent==2)sent+=SendInput(1,new[]{inputs[2]},40);
   return sent==3;
  }
  [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern int GetClassName(IntPtr hwnd,StringBuilder text,int count);
  [DllImport("user32.dll")]internal static extern IntPtr GetWindow(IntPtr hwnd,uint command);
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern IntPtr FindWindow(string cls,string name);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string name);
  [DllImport("user32.dll")]internal static extern bool ShowWindow(IntPtr hwnd,int command);
  [DllImport("user32.dll")]internal static extern bool SetForegroundWindow(IntPtr hwnd);
  [DllImport("user32.dll")]internal static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
  [DllImport("user32.dll")]internal static extern bool AllowSetForegroundWindow(uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern uint RegisterWindowMessage(string name);
  [DllImport("user32.dll",SetLastError=true)]internal static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint msg,UIntPtr wp,IntPtr lp,uint flags,uint timeout,out UIntPtr result);
  [DllImport("dwmapi.dll")]internal static extern int DwmGetWindowAttribute(IntPtr hwnd,int attr,out int value,int size);
  [DllImport("dwmapi.dll")]internal static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
  [DllImport("user32.dll",SetLastError=true)]internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint color,byte alpha,uint flags);
  [DllImport("user32.dll",SetLastError=true)]internal static extern bool GetLayeredWindowAttributes(IntPtr hwnd,out uint color,out byte alpha,out uint flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool QueryFullProcessImageName(IntPtr process,int flags,StringBuilder path,ref int length);
  [DllImport("kernel32.dll",ExactSpelling=true,CharSet=CharSet.Unicode)]static extern int GetApplicationUserModelId(IntPtr process,ref uint length,StringBuilder id);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  [DllImport("user32.dll")]internal static extern bool LockWorkStation();
  [DllImport("powrprof.dll",SetLastError=true)]internal static extern bool SetSuspendState(bool hibernate,bool forceCritical,bool disableWakeEvent);
  [DllImport("user32.dll")]internal static extern bool RegisterHotKey(IntPtr hWnd,int id,uint fsModifiers,uint vk);
  [DllImport("user32.dll")]internal static extern bool UnregisterHotKey(IntPtr hWnd,int id);
  [DllImport("user32.dll")]internal static extern bool DestroyIcon(IntPtr icon);
  internal static readonly uint ToggleMessage=RegisterWindowMessage("ROG.DesktopSpace.ToggleLauncher.v1");
  internal static readonly uint RestoreTaskbarMessage=RegisterWindowMessage("ROG.DesktopSpace.RestoreTaskbar.v1");
  internal static readonly uint OpenTrayMessage=RegisterWindowMessage("ROG.DesktopSpace.OpenTray.v1");
  internal static readonly uint OpenControlsMessage=RegisterWindowMessage("ROG.DesktopSpace.OpenControls.v1");
  internal static readonly uint ExitLauncherMessage=RegisterWindowMessage("ROG.DesktopSpace.ExitLauncher.v1");
  internal const string DockTitle="桌面空间 · Dock";
  internal const string LauncherHostTitle="桌面空间 · 启动器入口";
  internal static string Title(IntPtr hwnd){var b=new StringBuilder(512);GetWindowText(hwnd,b,b.Capacity);return b.ToString();}
  internal static string Class(IntPtr hwnd){var b=new StringBuilder(128);GetClassName(hwnd,b,b.Capacity);return b.ToString();}
  static readonly Dictionary<uint,string> processPaths=new Dictionary<uint,string>();
  static readonly Dictionary<uint,string> processAppIds=new Dictionary<uint,string>();
  internal static void PruneProcessCache(HashSet<uint> activePids){
   if(processPaths.Count>256)foreach(var k in new List<uint>(processPaths.Keys))if(!activePids.Contains(k))processPaths.Remove(k);
   if(processAppIds.Count>256)foreach(var k in new List<uint>(processAppIds.Keys))if(!activePids.Contains(k))processAppIds.Remove(k);
  }
  internal static string ProcessPath(uint pid){
   string cached;
   lock(processPaths){if(processPaths.TryGetValue(pid,out cached))return cached;}
   IntPtr process=OpenProcess(0x1000,false,pid);
   if(process==IntPtr.Zero)return "";
   try{
    var path=new StringBuilder(512);
    int length=path.Capacity;
    string result="";
    if(QueryFullProcessImageName(process,0,path,ref length))result=path.ToString();
    else if(Marshal.GetLastWin32Error()==122){
     path.Capacity=32768;
     length=path.Capacity;
     if(QueryFullProcessImageName(process,0,path,ref length))result=path.ToString();
    }
    if(result.Length>0)lock(processPaths){processPaths[pid]=result;}
    return result;
   }finally{CloseHandle(process);}
  }
  internal static string ProcessAppId(uint pid){
   string cached;
   lock(processAppIds){if(processAppIds.TryGetValue(pid,out cached))return cached;}
   IntPtr process=OpenProcess(0x1000,false,pid);
   if(process==IntPtr.Zero)return "";
   try{
    uint length=0;
    const int insufficientBuffer=122;
    string result="";
    if(GetApplicationUserModelId(process,ref length,null)==insufficientBuffer&&length>0){
     var id=new StringBuilder((int)length);
     if(GetApplicationUserModelId(process,ref length,id)==0)result=id.ToString();
    }
    lock(processAppIds){processAppIds[pid]=result;}
    return result;
   }finally{CloseHandle(process);}
  }
  internal static void Taskbar(bool visible,bool includeSecondary=true){
   SetTaskbarVisibility(FindWindow("Shell_TrayWnd",null),visible);
   if(visible||includeSecondary)SecondaryTaskbars(visible);
  }
  internal static void SecondaryTaskbars(bool visible){
   IntPtr secondary=IntPtr.Zero;
   while((secondary=FindWindowEx(IntPtr.Zero,secondary,"Shell_SecondaryTrayWnd",null))!=IntPtr.Zero)
    SetTaskbarVisibility(secondary,visible);
  }
  static void SetTaskbarVisibility(IntPtr window,bool visible){
   if(window!=IntPtr.Zero&&IsWindowVisible(window)!=visible)ShowWindow(window,visible?8:0);
  }
  internal static IntPtr FindTrayOverflow(bool includeHidden=false){
   IntPtr result=IntPtr.Zero;
   IntPtr taskbar=FindWindow("Shell_TrayWnd",null);
   uint explorerPid;
   if(taskbar==IntPtr.Zero||GetWindowThreadProcessId(taskbar,out explorerPid)==0)return result;
   EnumWindows(delegate(IntPtr window,IntPtr state){
    uint ownerPid;
    GetWindowThreadProcessId(window,out ownerPid);
    if(ownerPid!=explorerPid||(!includeHidden&&!IsWindowVisible(window)))return true;
    string name=Class(window);
    if(!IsTrayOverflowClass(name))return true;
    result=window;
    return false;
   },IntPtr.Zero);
   return result;
  }
  internal static bool IsTrayOverflowClass(string name){return name=="NotifyIconOverflowWindow"||name=="TopLevelWindowForOverflowXamlIsland";}
  [DllImport("user32.dll")]static extern bool BringWindowToTop(IntPtr hwnd);
  [DllImport("user32.dll")]static extern void SwitchToThisWindow(IntPtr hwnd,bool altTab);
  [DllImport("user32.dll")]static extern bool AttachThreadInput(uint idAttach,uint idAttachTo,bool fAttach);
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  internal static void Activate(IntPtr hwnd){
   if(!IsWindow(hwnd))return;
   if(IsIconic(hwnd))ShowWindow(hwnd,9);
   if(SetForegroundWindow(hwnd)&&GetForegroundWindow()==hwnd)return;
   uint fgPid;
   uint fgThread=GetWindowThreadProcessId(GetForegroundWindow(),out fgPid);
   uint curThread=GetCurrentThreadId();
   if(fgThread!=0&&fgThread!=curThread){
    AttachThreadInput(curThread,fgThread,true);
    BringWindowToTop(hwnd);
    SetForegroundWindow(hwnd);
    AttachThreadInput(curThread,fgThread,false);
   }
   if(GetForegroundWindow()!=hwnd)SwitchToThisWindow(hwnd,true);
  }
  internal static bool CoversDockInSplitView(IntPtr window,IntPtr dock,int edgeTolerance){
   if(window==IntPtr.Zero||dock==IntPtr.Zero||window==dock||IsIconic(window))return false;
   IntPtr monitor=MonitorFromWindow(dock,0);
   if(monitor==IntPtr.Zero||MonitorFromWindow(window,0)!=monitor)return false;
   RECT bounds,dockBounds;
   if(!GetWindowRect(window,out bounds)||!GetWindowRect(dock,out dockBounds)||
      bounds.Left>=dockBounds.Right||bounds.Right<=dockBounds.Left||bounds.Bottom<=dockBounds.Top)return false;
   var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
   if(!GetMonitorInfo(monitor,ref info)||bounds.Top>info.Work.Top+edgeTolerance||
      bounds.Bottom<info.Work.Bottom-edgeTolerance)return false;
   return !IsDesktopShell(window);
  }
  internal static bool IsFullScreen(IntPtr window){
   if(window==IntPtr.Zero||IsZoomed(window)||IsDesktopShell(window))return false;
   RECT bounds;
   var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
   if(!GetWindowRect(window,out bounds)||!GetMonitorInfo(MonitorFromWindow(window,2),ref info))return false;
   var screen=info.Monitor;
   return bounds.Left<=screen.Left&&bounds.Top<=screen.Top&&bounds.Right>=screen.Right&&bounds.Bottom>=screen.Bottom;
  }
  static bool IsDesktopShell(IntPtr window){
   string cls=Class(window);
   return cls=="Progman"||cls=="WorkerW"||cls=="Shell_TrayWnd"||cls=="Shell_SecondaryTrayWnd";
  }
  internal static bool CanClickDock(IntPtr dock,IntPtr foreground){
   RECT clip,area;
   if(GetClipCursor(out clip)&&GetWindowRect(dock,out area)&&
      (clip.Left>area.Left||clip.Right<area.Right||clip.Bottom>area.Bottom||clip.Top>area.Top))return false;
   int state;
   if(SHQueryUserNotificationState(out state)==0&&state==3)return false;
   if(!IsFullScreen(foreground))return true;
   var cursor=new CursorInfo{Size=Marshal.SizeOf(typeof(CursorInfo))};
   return !GetCursorInfo(ref cursor)||(cursor.Flags&1)!=0;
  }
 }
 public static class WindowService {
  public static List<TaskEntry> Enumerate(){var result=new List<TaskEntry>();uint own=(uint)Process.GetCurrentProcess().Id;Native.EnumWindows(delegate(IntPtr h,IntPtr s){
   if(!Native.IsWindowVisible(h)||Native.GetWindow(h,4)!=IntPtr.Zero)return true;
   uint pid;Native.GetWindowThreadProcessId(h,out pid);if(pid==own)return true;
   long style=Native.GetWindowLongPtr(h,-20).ToInt64();if((style&0x80)!=0)return true;
   string cls=Native.Class(h);if(cls=="Progman"||cls=="WorkerW"||cls=="Shell_TrayWnd"||cls=="Shell_SecondaryTrayWnd"||cls=="Windows.UI.Core.CoreWindow")return true;
   string title=Native.Title(h);if(String.IsNullOrWhiteSpace(title))return true;
   int cloaked=0;try{Native.DwmGetWindowAttribute(h,14,out cloaked,4);}catch{ }if(cloaked!=0)return true;
   result.Add(new TaskEntry{Handle=h,Pid=pid,Title=title,Executable=Native.ProcessPath(pid)});return true;
  },IntPtr.Zero);return result;}
 }
}
