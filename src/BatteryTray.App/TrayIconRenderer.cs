using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace BatteryTray.App;

/// <summary>
/// Dibuja el ícono de la bandeja: una "pastilla" del color del nivel con el número adentro.
/// Se dibuja en tiempo de ejecución al tamaño exacto que pide Windows según la escala (16 px al 100 %,
/// 24 px al 150 %, etc.) para que el número se lea nítido.
/// </summary>
internal static class TrayIconRenderer
{
    public static Icon Render(int? level, bool charging, int threshold)
    {
        var size = SystemInformation.SmallIconSize.Width;
        using var bitmap = RenderBitmap(size, level, charging, threshold, Theme.TaskbarIsDark);
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public static Bitmap RenderBitmap(int size, int? level, bool charging, int threshold, bool darkTaskbar)
    {
        var bitmap = new Bitmap(size, size);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        // Colores fijos (no los del tema de la app) para que contrasten en cualquier barra de tareas.
        var fill = level switch
        {
            null => darkTaskbar ? Color.FromArgb(0x60, 0x60, 0x60) : Color.FromArgb(0x8A, 0x8A, 0x8A),
            _ when charging => Color.FromArgb(0x00, 0x78, 0xD4),
            _ when level <= threshold => Color.FromArgb(0xD1, 0x34, 0x38),
            <= 50 => Color.FromArgb(0xF7, 0xB5, 0x00),
            _ => Color.FromArgb(0x10, 0x89, 0x3E),
        };
        var textColor = fill.GetBrightness() > 0.45f && level is > 20 and <= 50 && !charging ? Color.Black : Color.White;

        using (var path = RoundedRect(new RectangleF(0, 0, size, size), size * 0.22f))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        var text = level?.ToString() ?? "?";
        using var font = FitFont(g, text, size);
        using var textBrush = new SolidBrush(textColor);
        // Mismo formato con el que se midió en FitFont, y sin cortar en líneas.
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        g.DrawString(text, font, textBrush, new RectangleF(0, size * 0.04f, size, size), format);
        return bitmap;
    }

    /// <summary>La fuente más grande con la que el texto entra en el ícono ("100" necesita achicarse).</summary>
    static Font FitFont(Graphics g, string text, int size)
    {
        for (var px = size * 0.82f; ; px -= 0.5f)
        {
            var font = new Font("Segoe UI", px, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            if (measured.Width <= size * 0.96f || px <= 6) return font;
            font.Dispose();
        }
    }

    internal static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
