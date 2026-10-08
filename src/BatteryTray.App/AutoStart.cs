using Microsoft.Win32;

namespace BatteryTray.App;

/// <summary>
/// Autoarranque con la clave Run del usuario (HKCU): no necesita permisos de administrador
/// y aparece en Configuración > Aplicaciones > Inicio, donde también se puede desactivar.
/// </summary>
internal static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "BatteryTray";

    static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                   && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
