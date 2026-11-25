using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace DesktopSpace {
internal static class ImeReader
{
    static readonly int guiInfoSize=Marshal.SizeOf(typeof(GUIINFO));
    static uint checkedPid;
    static bool shellPopupProcess;
    static bool startMenuProcess;
    static IntPtr checkedWindow;
    static bool shellPopupWindow;
    [StructLayout(LayoutKind.Sequential)] internal struct GUIINFO
    {
        public int cbSize, flags;
        public IntPtr active, focus, capture, menuOwner, moveSize, caret;
        public Native.RECT caretRect;
    }
    [DllImport("user32.dll")] internal static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread, ref GUIINFO info);
    [DllImport("imm32.dll")] internal static extern IntPtr ImmGetDefaultIMEWnd(IntPtr window);
    internal struct State
    {
        internal string Mode;
        internal bool KeepPrevious;
        internal bool CanToggle;
        internal IntPtr Focus;
    }
    internal static State Read(IntPtr foreground,uint thread,uint pid,uint messageTimeout)
    {
        State state = new State{Focus=foreground};
        if(checkedPid!=pid) {
            checkedPid=pid; checkedWindow=IntPtr.Zero; shellPopupProcess=false; startMenuProcess=false;
            try { using(Process p=Process.GetProcessById((int)pid)) {
                string popupProcessName=p.ProcessName;
                shellPopupProcess=popupProcessName=="ShellExperienceHost" || popupProcessName=="Nexus";
                startMenuProcess=popupProcessName=="StartMenuExperienceHost";
            }} catch { }
        }
        if(shellPopupProcess){state.KeepPrevious=true;return state;}
        if(checkedWindow!=foreground){
            checkedWindow=foreground;
            string cls=Native.Class(foreground);
            shellPopupWindow=cls.StartsWith("ThunderRT6", StringComparison.Ordinal) || cls == "Shell_TrayWnd" ||
                cls == "Shell_SecondaryTrayWnd" ||
                cls == "#32768" || cls == "tooltips_class32" || cls == "TaskListThumbnailWnd" ||
                cls == "XamlExplorerHostIslandWindow" || cls == "ControlCenterWindow" ||
                cls.IndexOf("Overflow", StringComparison.OrdinalIgnoreCase)>=0;
        }
        if(shellPopupWindow){state.KeepPrevious=true;return state;}
        int primary=(int)(GetKeyboardLayout(thread).ToInt64()&0x3ff);
        if(primary==9){state.Mode="英";return state;}
        if(primary!=4)return state;
        GUIINFO gui = new GUIINFO();
        gui.cbSize = guiInfoSize;
        IntPtr focus=foreground;
        if (GetGUIThreadInfo(thread, ref gui) && gui.focus!=IntPtr.Zero) {
            focus=gui.focus;
        }
        state.Focus=focus;
        IntPtr ime=ImmGetDefaultIMEWnd(focus);
        if (ime==IntPtr.Zero) ime=ImmGetDefaultIMEWnd(foreground);
        if (ime==IntPtr.Zero) return state;
        UIntPtr open, conversion;
        if (Native.SendMessageTimeout(ime, 0x0283, (UIntPtr)5, IntPtr.Zero, 2, messageTimeout, out open)==IntPtr.Zero) return state;
        if(open==UIntPtr.Zero){
            // Start's pinned-app view has no active IME. The search field is a separate window.
            if(startMenuProcess)state.KeepPrevious=true;
            else state.Mode="英";
            return state;
        }
        if (Native.SendMessageTimeout(ime, 0x0283, (UIntPtr)1, IntPtr.Zero, 2, messageTimeout, out conversion)==IntPtr.Zero) return state;
        state.Mode = (conversion.ToUInt64()&1)!=0?"中":"英";
        state.CanToggle=true;
        return state;
    }

    internal static bool TryToggle(IntPtr foreground,uint thread,IntPtr focus)
    {
        var gui=new GUIINFO{cbSize=guiInfoSize};
        if(!GetGUIThreadInfo(thread,ref gui)||gui.focus!=focus||foreground!=Native.GetForegroundWindow())return false;
        return Native.TryTapShift();
    }
}


}
