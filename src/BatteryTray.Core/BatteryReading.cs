namespace BatteryTray.Core;

public enum DeviceKind { Unknown, Mouse, Keyboard, Headset, Earbuds, Controller }

public enum ChargingState { Unknown, NotCharging, Charging }

/// <summary>Parte de un dispositivo con batería propia (auricular izquierdo, estuche, etc.).</summary>
public sealed record BatteryComponent(string Name, int? Level, ChargingState Charging);

/// <summary>Una lectura de batería de un dispositivo.</summary>
public sealed record BatteryReading
{
    /// <summary>Identificador estable entre lecturas (lo usan los alias y la lista de ocultos).</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Nombre del proveedor que generó la lectura.</summary>
    public required string Source { get; init; }

    public DeviceKind Kind { get; init; }

    /// <summary>0-100, o null si no se sabe.</summary>
    public int? Level { get; init; }

    public ChargingState Charging { get; init; }

    public bool Connected { get; init; } = true;

    /// <summary>Momento en que se obtuvo el nivel.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public IReadOnlyList<BatteryComponent> Components { get; init; } = [];

    /// <summary>Aclaración para el usuario (por qué no hay datos, etc.).</summary>
    public string? Note { get; init; }
}
