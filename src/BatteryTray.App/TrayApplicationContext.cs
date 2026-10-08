using System.Diagnostics;
using BatteryTray.Core;
using BatteryTray.Core.Config;
using Microsoft.Win32;

namespace BatteryTray.App;

/// <summary>
/// La app vive en la bandeja: no hay ventana principal. Este contexto maneja el ícono,
/// el menú, la ventanita, las notificaciones y el monitor de baterías.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    /// <summary>Consulta extra al arrancar, para que los AirPods tengan tiempo de anunciarse.</summary>
    static readonly TimeSpan EarlyRefreshDelay = TimeSpan.FromSeconds(15);

    readonly NotifyIcon _tray;
    readonly DevicePopup _popup;
    readonly ContextMenuStrip _menu;
    readonly SynchronizationContext _ui;
    readonly FileSystemWatcher? _configWatcher;
    readonly System.Windows.Forms.Timer _reloadDebounce = new() { Interval = 800 };
    readonly System.Windows.Forms.Timer _earlyRefresh = new() { Interval = (int)EarlyRefreshDelay.TotalMilliseconds };

    AppConfig _config;
    BatteryMonitor? _monitor;
    IReadOnlyList<BatteryReading> _devices = [];
    DateTimeOffset? _lastUpdate;
    DateTime _ignoreConfigChangesUntil;
    Icon? _icon;
    int _refreshing;

    public TrayApplicationContext()
    {
        _config = ConfigStore.Load(AppPaths.ConfigFile);
        Log.Verbose = _config.VerboseLog;

        // Crear un control instala el SynchronizationContext de WinForms: lo usamos para volver
        // al hilo de la UI desde los eventos del monitor, que llegan desde otros hilos.
        _popup = new DevicePopup();
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _popup.RefreshRequested += RefreshNow;
        _popup.TrayDeviceRequested += SetTrayDevice;
        _popup.HideDeviceRequested += HideDevice;

        _menu = new ContextMenuStrip();
        _menu.Opening += (_, _) => BuildMenu();
        BuildMenu();

        _tray = new NotifyIcon { ContextMenuStrip = _menu, Text = "BatteryTray" };
        _tray.MouseClick += OnTrayClick;
        UpdateTrayIcon();
        _tray.Visible = true;

        _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); ReloadConfig(); };
        _earlyRefresh.Tick += (_, _) => { _earlyRefresh.Stop(); RefreshNow(); };
        _configWatcher = WatchConfig();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        StartMonitor();
        _earlyRefresh.Start();
    }

    // ---------------------------------------------------------------- monitor

    void StartMonitor()
    {
        var monitor = new BatteryMonitor(ProviderFactory.Create(_config), _config);
        monitor.Updated += (_, devices) => _ui.Post(_ => OnUpdated(monitor, devices), null);
        monitor.LowBattery += (_, d) => _ui.Post(_ => OnLowBattery(monitor, d), null);
        _monitor = monitor;

        // Fuera del hilo de la UI: las consultas Bluetooth/HID no deben trabar la interfaz.
        Task.Run(() =>
        {
            monitor.StartProviders();
            monitor.StartPolling();
        });
    }

    void OnUpdated(BatteryMonitor source, IReadOnlyList<BatteryReading> devices)
    {
        if (source != _monitor) return;   // evento atrasado de un monitor ya reemplazado
        _devices = devices;
        _lastUpdate = DateTimeOffset.Now;
        UpdateTrayIcon();
        UpdatePopup();
    }

    void OnLowBattery(BatteryMonitor source, BatteryReading d)
    {
        if (source != _monitor || !_config.Notifications) return;
        // En Windows 10/11 el "balloon" de NotifyIcon se muestra como notificación nativa (toast).
        _tray.ShowBalloonTip(10_000, "Batería baja", $"{d.Name}: {d.Level} %", ToolTipIcon.Warning);
    }

    void RefreshNow()
    {
        var monitor = _monitor;
        if (monitor is null || Interlocked.Exchange(ref _refreshing, 1) == 1) return;
        _popup.SetRefreshing(true);
        Task.Run(async () =>
        {
            try { await monitor.PollAsync(); }
            catch (Exception ex) { Log.Error("Error al actualizar", ex); }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
                _ui.Post(_ => _popup.SetRefreshing(false), null);
            }
        });
    }

    // ---------------------------------------------------------------- ícono

    /// <summary>El dispositivo elegido, o el de nivel más bajo (prefiriendo los conectados).</summary>
    BatteryReading? TrayTarget()
    {
        if (_config.TrayDevice is string id && _devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) is { } pinned)
            return pinned;
        var withLevel = _devices.Where(d => d.Level is not null).ToList();
        return withLevel.Where(d => d.Connected).MinBy(d => d.Level) ?? withLevel.MinBy(d => d.Level);
    }

    void UpdateTrayIcon()
    {
        var target = TrayTarget();
        var newIcon = TrayIconRenderer.Render(target?.Level, target?.Charging == ChargingState.Charging, _config.LowBatteryThreshold);
        _tray.Icon = newIcon;
        _icon?.Dispose();
        _icon = newIcon;
        _tray.Text = Tooltip();
    }

    /// <summary>Windows corta el tooltip en 127 caracteres.</summary>
    string Tooltip()
    {
        if (_devices.Count == 0) return "BatteryTray — buscando dispositivos…";
        var lines = _devices.Select(d =>
            $"{d.Name}: {(d.Level is int l ? $"{l} %" : "—")}{(d.Charging == ChargingState.Charging ? " ⚡" : "")}{(d.Connected ? "" : " (desc.)")}");
        var text = string.Join("\n", lines);
        return text.Length <= 127 ? text : text[..126] + "…";
    }

    void UpdatePopup() =>
        _popup.SetData(_devices, _lastUpdate, _config.LowBatteryThreshold, _config.TrayDevice, _refreshing == 1);

    void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (_popup.Visible || _popup.RecentlyHidden) return;
        UpdatePopup();
        _popup.ShowNearTray();
    }

    // ---------------------------------------------------------------- menú

    void BuildMenu()
    {
        _menu.Items.Clear();

        var show = new ToolStripMenuItem("Mostrar baterías") { Font = new Font(_menu.Font, FontStyle.Bold) };
        show.Click += (_, _) => { UpdatePopup(); _popup.ShowNearTray(); };
        var refresh = new ToolStripMenuItem("Actualizar ahora");
        refresh.Click += (_, _) => RefreshNow();

        var trayDevice = new ToolStripMenuItem("El ícono muestra");
        var lowest = new ToolStripMenuItem("El nivel más bajo") { Checked = _config.TrayDevice is null };
        lowest.Click += (_, _) => SetTrayDevice(null);
        trayDevice.DropDownItems.Add(lowest);
        if (_devices.Count > 0) trayDevice.DropDownItems.Add(new ToolStripSeparator());
        foreach (var d in _devices)
        {
            var item = new ToolStripMenuItem(d.Name) { Checked = string.Equals(_config.TrayDevice, d.Id, StringComparison.OrdinalIgnoreCase) };
            var id = d.Id;
            item.Click += (_, _) => SetTrayDevice(id);
            trayDevice.DropDownItems.Add(item);
        }

        var notify = new ToolStripMenuItem($"Avisar con batería baja (≤ {_config.LowBatteryThreshold} %)") { Checked = _config.Notifications };
        notify.Click += (_, _) =>
        {
            _config.Notifications = !_config.Notifications;
            SaveConfig();
        };

        var autostart = new ToolStripMenuItem("Iniciar con Windows") { Checked = SafeAutoStartEnabled() };
        autostart.Click += (_, _) => ToggleAutoStart(!autostart.Checked);

        var hidden = new ToolStripMenuItem($"Mostrar dispositivos ocultos ({_config.HiddenDevices.Count})") { Enabled = _config.HiddenDevices.Count > 0 };
        hidden.Click += (_, _) =>
        {
            _config.HiddenDevices.Clear();
            SaveConfig();
            RefreshNow();
        };

        var openConfig = new ToolStripMenuItem("Abrir configuración…");
        openConfig.Click += (_, _) => OpenFile(AppPaths.ConfigFile);
        var openLogs = new ToolStripMenuItem("Abrir carpeta de logs");
        openLogs.Click += (_, _) => OpenFile(AppPaths.LogDir);

        var exit = new ToolStripMenuItem("Salir");
        exit.Click += (_, _) => ExitThread();

        _menu.Items.AddRange([
            show, refresh, new ToolStripSeparator(),
            trayDevice, notify, autostart, hidden, new ToolStripSeparator(),
            openConfig, openLogs, new ToolStripSeparator(),
            exit,
        ]);
        MenuTheming.Apply(_menu, Theme.Current);
    }

    void SetTrayDevice(string? id)
    {
        _config.TrayDevice = id;
        SaveConfig();
        UpdateTrayIcon();
        UpdatePopup();
    }

    void HideDevice(BatteryReading d)
    {
        if (!_config.HiddenDevices.Contains(d.Id, StringComparer.OrdinalIgnoreCase))
            _config.HiddenDevices.Add(d.Id);
        if (string.Equals(_config.TrayDevice, d.Id, StringComparison.OrdinalIgnoreCase))
            _config.TrayDevice = null;
        SaveConfig();
        _devices = _devices.Where(x => x.Id != d.Id).ToList();
        UpdateTrayIcon();
        UpdatePopup();
    }

    static bool SafeAutoStartEnabled()
    {
        try { return AutoStart.IsEnabled; }
        catch { return false; }
    }

    void ToggleAutoStart(bool enable)
    {
        try
        {
            AutoStart.Set(enable);
            Log.Info($"Autoarranque {(enable ? "activado" : "desactivado")}");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo cambiar el autoarranque", ex);
            _tray.ShowBalloonTip(5000, "BatteryTray", "No se pudo cambiar el inicio con Windows. Mirá el log.", ToolTipIcon.Error);
        }
    }

    static void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            // Sin programa asociado a .json: el Bloc de notas siempre está.
            Log.Debug($"No hay programa asociado a .json ({ex.Message}), abro con el Bloc de notas");
            Process.Start("notepad.exe", $"\"{path}\"");
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir {path}", ex);
        }
    }

    // ---------------------------------------------------------------- configuración

    void SaveConfig()
    {
        _ignoreConfigChangesUntil = DateTime.Now.AddSeconds(2);
        try { ConfigStore.Save(AppPaths.ConfigFile, _config); }
        catch (Exception ex) { Log.Error("No se pudo guardar la configuración", ex); }
    }

    FileSystemWatcher? WatchConfig()
    {
        try
        {
            var watcher = new FileSystemWatcher(AppPaths.DataDir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => _ui.Post(_ => OnConfigFileChanged(), null);
            watcher.Changed += changed;
            watcher.Created += changed;
            watcher.Renamed += (_, _) => _ui.Post(_ => OnConfigFileChanged(), null);
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex)
        {
            Log.Error("No se puede vigilar el archivo de configuración", ex);
            return null;
        }
    }

    void OnConfigFileChanged()
    {
        if (DateTime.Now < _ignoreConfigChangesUntil) return;   // lo guardamos nosotros
        _reloadDebounce.Stop();
        _reloadDebounce.Start();
    }

    /// <summary>Editaste config.json a mano: se recrea el monitor con los valores nuevos.</summary>
    void ReloadConfig()
    {
        Log.Info("Configuración modificada, recargando");
        _config = ConfigStore.Load(AppPaths.ConfigFile);
        Log.Verbose = _config.VerboseLog;
        _monitor?.Dispose();
        StartMonitor();
        _earlyRefresh.Stop();
        _earlyRefresh.Start();   // los proveedores nuevos necesitan unos segundos (BLE, mandos)
        UpdateTrayIcon();
        UpdatePopup();
    }

    void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // Cambio de tema claro/oscuro, colores, etc.
        _ui.Post(_ =>
        {
            UpdateTrayIcon();
            BuildMenu();
            _popup.Invalidate();
        }, null);
    }

    // ---------------------------------------------------------------- salida

    protected override void ExitThreadCore()
    {
        Log.Info("BatteryTray cerrado");
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _tray.Visible = false;
        _tray.Dispose();
        _monitor?.Dispose();
        _configWatcher?.Dispose();
        _reloadDebounce.Dispose();
        _earlyRefresh.Dispose();
        _popup.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
        base.ExitThreadCore();
    }
}
