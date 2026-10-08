using System.Text;

namespace BatteryTray.Diag;

/// <summary>
/// Escribe el reporte a la consola y a un archivo de texto a la vez.
/// Thread-safe porque los eventos de Bluetooth llegan desde otros hilos.
/// </summary>
sealed class Output : IDisposable
{
    readonly StreamWriter _file;
    readonly object _lock = new();

    public string FilePath { get; }

    public Output(string filePath)
    {
        FilePath = filePath;
        _file = new StreamWriter(filePath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Line(string text = "")
    {
        lock (_lock)
        {
            Console.WriteLine(text);
            _file.WriteLine(text);
        }
    }

    /// <summary>Solo a consola (progreso, cuentas regresivas), no al archivo.</summary>
    public void Status(string text)
    {
        lock (_lock) Console.WriteLine(text);
    }

    public void Section(string title)
    {
        Line();
        Line(new string('=', 90));
        Line(title);
        Line(new string('=', 90));
    }

    public void Dispose() => _file.Dispose();
}
