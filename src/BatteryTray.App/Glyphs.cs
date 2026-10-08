using System.Drawing.Text;
using BatteryTray.Core;

namespace BatteryTray.App;

/// <summary>Íconos de la fuente de símbolos de Windows (Segoe Fluent Icons en 11, MDL2 en 10).</summary>
internal static class Glyphs
{
    public const string Refresh = "";
    public const string Bolt = "";
    public const string Pin = "";

    public static readonly string FontName = Resolve();

    public static string ForKind(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => "",
        DeviceKind.Keyboard => "",
        DeviceKind.Headset => "",
        DeviceKind.Earbuds => "",
        DeviceKind.Controller => "",
        _ => "",
    };

    static string Resolve()
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
    }
}
