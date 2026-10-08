namespace BatteryTray.Core;

/// <summary>
/// Log simple a archivo: un archivo por día en %AppData%\BatteryTray\logs, se borran los de más de 7 días.
/// </summary>
public static class Log
{
    static readonly object Lock = new();
    static string? _dir;

    /// <summary>Si es true, también escribe en la consola (stderr).</summary>
    public static bool ToConsole { get; set; }

    /// <summary>Si es true, registra también los mensajes de depuración.</summary>
    public static bool Verbose { get; set; }

    public static void Init(string directory)
    {
        _dir = directory;
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var f in Directory.EnumerateFiles(directory, "batterytray-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7))
                    File.Delete(f);
        }
        catch
        {
            // Si no se puede limpiar, no es grave.
        }
    }

    public static void Debug(string message)
    {
        if (Verbose) Write("DBG", message);
    }

    public static void Info(string message) => Write("INF", message);

    public static void Warn(string message) => Write("WRN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERR", ex is null ? message : $"{message}: {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Lock)
        {
            if (ToConsole) Console.Error.WriteLine(line);
            if (_dir is null) return;
            try
            {
                File.AppendAllText(Path.Combine(_dir, $"batterytray-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
            }
            catch
            {
                // El log nunca debe tirar la app.
            }
        }
    }
}
