using System.Drawing.Drawing2D;
using BatteryTray.Core;

namespace BatteryTray.App;

/// <summary>
/// Ventanita que aparece al hacer clic en el ícono: lista de dispositivos con nivel, barra,
/// estado de carga y antigüedad de la lectura. Se dibuja a mano (GDI+) para que se vea como
/// los flyouts de Windows 11; se cierra al perder el foco o con Esc.
/// </summary>
internal sealed class DevicePopup : Form
{
    // Medidas en píxeles a 96 DPI; se escalan con Px().
    const int PopupWidth = 360;
    const int Pad = 16;
    const int TopPadding = 8;
    const int RowHeight = 64;
    const int ExtraLineHeight = 18;
    const int EmptyHeight = 64;
    const int FooterHeight = 40;

    readonly List<(Rectangle Bounds, BatteryReading Device)> _rowHits = [];
    readonly ContextMenuStrip _rowMenu = new();
    readonly System.Windows.Forms.Timer _clock = new() { Interval = 30_000 };

    IReadOnlyList<BatteryReading> _devices = [];
    DateTimeOffset? _updated;
    int _threshold = 20;
    string? _trayDeviceId;
    bool _refreshing;
    Rectangle _refreshHit;
    bool _refreshHover;
    int _hoverRow = -1;
    DateTime _lastHidden;

    public event Action? RefreshRequested;
    public event Action<string?>? TrayDeviceRequested;
    public event Action<BatteryReading>? HideDeviceRequested;

    public DevicePopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Text = "BatteryTray";
        _clock.Tick += (_, _) => Invalidate();   // actualiza los "hace X min"
    }

    /// <summary>true justo después de cerrarse: evita que el mismo clic en el ícono la vuelva a abrir.</summary>
    public bool RecentlyHidden => (DateTime.Now - _lastHidden).TotalMilliseconds < 300;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x80;
            const int CsDropShadow = 0x20000;
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow;
            cp.ClassStyle |= CsDropShadow;
            return cp;
        }
    }

    public void SetData(IReadOnlyList<BatteryReading> devices, DateTimeOffset? updated, int threshold, string? trayDeviceId, bool refreshing)
    {
        _devices = devices;
        _updated = updated;
        _threshold = threshold;
        _trayDeviceId = trayDeviceId;
        _refreshing = refreshing;
        if (Visible) ResizeKeepingAnchor();
        Invalidate();
    }

    public void SetRefreshing(bool refreshing)
    {
        _refreshing = refreshing;
        Invalidate();
    }

    public void ShowNearTray()
    {
        Size = new Size(Px(PopupWidth), Px(ContentHeight()));
        Location = ComputeLocation(Cursor.Position);
        Show();
        Activate();
        _clock.Start();
    }

    // ---------------------------------------------------------------- posición

    Point ComputeLocation(Point anchor)
    {
        var screen = Screen.FromPoint(anchor);
        var wa = screen.WorkingArea;
        var b = screen.Bounds;
        var margin = Px(12);

        var x = Math.Clamp(anchor.X - Width / 2, wa.Left + margin, wa.Right - Width - margin);
        var y = Math.Clamp(anchor.Y - Height - margin, wa.Top + margin, wa.Bottom - Height - margin);

        if (wa.Bottom < b.Bottom) y = wa.Bottom - Height - margin;          // barra abajo (lo normal)
        else if (wa.Top > b.Top) y = wa.Top + margin;                       // barra arriba
        if (wa.Right < b.Right) x = wa.Right - Width - margin;              // barra a la derecha
        else if (wa.Left > b.Left) x = wa.Left + margin;                    // barra a la izquierda

        return new Point(x, y);
    }

    /// <summary>Si cambia la cantidad de dispositivos con la ventana abierta, crece hacia arriba.</summary>
    void ResizeKeepingAnchor()
    {
        var newHeight = Px(ContentHeight());
        if (newHeight == Height) return;
        var bottom = Bottom;
        Height = newHeight;
        Top = bottom - newHeight;
    }

    // ---------------------------------------------------------------- eventos

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetRoundedCorners(Handle);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HidePopup();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) HidePopup();
        if (e.KeyCode == Keys.F5) RefreshRequested?.Invoke();
    }

    void HidePopup()
    {
        if (!Visible) return;
        Hide();
        _clock.Stop();
        _lastHidden = DateTime.Now;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Size = new Size(Px(PopupWidth), Px(ContentHeight()));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var refreshHover = _refreshHit.Contains(e.Location);
        var row = _rowHits.FindIndex(r => r.Bounds.Contains(e.Location));
        if (refreshHover != _refreshHover || row != _hoverRow)
        {
            _refreshHover = refreshHover;
            _hoverRow = row;
            Cursor = refreshHover ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _refreshHover = false;
        _hoverRow = -1;
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && _refreshHit.Contains(e.Location))
        {
            RefreshRequested?.Invoke();
            return;
        }

        var hit = _rowHits.FindIndex(r => r.Bounds.Contains(e.Location));
        if (hit < 0) return;
        if (e.Button == MouseButtons.Right || e.Button == MouseButtons.Left)
            ShowRowMenu(_rowHits[hit].Device, e.Location);
    }

    void ShowRowMenu(BatteryReading device, Point location)
    {
        _rowMenu.Items.Clear();
        var pinned = string.Equals(_trayDeviceId, device.Id, StringComparison.OrdinalIgnoreCase);

        var pin = new ToolStripMenuItem("Mostrar este en el ícono") { Checked = pinned };
        pin.Click += (_, _) => TrayDeviceRequested?.Invoke(device.Id);
        var lowest = new ToolStripMenuItem("Mostrar el más bajo en el ícono") { Checked = _trayDeviceId is null };
        lowest.Click += (_, _) => TrayDeviceRequested?.Invoke(null);
        var hide = new ToolStripMenuItem("Ocultar este dispositivo");
        hide.Click += (_, _) => HideDeviceRequested?.Invoke(device);
        var id = new ToolStripMenuItem($"Copiar id ({device.Id})");
        id.Click += (_, _) => Clipboard.SetText(device.Id);

        _rowMenu.Items.AddRange([pin, lowest, new ToolStripSeparator(), hide, id]);
        MenuTheming.Apply(_rowMenu, Theme.Current);
        _rowMenu.Show(this, location);
    }

    // ---------------------------------------------------------------- dibujo

    protected override void OnPaint(PaintEventArgs e) => PaintContent(e.Graphics, Theme.Current);

    /// <summary>Imagen de la ventana sin mostrarla (la usa el modo --preview).</summary>
    public Bitmap Render(Theme theme)
    {
        // Opaco (24 bits): ClearType necesita un fondo sin transparencia, como el de la ventana real.
        var bitmap = new Bitmap(Px(PopupWidth), Px(ContentHeight()), System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        PaintContent(g, theme);
        return bitmap;
    }

    int Px(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    float DpiScale => DeviceDpi / 96f;

    static bool HasExtraLine(BatteryReading d) => d.Components.Count > 0 || d.Note is not null;

    int ContentHeight()
    {
        var rows = _devices.Count == 0 ? EmptyHeight : _devices.Sum(d => RowHeight + (HasExtraLine(d) ? ExtraLineHeight : 0));
        return TopPadding + rows + FooterHeight;
    }

    void PaintContent(Graphics g, Theme t)
    {
        var s = DpiScale;
        var width = Px(PopupWidth);
        var height = Px(ContentHeight());
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(t.Background);

        using var nameFont = new Font("Segoe UI Semibold", 14 * s, GraphicsUnit.Pixel);
        using var smallFont = new Font("Segoe UI", 12 * s, GraphicsUnit.Pixel);
        using var glyphFont = new Font(Glyphs.FontName, 16 * s, GraphicsUnit.Pixel);
        using var smallGlyphFont = new Font(Glyphs.FontName, 12 * s, GraphicsUnit.Pixel);

        // --- filas
        var y = Px(TopPadding);
        _rowHits.Clear();
        if (_devices.Count == 0)
        {
            var empty = new RectangleF(0, y, width, Px(EmptyHeight));
            DrawText(g, _updated is null ? "Buscando dispositivos…" : "No se encontraron dispositivos con batería",
                smallFont, t.SecondaryText, empty, StringAlignment.Center, StringAlignment.Center);
            y += Px(EmptyHeight);
        }

        for (var i = 0; i < _devices.Count; i++)
        {
            var d = _devices[i];
            var rowHeight = Px(RowHeight + (HasExtraLine(d) ? ExtraLineHeight : 0));
            var row = new Rectangle(Px(6), y, width - Px(12), rowHeight);
            _rowHits.Add((row, d));
            if (i == _hoverRow) FillRounded(g, t.Hover, row, 6 * s);
            PaintRow(g, t, d, y, width, s, nameFont, smallFont, glyphFont, smallGlyphFont);
            y += rowHeight;
        }

        // --- pie
        using (var pen = new Pen(t.Border))
            g.DrawLine(pen, Px(Pad), y, width - Px(Pad), y);
        var footerRect = new RectangleF(Px(Pad), y, width - 2 * Px(Pad), Px(FooterHeight));
        DrawText(g, "Clic en un dispositivo: opciones", smallFont, t.SecondaryText, footerRect, StringAlignment.Near, StringAlignment.Center);

        // Botón de actualizar, a la derecha del pie (azul mientras consulta).
        _refreshHit = new Rectangle(width - Px(Pad) - Px(28), y + (Px(FooterHeight) - Px(28)) / 2, Px(28), Px(28));
        if (_refreshHover) FillRounded(g, t.Surface, _refreshHit, 4 * s);
        DrawText(g, Glyphs.Refresh, smallGlyphFont, _refreshing ? t.Accent : t.SecondaryText, _refreshHit, StringAlignment.Center, StringAlignment.Center);

        using (var border = new Pen(t.Border))
            g.DrawRectangle(border, 0, 0, width - 1, height - 1);
    }

    void PaintRow(Graphics g, Theme t, BatteryReading d, int y, int width, float s,
        Font nameFont, Font smallFont, Font glyphFont, Font smallGlyphFont)
    {
        var primary = d.Connected ? t.Text : t.SecondaryText;

        // Ícono del tipo de dispositivo dentro de un círculo.
        var circle = new Rectangle(Px(Pad), y + Px(12), Px(36), Px(36));
        using (var surface = new SolidBrush(t.Surface))
            g.FillEllipse(surface, circle);
        DrawText(g, Glyphs.ForKind(d.Kind), glyphFont, primary, circle, StringAlignment.Center, StringAlignment.Center);

        float textX = circle.Right + Px(12);
        float right = width - Px(Pad);
        var lineTop = y + Px(9);

        // Nivel a la derecha, con un rayo si está cargando.
        var levelText = d.Level is int l ? $"{l} %" : "—";
        var levelX = right - Measure(g, levelText, nameFont).Width;
        DrawText(g, levelText, nameFont, primary, new RectangleF(levelX, lineTop, right - levelX + Px(2), Px(20)));
        if (d.Charging == ChargingState.Charging && d.Level is not null)
        {
            levelX -= Measure(g, Glyphs.Bolt, smallGlyphFont).Width + Px(4);
            DrawText(g, Glyphs.Bolt, smallGlyphFont, t.Accent, new RectangleF(levelX, lineTop + Px(3), Px(16), Px(16)));
        }

        // Nombre (con un pin si es el que muestra el ícono).
        var nameRight = levelX - Px(8);
        if (string.Equals(_trayDeviceId, d.Id, StringComparison.OrdinalIgnoreCase))
        {
            nameRight -= Measure(g, Glyphs.Pin, smallGlyphFont).Width + Px(4);
            DrawText(g, Glyphs.Pin, smallGlyphFont, t.SecondaryText, new RectangleF(nameRight + Px(4), lineTop + Px(3), Px(16), Px(16)));
        }
        DrawText(g, d.Name, nameFont, primary, new RectangleF(textX, lineTop, nameRight - textX, Px(20)));

        // Barra de nivel.
        var bar = new RectangleF(textX, y + Px(34), right - textX, 4 * s);
        FillRounded(g, t.Track, bar, 2 * s);
        if (d.Level is int level && level > 0)
        {
            var fill = bar with { Width = Math.Max(bar.Height, bar.Width * level / 100f) };
            FillRounded(g, d.Connected ? t.LevelColor(level, _threshold) : t.SecondaryText, fill, 2 * s);
        }

        // Estado.
        DrawText(g, StatusText(d), smallFont, t.SecondaryText, new RectangleF(textX, y + Px(42), right - textX, Px(16)));

        // Línea extra: partes (AirPods) o aclaración.
        if (HasExtraLine(d))
        {
            var extra = d.Components.Count > 0
                ? string.Join("  ·  ", d.Components.Select(c =>
                    $"{Short(c.Name)} {(c.Level is int cl ? $"{cl} %" : "—")}{(c.Charging == ChargingState.Charging ? " (cargando)" : "")}"))
                : d.Note!;
            DrawText(g, extra, smallFont, t.SecondaryText, new RectangleF(textX, y + Px(60), right - textX, Px(16)));
        }
    }

    /// <summary>
    /// Texto con GDI+ (no TextRenderer): se ve igual en la ventana y en la vista previa,
    /// en una sola línea y con "…" si no entra.
    /// </summary>
    static void DrawText(Graphics g, string text, Font font, Color color, RectangleF bounds,
        StringAlignment horizontal = StringAlignment.Near, StringAlignment vertical = StringAlignment.Near)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        format.FormatFlags |= StringFormatFlags.NoWrap;
        g.DrawString(text, font, brush, bounds, format);
    }

    static SizeF Measure(Graphics g, string text, Font font) =>
        g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);

    static string StatusText(BatteryReading d)
    {
        var parts = new List<string>();
        if (!d.Connected) parts.Add("Desconectado");
        else if (d.Charging == ChargingState.Charging) parts.Add("Cargando");
        else parts.Add("Conectado");

        if (d.Level is not null) parts.Add(d.Connected ? Ago(d.Timestamp) : $"última lectura {Ago(d.Timestamp)}");
        return string.Join(" · ", parts);
    }

    static string Short(string component) => component switch
    {
        "Izquierdo" => "Izq.",
        "Derecho" => "Der.",
        _ => component,
    };

    internal static string Ago(DateTimeOffset t)
    {
        var elapsed = DateTimeOffset.Now - t;
        if (elapsed < TimeSpan.FromMinutes(1)) return "recién";
        if (elapsed < TimeSpan.FromHours(1)) return $"hace {(int)elapsed.TotalMinutes} min";
        if (elapsed < TimeSpan.FromDays(1)) return $"hace {(int)elapsed.TotalHours} h";
        return t.ToLocalTime().ToString("dd/MM HH:mm");
    }

    static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = TrayIconRenderer.RoundedRect(r, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clock.Dispose();
            _rowMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
