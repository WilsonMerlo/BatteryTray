namespace BatteryTray.App;

/// <summary>Colores claro/oscuro para los menús contextuales (WinForms no sigue el tema de Windows solo).</summary>
internal static class MenuTheming
{
    public static void Apply(ToolStrip menu, Theme theme)
    {
        menu.Renderer = new ToolStripProfessionalRenderer(new Colors(theme)) { RoundedEdges = false };
        menu.BackColor = theme.Background;
        menu.ForeColor = theme.Text;
        foreach (ToolStripItem item in menu.Items)
            ApplyItem(item, theme);
    }

    static void ApplyItem(ToolStripItem item, Theme theme)
    {
        item.ForeColor = theme.Text;
        item.BackColor = theme.Background;
        if (item is ToolStripMenuItem m)
        {
            m.DropDown.BackColor = theme.Background;
            foreach (ToolStripItem child in m.DropDownItems)
                ApplyItem(child, theme);
            if (m.HasDropDownItems)
                m.DropDown.Renderer = menuRenderer(theme);
        }

        static ToolStripRenderer menuRenderer(Theme t) => new ToolStripProfessionalRenderer(new Colors(t)) { RoundedEdges = false };
    }

    sealed class Colors(Theme t) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => t.Background;
        public override Color MenuBorder => t.Border;
        public override Color MenuItemBorder => t.Surface;
        public override Color MenuItemSelected => t.Surface;
        public override Color MenuItemSelectedGradientBegin => t.Surface;
        public override Color MenuItemSelectedGradientEnd => t.Surface;
        public override Color MenuItemPressedGradientBegin => t.Surface;
        public override Color MenuItemPressedGradientEnd => t.Surface;
        public override Color ImageMarginGradientBegin => t.Background;
        public override Color ImageMarginGradientMiddle => t.Background;
        public override Color ImageMarginGradientEnd => t.Background;
        public override Color SeparatorDark => t.Border;
        public override Color SeparatorLight => t.Background;
        public override Color CheckBackground => t.Surface;
        public override Color CheckSelectedBackground => t.Surface;
        public override Color CheckPressedBackground => t.Surface;
        public override Color ButtonSelectedBorder => t.Surface;
    }
}
