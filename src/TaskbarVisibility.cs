using System;
using System.IO;
using System.Web.Script.Serialization;

namespace DesktopSpace {
 internal sealed class TaskbarVisibility : IDisposable {
  const long Layered = 0x80000;
  static readonly string RecoveryPath = Path.Combine(Store.Root, "tray-visibility-recovery.json");
  static readonly string PopupRecoveryPath = Path.Combine(Store.Root, "tray-popup-visibility-recovery.json");
  readonly Snapshot state;
  readonly bool popup;
  bool disposed;

  public sealed class Snapshot {
   public long Window;
   public uint Owner;
   public bool WasLayered;
   public uint Color;
   public byte Alpha;
   public uint Flags;
  }

  private TaskbarVisibility(Snapshot snapshot, bool popup) { state = snapshot; this.popup = popup; }

  internal static TaskbarVisibility TryOpen(IntPtr window) {
   return TryMask(window, false);
  }

  internal static TaskbarVisibility TryMaskPopup(IntPtr window) {
   if (window == IntPtr.Zero || Native.IsWindowVisible(window)) return null;
   return TryMask(window, true);
  }

  static TaskbarVisibility TryMask(IntPtr window, bool popup) {
   var snapshot = new Snapshot { Window = window.ToInt64() };
   if (Native.GetWindowThreadProcessId(window, out snapshot.Owner) == 0) return null;
   long style = Native.GetWindowLongPtr(window, -20).ToInt64();
   snapshot.WasLayered = (style & Layered) != 0;
   if (snapshot.WasLayered && !Native.GetLayeredWindowAttributes(window, out snapshot.Color, out snapshot.Alpha, out snapshot.Flags)) return null;

   var lease = new TaskbarVisibility(snapshot, popup);
   try {
    File.WriteAllText(popup ? PopupRecoveryPath : RecoveryPath, new JavaScriptSerializer().Serialize(snapshot));
    if (!snapshot.WasLayered) Native.SetWindowLongPtr(window, -20, new IntPtr(style | Layered));
    if (!Native.SetLayeredWindowAttributes(window, 0, 0, 2)) {
     lease.Dispose();
     return null;
    }
    if (!popup) Native.ShowWindow(window, 8);
    return lease;
   } catch (Exception error) {
    Store.Error("prepare hidden tray", error);
    lease.Dispose();
    return null;
   }
  }

  internal static void Recover() {
   Recover(PopupRecoveryPath, true);
   Recover(RecoveryPath, false);
  }

  static void Recover(string path, bool popup) {
   try {
    if (!File.Exists(path)) return;
    var snapshot = new JavaScriptSerializer().Deserialize<Snapshot>(File.ReadAllText(path));
    Restore(snapshot, popup, true);
    File.Delete(path);
   } catch (Exception error) { Store.Error("restore taskbar drawing", error); }
  }

  public void Dispose() {
   Finish(false);
  }

  internal void Cancel() {
   Finish(true);
  }

  void Finish(bool hide) {
   if (disposed) return;
   disposed = true;
   try {
    Restore(state, popup, hide);
    File.Delete(popup ? PopupRecoveryPath : RecoveryPath);
   } catch (Exception error) { Store.Error("restore taskbar drawing", error); }
  }

  static void Restore(Snapshot snapshot, bool popup, bool hide) {
   if (snapshot == null) return;
   var window = new IntPtr(snapshot.Window);
   uint owner;
   if (Native.GetWindowThreadProcessId(window, out owner) == 0 || owner != snapshot.Owner) return;
   string windowClass = Native.Class(window);
   if (popup ? !Native.IsTrayOverflowClass(windowClass) : windowClass != "Shell_TrayWnd") return;
   if (!popup || hide) Native.ShowWindow(window, 0);
   if (snapshot.WasLayered) {
    Native.SetLayeredWindowAttributes(window, snapshot.Color, snapshot.Alpha, snapshot.Flags);
   } else {
    long style = Native.GetWindowLongPtr(window, -20).ToInt64();
    Native.SetWindowLongPtr(window, -20, new IntPtr(style & ~Layered));
   }
  }
 }
}
