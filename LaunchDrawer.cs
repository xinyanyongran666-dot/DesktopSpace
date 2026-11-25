using System;
using System.Diagnostics;
using System.IO;

internal static class LaunchDrawer
{
    [STAThread]
    private static void Main()
    {
        string program = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopSpace.exe");
        Process.Start(new ProcessStartInfo(program, "--launcher-only") { UseShellExecute = false });
    }
}
