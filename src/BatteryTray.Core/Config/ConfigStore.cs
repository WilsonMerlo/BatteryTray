using System.Text.Json;

namespace BatteryTray.Core.Config;

public static class ConfigStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Carga la configuración. Si el archivo no existe lo crea con los valores por defecto.
    /// Si está roto, usa los valores por defecto pero NO lo pisa, para no perder lo que escribiste.
    /// </summary>
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var fresh = new AppConfig();
            Save(path, fresh);
            Log.Info($"Configuración creada con valores por defecto: {path}");
            return fresh;
        }

        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options) ?? new AppConfig();
            Log.Info($"Configuración cargada: {path}");
            return config;
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo leer {path}, uso valores por defecto", ex);
            return new AppConfig();
        }
    }

    public static void Save(string path, AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
