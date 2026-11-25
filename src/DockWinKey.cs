using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace DesktopSpace {
 internal sealed class DockWinKey : IDisposable {
  internal static readonly uint WakeMessage=Native.RegisterWindowMessage("ROG.DesktopSpace.WakeDock.v1");
  internal static readonly uint OutsideClickMessage=Native.RegisterWindowMessage("ROG.DesktopSpace.WakeDock.OutsideClick.v1");
  internal static readonly uint DockWheelMessage=Native.RegisterWindowMessage("ROG.DesktopSpace.DockWheel.v1");
  const int KeyboardLowLevel=13,MouseLowLevel=14,LeftWin=0x5B,RightWin=0x5C,Injected=0x10;
  readonly IntPtr dock;
  readonly uint ownerPid;
  readonly HookCallback callback;
  readonly HookCallback mouseCallback;
  readonly object gate=new object();
  Dispatcher worker;
  IntPtr mouseHook,dockMouseDownForeground;
  bool disposed;
  bool watchingOutsideClicks;
  IntPtr pendingWindow;
  int heldKeys;
  bool chord;

  delegate IntPtr HookCallback(int code,IntPtr message,IntPtr data);
  [DllImport("user32.dll",EntryPoint="SetWindowsHookExW",SetLastError=true)]
  static extern IntPtr SetWindowsHookEx(int kind,HookCallback callback,IntPtr module,uint thread);
  [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
  [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
  [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Native.POINT point);
  [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);

  internal DockWinKey(IntPtr dock){
   this.dock=dock;
   using(var process=Process.GetCurrentProcess())ownerPid=(uint)process.Id;
   callback=OnKey;
   mouseCallback=OnMouse;
   new Thread(Run){IsBackground=true,Name="DesktopSpace Win key"}.Start();
  }

  void Run(){
   lock(gate){
    if(disposed)return;
    worker=Dispatcher.CurrentDispatcher;
   }
   IntPtr hook=SetWindowsHookEx(KeyboardLowLevel,callback,GetModuleHandle(null),0);
   if(hook==IntPtr.Zero){
    Store.Error("Win key Dock shortcut",new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    return;
   }
   mouseHook=SetWindowsHookEx(MouseLowLevel,mouseCallback,GetModuleHandle(null),0);
   try{Dispatcher.Run();}
   finally{
    SetOutsideClickWatch(false);
    if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}
    UnhookWindowsHookEx(hook);
   }
  }

  IntPtr OnKey(int code,IntPtr message,IntPtr data){
   if(code==0&&(Marshal.ReadInt32(data,8)&Injected)==0){
    int key=Marshal.ReadInt32(data);
    int kind=message.ToInt32();
    if(kind==0x100||kind==0x104)KeyDown(key);
    else if((kind==0x101||kind==0x105)&&KeyUp(key))return new IntPtr(1);
   }
   return CallNextHookEx(IntPtr.Zero,code,message,data);
  }

  void KeyDown(int key){
   int bit=WinBit(key);
   if(bit==0){
    if(heldKeys!=0)chord=true;
    return;
   }
   if((heldKeys&bit)!=0)return;
   if(heldKeys==0){
    pendingWindow=Native.GetForegroundWindow();
    chord=Native.GetAsyncKeyState(0x10)<0||Native.GetAsyncKeyState(0x11)<0||Native.GetAsyncKeyState(0x12)<0;
   }else chord=true;
   heldKeys|=bit;
  }

  bool KeyUp(int key){
   int bit=WinBit(key);
   if((heldKeys&bit)==0)return false;
   heldKeys&=~bit;
   if(heldKeys!=0||chord||pendingWindow!=Native.GetForegroundWindow()||!IsApplication(pendingWindow))return false;
   if(!Native.ReleaseWinWithoutStart((ushort)key))return false;
   Native.PostMessage(dock,WakeMessage,pendingWindow,IntPtr.Zero);
   return true;
  }

  bool IsApplication(IntPtr window){
   uint pid;
   if(window==IntPtr.Zero||Native.GetWindowThreadProcessId(window,out pid)==0||pid==ownerPid)return false;
   string cls=Native.Class(window);
   if(cls=="Progman"||cls=="WorkerW"||cls=="Shell_TrayWnd"||cls=="Shell_SecondaryTrayWnd")return false;
   if(cls!="Windows.UI.Core.CoreWindow")return true;
   string name=System.IO.Path.GetFileName(Native.ProcessPath(pid));
   return !string.IsNullOrEmpty(name)&&
    !name.Equals("StartMenuExperienceHost.exe",StringComparison.OrdinalIgnoreCase)&&
    !name.Equals("SearchHost.exe",StringComparison.OrdinalIgnoreCase)&&
    !name.Equals("ShellExperienceHost.exe",StringComparison.OrdinalIgnoreCase);
  }

  static int WinBit(int key){return key==LeftWin?1:key==RightWin?2:0;}

  internal void WatchOutsideClicks(bool enabled){
   lock(gate){
    if(!disposed&&worker!=null)
     worker.BeginInvoke(new Action(()=>SetOutsideClickWatch(enabled)),DispatcherPriority.Send);
   }
  }

  void SetOutsideClickWatch(bool enabled){
   watchingOutsideClicks=enabled;
   if(!enabled)TakeDockClickForeground();
  }

  internal IntPtr TakeDockClickForeground(){return Interlocked.Exchange(ref dockMouseDownForeground,IntPtr.Zero);}

  IntPtr OnMouse(int code,IntPtr message,IntPtr data){
   int kind=message.ToInt32();
   if(code==0){
    if(kind==0x20A){
     var point=new Native.POINT{X=Marshal.ReadInt32(data),Y=Marshal.ReadInt32(data,4)};
     Native.RECT bounds;
     if(Native.GetWindowRect(dock,out bounds)&&point.X>=bounds.Left&&point.X<=bounds.Right&&point.Y>=bounds.Top&&point.Y<=bounds.Bottom){
      int mouseData=Marshal.ReadInt32(data,8);
      short delta=unchecked((short)(mouseData>>16));
      if(delta!=0)Native.PostMessage(dock,DockWheelMessage,new IntPtr(delta),IntPtr.Zero);
     }
    }else if(watchingOutsideClicks&&(kind==0x201||kind==0x204||kind==0x207||kind==0x20B)){
     var point=new Native.POINT{X=Marshal.ReadInt32(data),Y=Marshal.ReadInt32(data,4)};
     IntPtr target=WindowFromPoint(point);
     uint pid;
     Native.GetWindowThreadProcessId(target,out pid);
     if(pid!=ownerPid)Native.PostMessage(dock,OutsideClickMessage,IntPtr.Zero,IntPtr.Zero);
     else if(kind==0x201&&GetAncestor(target,2)==dock)
      Interlocked.Exchange(ref dockMouseDownForeground,Native.GetForegroundWindow());
    }
   }
   return CallNextHookEx(IntPtr.Zero,code,message,data);
  }

  public void Dispose(){
   lock(gate){
    disposed=true;
    if(worker!=null)worker.BeginInvokeShutdown(DispatcherPriority.Send);
   }
  }
 }
}
