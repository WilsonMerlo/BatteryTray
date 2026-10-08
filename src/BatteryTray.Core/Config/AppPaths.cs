namespace BatteryTray.Core.Config;

public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BatteryTray");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");

    public static string LogDir => Path.Combine(DataDir, "logs");
}
