using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace PinDisplay;

/// <summary>Bảng màu theo phong cách Windows 11, tự đổi theo chế độ Sáng/Tối, màu nhấn và hiệu ứng trong suốt.</summary>
public sealed record Theme(
    bool Dark, bool Translucent,
    Color Bg, Color Card, Color CardHover, Color CardBorder, Color Text, Color Sub, Color Hover,
    Color Menu, Color MenuHover,
    Color Accent, Color OnAccent, Color Good, Color Mid, Color Low, Color Track)
{
    /// <summary>Nền mờ (Acrylic) chỉ có trên Windows 11 22H2+ và khi người dùng bật "Transparency effects".</summary>
    public static bool BackdropSupported =>
        Environment.OSVersion.Version.Build >= 22621 && ReadFlag("EnableTransparency", true);

    public static Theme Current(bool translucent)
    {
        bool dark = !ReadFlag("AppsUseLightTheme", true);
        var accent = ReadAccent();
        if (dark)
        {
            return new Theme(true, translucent,
                Bg: Hex(0x202020),
                Card: translucent ? Color.FromArgb(18, 255, 255, 255) : Hex(0x2B2B2B),
                CardHover: translucent ? Color.FromArgb(30, 255, 255, 255) : Hex(0x323232),
                CardBorder: translucent ? Color.FromArgb(22, 255, 255, 255) : Hex(0x353535),
                Text: Color.White, Sub: Hex(0xB4B4B4),
                Hover: translucent ? Color.FromArgb(24, 255, 255, 255) : Hex(0x383838),
                Menu: Hex(0x2C2C2C), MenuHover: Hex(0x3A3A3A),
                Accent: Blend(accent, Color.White, 0.35f), OnAccent: Color.Black,
                Good: Hex(0x6CCB5F), Mid: Hex(0xFFC83D), Low: Hex(0xFF6B6B),
                Track: translucent ? Color.FromArgb(45, 255, 255, 255) : Hex(0x454545));
        }
        return new Theme(false, translucent,
            Bg: Hex(0xF3F3F3),
            Card: translucent ? Color.FromArgb(170, 255, 255, 255) : Color.White,
            CardHover: translucent ? Color.FromArgb(230, 255, 255, 255) : Hex(0xFAFAFA),
            CardBorder: translucent ? Color.FromArgb(18, 0, 0, 0) : Hex(0xE5E5E5),
            Text: Hex(0x1B1B1B), Sub: Hex(0x5C5C5C),
            Hover: translucent ? Color.FromArgb(14, 0, 0, 0) : Hex(0xE9E9E9),
            Menu: Hex(0xF9F9F9), MenuHover: Hex(0xEAEAEA),
            Accent: accent, OnAccent: Color.White,
            Good: Hex(0x0F7B0F), Mid: Hex(0xC27C0E), Low: Hex(0xC42B1C),
            Track: translucent ? Color.FromArgb(30, 0, 0, 0) : Hex(0xE0E0E0));
    }

    /// <summary>Thanh taskbar dùng nền sáng hay tối (có thể khác với chế độ của app).</summary>
    public static bool TaskbarIsLight() => ReadFlag("SystemUsesLightTheme", false);

    public Color Level(int percent) => percent <= 20 ? Low : percent < 50 ? Mid : Good;

    // ---------- helpers ----------
    public static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.A + (b.A - a.A) * t),
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static bool ReadFlag(string name, bool fallback)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue(name) is int v ? v != 0 : fallback;
        }
        catch { return fallback; }
    }

    private static Color ReadAccent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int v) // ABGR
                return Color.FromArgb(255, v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF);
        }
        catch { }
        return Hex(0x0078D4);
    }

    public static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ---------- fonts ----------
    private static readonly HashSet<string> Installed =
        new(FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

    public static string TextFamily(bool display = false) =>
        display && Installed.Contains("Segoe UI Variable Display") ? "Segoe UI Variable Display"
        : Installed.Contains("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";

    public static string SemiboldFamily =>
        Installed.Contains("Segoe UI Semibold") ? "Segoe UI Semibold" : TextFamily(true);

    public static string IconFamily =>
        Installed.Contains("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    public static string Glyph(DeviceKind kind) => kind switch
    {
        DeviceKind.Audio => "",
        DeviceKind.Mouse => "",
        DeviceKind.Keyboard => "",
        DeviceKind.Gamepad => "",
        DeviceKind.Pen => "",
        _ => "",
    };
}
