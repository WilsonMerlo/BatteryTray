using System.Runtime.InteropServices;

namespace BatteryTray.App;

internal static class NativeMethods
{
    const int DwmwaWindowCornerPreference = 33;
    const int DwmwcpRound = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr handle);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Esquinas redondeadas de Windows 11 (en Windows 10 no hace nada).</summary>
    public static void SetRoundedCorners(IntPtr hwnd)
    {
        var preference = DwmwcpRound;
        try { DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int)); }
        catch { /* Windows 10 o DWM no disponible */ }
    }
}
