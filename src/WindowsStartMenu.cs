using System;
using System.Runtime.InteropServices;

namespace DesktopSpace {
 internal static class WindowsStartMenu {
  static readonly Guid ImmersiveShell = new Guid("c2f03a33-21f5-47fa-b4bb-156362a2f239");
  static readonly Guid LauncherService = new Guid("6f86e01c-c649-4d61-be23-f1322ddeca9d");

  // The Windows 10/11 immersive launcher ABI is also used by RetroBar's StartMenuMonitor.
  [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IShellServices {
   [PreserveSig]
   int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object value);
  }

  [ComImport, Guid("d8d60399-a0f1-f987-5551-321fd1b49864"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IStartLauncher {
   [PreserveSig]
   int ShowStartView(int method, int flags);
   [PreserveSig]
   int Dismiss(int method);
   [PreserveSig]
   int Dismiss2(int method);
   [PreserveSig]
   int DismissSynchronouslyWithoutTransition();
   [PreserveSig]
   int IsVisible(out int visible);
  }

  internal static void Toggle() { ChangeVisibility(true); }

  internal static bool DismissIfVisible() { return ChangeVisibility(false); }

  static bool ChangeVisibility(bool toggle) {
   object shell = Activator.CreateInstance(Type.GetTypeFromCLSID(ImmersiveShell));
   object launcher = null;
   try {
    Guid service = LauncherService;
    Guid iid = typeof(IStartLauncher).GUID;
    Marshal.ThrowExceptionForHR(((IShellServices)shell).QueryService(ref service, ref iid, out launcher));
    var start = (IStartLauncher)launcher;
    int visible;
    Marshal.ThrowExceptionForHR(start.IsVisible(out visible));
    if (visible != 0) {
     const int dismissFromStartButton = 0x6;
     Marshal.ThrowExceptionForHR(toggle?start.Dismiss(dismissFromStartButton):start.DismissSynchronouslyWithoutTransition());
     return true;
    }
    if (!toggle) return false;
    IntPtr desktop = Native.FindWindow("Progman", null);
    uint explorer;
    if (desktop != IntPtr.Zero && Native.GetWindowThreadProcessId(desktop, out explorer) != 0)
     Native.AllowSetForegroundWindow(explorer);
    const int startButton = 0xB;
    const int ignoreForegroundError = 0x4;
    Marshal.ThrowExceptionForHR(start.ShowStartView(startButton, ignoreForegroundError));
    return false;
   } finally {
    if (launcher != null) Marshal.ReleaseComObject(launcher);
    Marshal.ReleaseComObject(shell);
   }
  }
 }
}
