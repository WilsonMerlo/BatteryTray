namespace BatteryTray.Core;

/// <summary>
/// Fuente de lecturas de batería. Cada proveedor cubre una familia de dispositivos
/// (Razer, Apple, mandos, Bluetooth genérico) y devuelve todos los que encuentre.
/// </summary>
public interface IBatteryProvider : IDisposable
{
    string Name { get; }

    /// <summary>Arranca recursos de larga vida (watchers BLE, etc.). Se llama una sola vez.</summary>
    void Start() { }

    /// <summary>
    /// Devuelve los dispositivos presentes. Una lista vacía significa "no hay ninguno";
    /// si algo sale mal, tirar excepción: el monitor la registra y conserva las lecturas anteriores.
    /// </summary>
    Task<IReadOnlyList<BatteryReading>> QueryAsync(CancellationToken ct);
}
