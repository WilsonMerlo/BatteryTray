namespace BatteryTray.Diag;

static class Errors
{
    /// <summary>
    /// Las excepciones WinRT suelen venir con Message vacío: el dato útil es el HRESULT.
    /// </summary>
    public static string Describe(Exception ex)
    {
        var msg = ex.Message.Trim();
        return $"{ex.GetType().Name} 0x{ex.HResult:X8}" + (msg.Length > 0 ? $": {msg}" : "");
    }
}
