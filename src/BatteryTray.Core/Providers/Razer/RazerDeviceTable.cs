namespace BatteryTray.Core.Providers.Razer;

/// <param name="Model">Nombre del modelo (solo para el log; en pantalla se usa el nombre que reporta el USB).</param>
/// <param name="TransactionId">Transaction ID que espera el modelo.</param>
/// <param name="HasCharging">false para los que usan pilas (no informan carga).</param>
/// <param name="WaitMs">Espera entre enviar el pedido y leer la respuesta.</param>
internal sealed record RazerDeviceInfo(string Model, byte TransactionId, bool HasCharging = true, int WaitMs = RazerDeviceTable.DefaultWaitMs);

/// <summary>
/// Modelos Razer con batería, tomados de OpenRazer (driver/razermouse_driver.c,
/// funciones razer_attr_read_charge_level / razer_attr_read_charge_status).
/// La espera por defecto es la de los receptores Viper (RAZER_VIPER_MOUSE_RECEIVER_WAIT_US = 59,9 ms),
/// que es la más larga de las habituales: sirve para todos salvo el Atheris.
/// </summary>
internal static class RazerDeviceTable
{
    public const int DefaultWaitMs = 60;

    /// <summary>Transaction IDs a probar con modelos desconocidos, del más común al menos común.</summary>
    public static readonly byte[] ProbeTransactionIds = [0x1F, 0x3F, 0xFF, 0x9F];

    public static readonly IReadOnlyDictionary<int, RazerDeviceInfo> Known = Build();

    static Dictionary<int, RazerDeviceInfo> Build()
    {
        var t = new Dictionary<int, RazerDeviceInfo>();
        void Add(byte tid, string model, params int[] pids)
        {
            foreach (var pid in pids) t[pid] = new RazerDeviceInfo(model, tid);
        }

        // --- transaction id 0x1F ---
        Add(0x1F, "Naga Pro", 0x008F, 0x0090);
        Add(0x1F, "Basilisk Ultimate", 0x0086, 0x0088);
        Add(0x1F, "Lancehead Wireless", 0x006F, 0x0070);
        Add(0x1F, "Atheris", 0x0062);
        Add(0x1F, "Orochi V2", 0x0094, 0x0095);
        Add(0x1F, "Pro Click", 0x0077, 0x0080);
        Add(0x1F, "DeathAdder V2 X HyperSpeed", 0x009C);
        Add(0x1F, "Viper V2 Pro", 0x00A5, 0x00A6);
        Add(0x1F, "Viper Mini SE", 0x009E, 0x009F);
        Add(0x1F, "Cobra HyperSpeed", 0x00DA, 0x00DB);
        Add(0x1F, "Cobra Pro", 0x00AF, 0x00B0);
        Add(0x1F, "DeathAdder V3 Pro", 0x00B6, 0x00B7, 0x00C2, 0x00C3);
        Add(0x1F, "DeathAdder V3 HyperSpeed", 0x00C4, 0x00C5);
        Add(0x1F, "HyperPolling Wireless Dongle", 0x00B3);
        Add(0x1F, "Basilisk V3 Pro", 0x00AA, 0x00AB);
        Add(0x1F, "Basilisk V3 35K", 0x00CB);
        Add(0x1F, "Basilisk V3 Pro 35K", 0x00CC, 0x00CD, 0x00D6, 0x00D7);
        Add(0x1F, "Pro Click Mini", 0x009A);
        Add(0x1F, "Naga V2 Pro", 0x00A7, 0x00A8);
        Add(0x1F, "Naga V2 HyperSpeed", 0x00B4);
        Add(0x1F, "Viper V3 HyperSpeed", 0x00B8);
        Add(0x1F, "Basilisk V3 X HyperSpeed", 0x00B9);
        Add(0x1F, "Basilisk Mobile", 0x00D3, 0x00D4);
        Add(0x1F, "DeathAdder V4 Pro", 0x00BE, 0x00BF);
        Add(0x1F, "Viper V3 Pro", 0x00C0, 0x00C1);
        Add(0x1F, "Viper V3 Pro SE", 0x00DE, 0x00DF);
        Add(0x1F, "Pro Click V2 Vertical", 0x00C7, 0x00C8);
        Add(0x1F, "Pro Click V2", 0x00D0, 0x00D1);

        // --- transaction id 0x3F ---
        Add(0x3F, "Lancehead", 0x0059, 0x005A);
        Add(0x3F, "Mamba Wireless", 0x0072, 0x0073);
        Add(0x3F, "DeathAdder V2 Pro", 0x007C, 0x007D);

        // --- transaction id 0xFF ---
        Add(0xFF, "Naga Epic", 0x001F);
        Add(0xFF, "Mamba 2012", 0x0024, 0x0025);
        Add(0xFF, "Ouroboros", 0x0032);
        Add(0xFF, "Naga Epic Chroma", 0x003E, 0x003F);
        Add(0xFF, "Mamba", 0x0044, 0x0045);
        Add(0xFF, "Viper Ultimate", 0x007A, 0x007B);
        Add(0xFF, "Basilisk X HyperSpeed", 0x0083);

        // Modelos a pilas: no informan estado de carga.
        foreach (var pid in new[] { 0x0062, 0x0083, 0x0094, 0x0095, 0x009C, 0x00B4, 0x00B8, 0x00B9, 0x00D3, 0x00D4 })
            t[pid] = t[pid] with { HasCharging = false };

        // El receptor del Atheris es lento (RAZER_ATHERIS_RECEIVER_WAIT_US = 400 ms).
        t[0x0062] = t[0x0062] with { WaitMs = 400 };

        return t;
    }
}
