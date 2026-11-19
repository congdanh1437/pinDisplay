using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PinDisplay;

/// <summary>ContextMenuStrip vẽ lại theo kiểu menu Windows 11 (bo góc, hover mềm, dấu tick Fluent).</summary>
public static class ModernMenu
{
    public static ContextMenuStrip Create(Skin t)
    {
        var m = new ContextMenuStrip
        {
            Renderer = new Renderer(t),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(2, 4, 2, 4),
            Font = new Font(Theme.TextFamily(), 9.5f),
            BackColor = t.MenuBg,
            ForeColor = t.MenuText,
        };
        m.HandleCreated += (_, _) =>
        {
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(m.Handle, 33, ref round, 4);
            var b = t.MenuBorder;
            int border = b.R | (b.G << 8) | (b.B << 16);
            DwmSetWindowAttribute(m.Handle, 34, ref border, 4);
        };
        return m;
    }

    public static ToolStripMenuItem Item(this ContextMenuStrip m, string text, Action? onClick = null, bool isChecked = false, bool enabled = true)
    {
        var it = new ToolStripMenuItem(text) { Checked = isChecked, Enabled = enabled, Padding = new Padding(4, 5, 4, 5) };
        if (onClick is not null) it.Click += (_, _) => onClick();
        m.Items.Add(it);
        return it;
    }

    public static void Header(this ContextMenuStrip m, string text)
    {
        var it = new ToolStripMenuItem(text) { Enabled = false, Padding = new Padding(4, 5, 4, 3) };
        it.Font = new Font(m.Font, FontStyle.Bold);
        m.Items.Add(it);
    }

    public static void Separator(this ContextMenuStrip m) => m.Items.Add(new ToolStripSeparator());

    private sealed class Renderer(Skin t) : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(t.MenuBg);
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { /* DWM vẽ viền */ }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using var p = Theme.RoundRect(r, 6);
            using var b = new SolidBrush(t.MenuHover);
            g.FillPath(b, p);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? t.MenuText : t.MenuSub;
            if (e.Item is ToolStripMenuItem { Enabled: false }) // tiêu đề nhóm
                e.TextFont = e.Item.Font;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var f = new Font(Theme.IconFamily, 11f, GraphicsUnit.Point);
            TextRenderer.DrawText(g, "", f, e.ImageRectangle, t.MenuAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using var p = new Pen(t.MenuLine);
            e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = t.MenuText;
            base.OnRenderArrow(e);
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
