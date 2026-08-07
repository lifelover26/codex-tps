using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexTPSTray;

/// <summary>
/// Custom ToolStripRenderer that visually matches the WPF OverlayWindow context menu.
/// Follows EffectiveTheme (application theme), not OverlayTheme.
/// </summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly TrayMenuColorTable _colorTable;

    // Compact layout constants in logical pixels (96 DPI base).
    // WinForms auto-scales these at higher DPI.
    private const int CompactPadding = 1;
    private const int ItemPaddingHorizontal = 4;
    private const int ItemPaddingVertical = 2;

    public TrayMenuRenderer(EffectiveTheme theme) : base(new TrayMenuColorTable(theme))
    {
        _colorTable = (TrayMenuColorTable)ColorTable;
        RoundedEdges = false;
    }

    public EffectiveTheme Theme => _colorTable.Theme;

    /// <summary>
    /// Applies this renderer and compact layout settings to the root ContextMenuStrip
    /// and all nested ToolStripDropDownMenu instances.
    /// </summary>
    public void ApplyTo(ContextMenuStrip menu)
    {
        menu.Renderer = this;
        ConfigureToolStrip(menu);

        foreach (var item in GetMenuItemsRecursive(menu.Items))
        {
            ConfigureMenuItem(item);

            if (item is ToolStripMenuItem { HasDropDownItems: true } menuItem)
            {
                var dropDown = menuItem.DropDown as ToolStripDropDownMenu;
                if (dropDown != null)
                {
                    dropDown.Renderer = this;
                    ConfigureToolStrip(dropDown);
                }
            }
        }
    }

    /// <summary>
    /// Configures a dynamically created ToolStripMenuItem with the same compact layout.
    /// Call this when adding items at runtime (e.g., WSL data sources).
    /// </summary>
    public void ConfigureDynamicItem(ToolStripMenuItem item)
    {
        ConfigureMenuItem(item);

        if (item.HasDropDownItems && item.DropDown is ToolStripDropDownMenu dropDown)
        {
            dropDown.Renderer = this;
            ConfigureToolStrip(dropDown);
        }
    }

    private static void ConfigureToolStrip(ToolStrip toolStrip)
    {
        // ContextMenuStrip and ToolStripDropDownMenu both support these properties
        if (toolStrip is ContextMenuStrip contextMenu)
        {
            contextMenu.ShowImageMargin = false;
            contextMenu.ShowCheckMargin = true;
        }
        else if (toolStrip is ToolStripDropDownMenu dropDownMenu)
        {
            dropDownMenu.ShowImageMargin = false;
            dropDownMenu.ShowCheckMargin = true;
        }
        toolStrip.Padding = new Padding(CompactPadding);
    }

    private static void ConfigureMenuItem(ToolStripItem item)
    {
        item.Padding = new Padding(ItemPaddingHorizontal, ItemPaddingVertical, ItemPaddingHorizontal, ItemPaddingVertical);
    }

    /// <summary>
    /// Recursively collects all ToolStripMenuItem items from a collection.
    /// </summary>
    private static IEnumerable<ToolStripMenuItem> GetMenuItemsRecursive(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                yield return menuItem;
                if (menuItem.HasDropDownItems)
                {
                    foreach (var child in GetMenuItemsRecursive(menuItem.DropDownItems))
                        yield return child;
                }
            }
        }
    }

    /// <summary>
    /// Updates the color table for a new theme. Returns true if the theme actually changed.
    /// </summary>
    public bool TryUpdateTheme(EffectiveTheme newTheme)
    {
        if (_colorTable.Theme == newTheme)
            return false;

        _colorTable.UpdateTheme(newTheme);
        return true;
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        var rect = new Rectangle(Point.Empty, item.Size);

        // Always fill background with menu background first
        using var bgBrush = new SolidBrush(_colorTable.Background);
        e.Graphics.FillRectangle(bgBrush, rect);

        // Then overlay hover background for selected+enabled items
        if (item.Selected && item.Enabled)
        {
            using var hoverBrush = new SolidBrush(_colorTable.HoverBackground);
            e.Graphics.FillRectangle(hoverBrush, rect);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled)
            e.TextColor = _colorTable.DisabledText;
        else if (e.Item.Selected && e.Item.Enabled)
            e.TextColor = _colorTable.HoverText;
        else
            e.TextColor = _colorTable.Text;

        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        if (e?.Item == null)
            return;
        e.ArrowColor = e.Item.Enabled
            ? (e.Item.Selected ? _colorTable.HoverText : _colorTable.Arrow)
            : _colorTable.DisabledText;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var item = e.Item;
        var rect = e.ImageRectangle;

        // Use the actual image rectangle provided by WinForms (DPI-aware).
        // If it's empty, fall back to a reasonable default based on item height.
        if (rect.Width == 0 || rect.Height == 0)
        {
            var size = item.Height > 0 ? item.Height - 4 : 16;
            rect = new Rectangle(2, 2, size, size);
        }

        // Draw checkmark only within the actual check area
        var checkColor = item.Enabled
            ? (item.Selected ? _colorTable.HoverText : _colorTable.Check)
            : _colorTable.DisabledText;

        using var pen = new Pen(checkColor, 1.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };

        var cx = rect.X + rect.Width / 2;
        var cy = rect.Y + rect.Height / 2;
        var offset = rect.Width / 4;
        var pts = new[]
        {
            new PointF(cx - offset, cy),
            new PointF(cx - offset / 3, cy + offset),
            new PointF(cx + offset, cy - offset)
        };
        e.Graphics.DrawLines(pen, pts);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Fill the left check column with menu background instead of default white
        using var brush = new SolidBrush(_colorTable.Background);
        e.Graphics.FillRectangle(brush, e.ToolStrip.ClientRectangle);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var sep = (ToolStripSeparator)e.Item;

        // Calculate the check column width from the ToolStrip's current scale.
        // WinForms provides the image margin width via the ToolStripDropDownMenu.
        int checkColumnWidth = 0;
        if (e.ToolStrip is ToolStripDropDownMenu dropDown)
        {
            checkColumnWidth = dropDown.DisplayRectangle.X;
        }

        var rect = sep.IsOnDropDown
            ? new Rectangle(checkColumnWidth, 0, e.Item.Width - checkColumnWidth, e.Item.Height)
            : new Rectangle(0, 0, e.Item.Width, e.Item.Height);

        var y = rect.Y + rect.Height / 2;
        using var pen = new Pen(_colorTable.Sep);
        e.Graphics.DrawLine(pen, rect.X, y, rect.Right, y);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_colorTable.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_colorTable.Background);
        e.Graphics.FillRectangle(brush, e.ToolStrip.ClientRectangle);
    }
}

/// <summary>
/// Color table matching OverlayDark.xaml / OverlayLight.xaml resources.
/// Uses opaque colors to avoid WinForms transparency flicker.
/// </summary>
internal sealed class TrayMenuColorTable : ProfessionalColorTable
{
    public EffectiveTheme Theme { get; private set; }

    // Public properties with non-conflicting names for use by the renderer
    public Color Background { get; private set; }
    public Color Border { get; private set; }
    public Color Text { get; private set; }
    public Color HoverBackground { get; private set; }
    public Color HoverText { get; private set; }
    public Color DisabledText { get; private set; }
    public Color Check { get; private set; }
    public Color Arrow { get; private set; }
    public Color Sep { get; private set; }

    public TrayMenuColorTable(EffectiveTheme theme)
    {
        UpdateTheme(theme);
    }

    public void UpdateTheme(EffectiveTheme theme)
    {
        Theme = theme;
        if (theme == EffectiveTheme.Dark)
        {
            Background = Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D);
            Border = Color.FromArgb(0xFF, 0x40, 0x40, 0x40);
            Text = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            HoverBackground = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
            HoverText = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            DisabledText = Color.FromArgb(0xFF, 0x99, 0x99, 0x99);
            Check = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            Arrow = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            Sep = Color.FromArgb(0xFF, 0x40, 0x40, 0x40);
        }
        else
        {
            Background = Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5);
            Border = Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0);
            Text = Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);
            HoverBackground = Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
            HoverText = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            DisabledText = Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0);
            Check = Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);
            Arrow = Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);
            Sep = Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0);
        }
    }

    // Override ProfessionalColorTable members so WinForms infrastructure uses our colors
    public override Color MenuBorder => Border;
    public override Color ToolStripBorder => Border;
    public override Color MenuItemSelected => HoverBackground;
    public override Color MenuItemSelectedGradientBegin => HoverBackground;
    public override Color MenuItemSelectedGradientEnd => HoverBackground;
    public override Color MenuItemBorder => HoverBackground;
    public override Color MenuStripGradientBegin => Background;
    public override Color MenuStripGradientEnd => Background;
    public override Color CheckBackground => Background;
    public override Color CheckSelectedBackground => Background;
    public override Color CheckPressedBackground => Background;
    public override Color SeparatorDark => Sep;
    public override Color SeparatorLight => Sep;
    public override Color ImageMarginGradientBegin => Background;
    public override Color ImageMarginGradientMiddle => Background;
    public override Color ImageMarginGradientEnd => Background;
    public override Color ImageMarginRevealedGradientBegin => Background;
    public override Color ImageMarginRevealedGradientMiddle => Background;
    public override Color ImageMarginRevealedGradientEnd => Background;
    public override Color ButtonSelectedHighlight => HoverBackground;
    public override Color ButtonSelectedHighlightBorder => HoverBackground;
    public override Color ButtonPressedGradientBegin => HoverBackground;
    public override Color ButtonPressedGradientEnd => HoverBackground;
    public override Color ButtonSelectedGradientBegin => HoverBackground;
    public override Color ButtonSelectedGradientEnd => HoverBackground;
    public override Color StatusStripGradientBegin => Background;
    public override Color StatusStripGradientEnd => Background;
}
