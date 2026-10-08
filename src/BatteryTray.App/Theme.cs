using Microsoft.Win32;

namespace BatteryTray.App;

/// <summary>Colores claro/oscuro al estilo de Windows 11 (paleta Fluent).</summary>
internal sealed record Theme(
    Color Background,
    Color Surface,
    Color Border,
    Color Text,
    Color SecondaryText,
    Color Track,
    Color Hover,
    Color Accent,
    Color Good,
    Color Warning,
    Color Critical)
{
    public static readonly Theme Dark = new(
        Background: Color.FromArgb(0x20, 0x20, 0x20),
        Surface: Color.FromArgb(0x2D, 0x2D, 0x2D),
        Border: Color.FromArgb(0x3A, 0x3A, 0x3A),
        Text: Color.White,
        SecondaryText: Color.FromArgb(0xA6, 0xA6, 0xA6),
        Track: Color.FromArgb(0x3D, 0x3D, 0x3D),
        Hover: Color.FromArgb(0x2B, 0x2B, 0x2B),
        Accent: Color.FromArgb(0x4C, 0xC2, 0xFF),
        Good: Color.FromArgb(0x6C, 0xCB, 0x5F),
        Warning: Color.FromArgb(0xFC, 0xE1, 0x00),
        Critical: Color.FromArgb(0xFF, 0x99, 0xA4));

    public static readonly Theme Light = new(
        Background: Color.FromArgb(0xF9, 0xF9, 0xF9),
        Surface: Color.FromArgb(0xEA, 0xEA, 0xEA),
        Border: Color.FromArgb(0xD5, 0xD5, 0xD5),
        Text: Color.FromArgb(0x1A, 0x1A, 0x1A),
        SecondaryText: Color.FromArgb(0x5F, 0x5F, 0x5F),
        Track: Color.FromArgb(0xDA, 0xDA, 0xDA),
        Hover: Color.FromArgb(0xF0, 0xF0, 0xF0),
        Accent: Color.FromArgb(0x00, 0x5F, 0xB8),
        Good: Color.FromArgb(0x0F, 0x7B, 0x0F),
        Warning: Color.FromArgb(0x9D, 0x5D, 0x00),
        Critical: Color.FromArgb(0xC4, 0x2B, 0x1C));

    /// <summary>Tema de las apps según Configuración > Personalización > Colores.</summary>
    public static Theme Current => ReadPersonalize("AppsUseLightTheme") == 0 ? Dark : Light;

    /// <summary>La barra de tareas puede tener un tema distinto al de las apps.</summary>
    public static bool TaskbarIsDark => ReadPersonalize("SystemUsesLightTheme") == 0;

    public Color LevelColor(int? level, int threshold) => level switch
    {
        null => SecondaryText,
        _ when level <= threshold => Critical,
        <= 50 => Warning,
        _ => Good,
    };

    static int ReadPersonalize(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue(name) is int v ? v : 1;
        }
        catch
        {
            return 1;
        }
    }
}
