using System.Drawing.Drawing2D;

namespace PinDisplay;

public enum UiStyle { Glass, Modern }

/// <summary>Giao diện của bảng popup. Mỗi skin tự vẽ nền, thẻ, nút, thanh pin, công tắc.</summary>
public abstract class Skin
{
    public abstract bool Dark { get; }
    /// <summary>Có dùng nền Acrylic của Windows phía sau hay không.</summary>
    public abstract bool Translucent { get; }
    public abstract float CardRadius { get; }

    public Color Text { get; protected init; } = Color.White;
    public Color Sub { get; protected init; }
    public Color Low { get; protected init; }
    public Color Mid { get; protected init; }
    public Color Good { get; protected init; }

    public Color MenuBg { get; protected init; }
    public Color MenuHover { get; protected init; }
    public Color MenuText { get; protected init; }
    public Color MenuSub { get; protected init; }
    public Color MenuAccent { get; protected init; }
    public Color MenuLine { get; protected init; }
    public Color MenuBorder { get; protected init; }

    public Color Level(int percent) => percent <= 20 ? Low : percent < 50 ? Mid : Good;

    /// <summary>Thẻ đang hover có chuyển sang nền màu nhấn (chữ trắng) không.</summary>
    public virtual bool AccentCardOnHover => false;
    protected virtual bool TextShadow => false;

    public abstract void Background(Graphics g, Rectangle r, float radius, float k);
    public abstract void Pane(Graphics g, RectangleF r, float radius, float k, bool hover = false, bool shadow = true, float strength = 1f);
    public abstract void DeviceBadge(Graphics g, RectangleF r, float k, bool active, bool onAccent);
    public abstract void IconButton(Graphics g, RectangleF r, float k, bool hover);
    public abstract void Bar(Graphics g, RectangleF r, int percent, Color level, float k, bool onAccent);
    public abstract void Switch(Graphics g, RectangleF r, bool on, float k);
    public virtual void Divider(Graphics g, float x1, float x2, float y, float k)
    {
        using var lb = new LinearGradientBrush(new RectangleF(x1, y - 1, Math.Max(1, x2 - x1), 3), Color.White, Color.White, 0f)
        {
            InterpolationColors = new ColorBlend
            {
                Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(Dark ? 40 : 130, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) },
                Positions = new[] { 0f, 0.5f, 1f },
            },
        };
        using var pen = new Pen(lb, Math.Max(1, k));
        g.DrawLine(pen, x1, y, x2, y);
    }

    public void Label(Graphics g, string s, Font f, RectangleF r, Color c,
        StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near,
        bool ellipsis = false, bool wrap = false)
    {
        using var sf = new StringFormat(wrap ? 0 : StringFormatFlags.NoWrap)
        {
            Alignment = h, LineAlignment = v,
            Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None,
        };
        if (TextShadow)
        {
            using var sb = new SolidBrush(Color.FromArgb(c.A * TextShadowAlpha / 255, 0, 45, 120));
            g.DrawString(s, f, sb, r with { Y = r.Y + 1 }, sf);
        }
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, r, sf);
    }

    // ---------- Kính vật lý: cần ảnh nền phía sau cửa sổ + hai lượt vẽ ----------
    /// <summary>Skin cần ảnh chụp nền phía sau cửa sổ.</summary>
    public virtual bool NeedsBackdrop => false;
    /// <summary>Cần lượt vẽ đầu để ghi lại vị trí các tấm kính.</summary>
    public virtual bool NeedsCollectPass => NeedsBackdrop;
    /// <summary>Kiểu chữ nhẹ (semibold/regular, nhỏ) thay vì đậm.</summary>
    public virtual bool LightType => false;
    protected virtual int TextShadowAlpha => 125;
    /// <summary>Ảnh chụp vùng màn hình phía sau popup (chụp trước khi hiện).</summary>
    public Bitmap? Backdrop { get; set; }
    public Rectangle BackdropBounds { get; set; }
    /// <summary>Vị trí đích của cửa sổ trên màn hình.</summary>
    public Point WindowLocation { get; set; }
    /// <summary>Lượt 1: chỉ ghi lại các tấm kính; lượt 2: vẽ thật.</summary>
    public virtual void BeginCollect() { }
    public virtual void EndCollect() { }

    public static Skin Create(UiStyle style) => style == UiStyle.Modern ? new ModernSkin() : new GlassSkin();

    // ---------- tiện ích ----------
    protected static Color W(int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), 255, 255, 255);

    protected static void Glow(Graphics g, RectangleF r, Color c)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(r);
        using var b = new PathGradientBrush(path) { CenterColor = c, SurroundColors = new[] { Color.FromArgb(0, c) } };
        g.FillPath(b, path);
    }

    protected static void SoftShadow(Graphics g, RectangleF r, float radius, float k, Color c, int alpha, int layers, float offsetY)
    {
        using var cardPath = Theme.RoundRect(r, radius);
        var state = g.Save();
        g.SetClip(cardPath, CombineMode.Exclude);
        for (int i = layers; i >= 1; i--)
        {
            float grow = i * 1.4f * k;
            var sr = new RectangleF(r.X - grow / 2, r.Y - grow / 2 + offsetY * k, r.Width + grow, r.Height + grow);
            using var p = Theme.RoundRect(sr, radius + grow / 2);
            using var b = new SolidBrush(Color.FromArgb(alpha, c));
            g.FillPath(b, p);
        }
        g.Restore(state);
    }
}

/// <summary>
/// Liquid Glass: nền xanh da trời + chất lỏng 3D (ảnh nhúng sẵn), các tấm kính dày dựng bằng
/// <see cref="GlassRenderer"/>: mặt bên (thành kính), khúc xạ tập trung sát mép, đường sáng mảnh,
/// bóng dài ngả xanh. Nút, thanh pin, công tắc là viền trắng mảnh; chữ trắng nhỏ, gần như không bóng.
/// </summary>
public sealed class GlassSkin : Skin
{
    private static readonly Color Env = Color.FromArgb(255, 236, 247, 255);  // bầu trời phản chiếu trên mép kính
    private static Bitmap? _art;
    private static bool _artLoaded;

    private readonly List<GlassPane> _big = new(), _small = new();
    private bool _collecting;
    private string? _baseKey, _frameKey, _bigKey;
    private int[]? _base, _bigLayer;
    private Bitmap? _frame;
    internal static string LastTimings = "";

    public override bool Dark => false;
    public override bool Translucent => false;
    public override bool NeedsCollectPass => true;
    public override bool LightType => true;
    public override float CardRadius => 14;
    protected override bool TextShadow => true;
    protected override int TextShadowAlpha => 95;

    public GlassSkin()
    {
        Text = Color.White;
        Sub = W(225);
        Low = Theme.Hex(0xFF5F6D); Mid = Theme.Hex(0xFFC93C); Good = Theme.Hex(0x3BE38A);
        MenuBg = Theme.Hex(0xEEF7FF); MenuHover = Theme.Hex(0xD3EAFF); MenuText = Theme.Hex(0x0E2E55);
        MenuSub = Theme.Hex(0x5C7DA0); MenuAccent = Theme.Hex(0x1A7BE0); MenuLine = Theme.Hex(0xCBE3F8);
        MenuBorder = Theme.Hex(0xA8D4FA);
    }

    /// <summary>Ảnh chất lỏng nhúng trong exe (assets/liquid.jpg). Không có thì dùng nền vẽ bằng code.</summary>
    private static Bitmap? Art
    {
        get
        {
            if (_artLoaded) return _art;
            _artLoaded = true;
            try
            {
                using var s = typeof(GlassSkin).Assembly.GetManifestResourceStream("liquid.jpg");
                if (s is not null) _art = new Bitmap(s);
            }
            catch { _art = null; }
            return _art;
        }
    }

    public override void BeginCollect() { _collecting = true; _big.Clear(); _small.Clear(); }
    public override void EndCollect() => _collecting = false;

    public override void Background(Graphics g, Rectangle r, float radius, float k)
    {
        if (_collecting) return;
        g.DrawImageUnscaled(Compose(r.Width, r.Height, radius, k), 0, 0);
    }

    public override void Pane(Graphics g, RectangleF r, float radius, float k, bool hover = false, bool shadow = true, float strength = 1f)
    {
        if (!_collecting) return;
        float bezel = Math.Min(radius * 0.8f, Math.Min(r.Width, r.Height) / 2 - 1);
        _big.Add(new GlassPane(r, radius, Bezel: bezel, Thickness: bezel * 1.5f,
            Lift: (hover ? 0.2f : 0.12f) * strength, Specular: (hover ? 1.15f : 1f) * strength, Shadow: shadow));
    }

    public override void DeviceBadge(Graphics g, RectangleF r, float k, bool active, bool onAccent) => Disc(r, active);
    public override void IconButton(Graphics g, RectangleF r, float k, bool hover) => Disc(r, hover);

    /// <summary>Đĩa kính tròn: vành dày khúc xạ, lòng đĩa trong.</summary>
    private void Disc(RectangleF r, bool active)
    {
        if (!_collecting) return;
        float rad = r.Width / 2;
        _small.Add(new GlassPane(r, rad, Bezel: rad * 0.55f, Thickness: rad * 0.75f,
            Lift: active ? 0.18f : 0.08f, Specular: active ? 1.2f : 0.95f, Shadow: false));
    }

    private Bitmap Compose(int w, int h, float radius, float k)
    {
        string baseKey = $"{w}x{h}|{radius}|{k}";
        if (_base is null || baseKey != _baseKey)
        {
            _base = BuildBase(w, h, radius, k);
            _baseKey = baseKey;
            _bigKey = null;
        }

        string bigKey = baseKey + "|" + string.Join(";", _big);
        string frameKey = bigKey + "|" + string.Join(";", _small);
        if (_frame is not null && frameKey == _frameKey) return _frame;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (_bigLayer is null || bigKey != _bigKey)
        {
            var big = (int[])_base.Clone();
            foreach (var p in _big.Where(p => p.Shadow)) GlassRenderer.Shadow(big, w, h, p, k);
            var behind = GlassRenderer.Blur(big, w, h, Math.Max(1, (int)(2.5f * k)));
            foreach (var p in _big)
            {
                if (p.Shadow) GlassRenderer.Slab(big, behind, w, h, p, 1.5f * k, 5f * k);
                GlassRenderer.Lens(big, behind, w, h, p, Env);
            }
            _bigLayer = big;
            _bigKey = bigKey;
            LastTimings = $"big {sw.Elapsed.TotalMilliseconds:F1}";
        }
        var layer = (int[])_bigLayer.Clone();
        foreach (var p in _small) GlassRenderer.Lens(layer, _bigLayer, w, h, p, Env);
        LastTimings += $" total {sw.Elapsed.TotalMilliseconds:F1}";

        _frame?.Dispose();
        _frame = GlassRenderer.Write(layer, w, h);
        _frameKey = frameKey;
        return _frame;
    }

    /// <summary>Nền = trời xanh + ảnh chất lỏng (phủ kín chiều ngang, neo đáy), cả cửa sổ là một tấm kính lớn.</summary>
    private static int[] BuildBase(int w, int h, float radius, float k)
    {
        int[] px;
        using (var bmp = new Bitmap(w, h))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, w, h), Theme.Hex(0x8ACDF4), Theme.Hex(0x5CA9E6), 80f))
                    g.FillRectangle(b, 0, 0, w, h);
                var art = Art;
                if (art is not null)
                {
                    float scale = (float)w / art.Width;
                    if (art.Height * scale < h) scale = (float)h / art.Height;
                    float dw = art.Width * scale, dh = art.Height * scale;
                    g.DrawImage(art, new RectangleF((w - dw) / 2, h - dh, dw, dh));
                }
                else
                {
                    var r = new Rectangle(0, 0, w, h);
                    Liquid(g, r, 0.40f, 0.26f, Color.FromArgb(170, 0x3E, 0x7C, 0xE0), Color.FromArgb(150, 0x8A, 0x6E, 0xFF), k);
                    Liquid(g, r, 0.80f, 0.16f, Color.FromArgb(150, 0x7F, 0xD8, 0xFF), Color.FromArgb(150, 0x2E, 0x55, 0xD8), k);
                }
                // lớp sáng nhẹ phía trên để tiêu đề dễ đọc
                var top = new RectangleF(0, 0, w, h * 0.22f);
                using (var b = new LinearGradientBrush(top, Color.FromArgb(70, 0x6F, 0xB8, 0xEE), Color.FromArgb(0, 0x6F, 0xB8, 0xEE), 90f))
                    g.FillRectangle(b, top);
            }
            px = GlassRenderer.Read(bmp);
        }
        GlassRenderer.Grade(px, saturation: 1.1f, Theme.Hex(0x3A8BDF), tintAmount: 0.22f, brighten: 0f);

        // Dải tiêu đề và dải chân: kính mờ hơn để chữ đọc rõ trên nền nhiều chi tiết
        var soft = GlassRenderer.Blur(px, w, h, Math.Max(2, (int)(9 * k)));
        GlassRenderer.Grade(soft, saturation: 1f, Theme.Hex(0x4A97E2), tintAmount: 0.3f, brighten: 0f);
        float t0 = 70 * k, t1 = 92 * k, b0 = h - 80 * k, b1 = h - 62 * k;
        for (int y = 0; y < h; y++)
        {
            float m = y < t0 ? 1 : y < t1 ? 1 - (y - t0) / (t1 - t0) : y > b1 ? 1 : y > b0 ? (y - b0) / (b1 - b0) : 0;
            if (m <= 0) continue;
            m = m * m * (3 - 2 * m) * 0.9f;
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, a = px[i], c = soft[i];
                int r = (int)(((a >> 16) & 255) + ((((c >> 16) & 255) - ((a >> 16) & 255)) * m));
                int gg = (int)(((a >> 8) & 255) + ((((c >> 8) & 255) - ((a >> 8) & 255)) * m));
                int bb = (int)((a & 255) + (((c & 255) - (a & 255)) * m));
                px[i] = unchecked((int)0xFF000000) | (r << 16) | (gg << 8) | bb;
            }
        }

        var win = (int[])px.Clone();
        GlassRenderer.Lens(win, px, w, h,
            new GlassPane(new RectangleF(0, 0, w, h), radius, Bezel: 7 * k, Thickness: 9 * k, Lift: 0f, Specular: 0.9f, Shadow: false), Env);
        return win;
    }

    private static void Liquid(Graphics g, Rectangle r, float yRatio, float amp, Color c1, Color c2, float k)
    {
        float w = r.Width, h = r.Height, y = h * yRatio, a = h * amp, t = h * 0.13f;
        using var path = new GraphicsPath();
        path.AddBezier(-w * 0.1f, y + a * 0.6f, w * 0.25f, y - a, w * 0.6f, y + a * 0.9f, w * 1.1f, y - a * 0.5f);
        path.AddLine(w * 1.1f, y - a * 0.5f, w * 1.1f, y - a * 0.5f + t);
        path.AddBezier(w * 1.1f, y - a * 0.5f + t, w * 0.65f, y + a * 1.1f + t, w * 0.3f, y - a * 0.7f + t, -w * 0.1f, y + a * 0.6f + t);
        path.CloseFigure();
        using (var b = new LinearGradientBrush(new RectangleF(0, y - a, w, a * 2 + t + 1), c1, c2, 20f))
            g.FillPath(b, path);
        using var hl = new GraphicsPath();
        hl.AddBezier(-w * 0.1f, y + a * 0.6f, w * 0.25f, y - a, w * 0.6f, y + a * 0.9f, w * 1.1f, y - a * 0.5f);
        using var pen = new Pen(W(170), 1.6f * k);
        g.DrawPath(pen, hl);
    }

    /// <summary>Thanh pin tối giản: rãnh viền trắng mảnh, ruột màu mức pin.</summary>
    public override void Bar(Graphics g, RectangleF r, int percent, Color level, float k, bool onAccent)
    {
        if (_collecting) return;
        var track = r with { Y = r.Y + r.Height / 2 - 2.5f * k, Height = 5 * k };
        using (var p = Theme.RoundRect(track, track.Height / 2))
        {
            using var b = new SolidBrush(W(45));
            g.FillPath(b, p);
            using var pen = new Pen(W(150), Math.Max(1, k * 0.9f));
            g.DrawPath(pen, p);
        }
        var fill = RectangleF.Inflate(track, -1.2f * k, -1.2f * k);
        fill.Width = Math.Max(fill.Height, fill.Width * percent / 100f);
        using var fp = Theme.RoundRect(fill, fill.Height / 2);
        using var fb = new SolidBrush(level);
        g.FillPath(fb, fp);
    }

    /// <summary>Công tắc dạng viên kính viền trắng, núm trắng.</summary>
    public override void Switch(Graphics g, RectangleF r, bool on, float k)
    {
        if (_collecting) return;
        using var path = Theme.RoundRect(r, r.Height / 2);
        using (var b = new SolidBrush(W(on ? 120 : 35))) g.FillPath(b, path);
        using (var pen = new Pen(W(210), 1.1f * k)) g.DrawPath(pen, path);
        float d = r.Height - 6 * k;
        var knob = new RectangleF(on ? r.Right - 3 * k - d : r.X + 3 * k, r.Y + 3 * k, d, d);
        using (var sb = new SolidBrush(Color.FromArgb(50, 0, 40, 110))) g.FillEllipse(sb, knob with { Y = knob.Y + k });
        using (var kb = new SolidBrush(on ? Theme.Hex(0x2F86F0) : Color.White)) g.FillEllipse(kb, knob);
    }

    public override void Divider(Graphics g, float x1, float x2, float y, float k)
    {
        if (!_collecting) base.Divider(g, x1, x2, y, k);
    }
}

/// <summary>Hiện đại (tối): nền xanh đen, thẻ bo lớn, huy hiệu xanh dương đặc, thẻ hover chuyển xanh.</summary>
public sealed class ModernSkin : Skin
{
    private static readonly Color Bg = Theme.Hex(0x0B0F19);
    private static readonly Color Panel = Theme.Hex(0x151B27);
    private static readonly Color PanelHover = Theme.Hex(0x1B2231);
    private static readonly Color Line = Theme.Hex(0x232B3B);
    private static readonly Color Blue = Theme.Hex(0x2F6BFF);
    private static readonly Color BlueLight = Theme.Hex(0x5C8CFF);

    public override bool Dark => true;
    public override bool Translucent => false;
    public override float CardRadius => 20;
    public override bool AccentCardOnHover => true;

    public ModernSkin()
    {
        Text = Color.White;
        Sub = Theme.Hex(0x8B95A8);
        Low = Theme.Hex(0xFF5C6C); Mid = Theme.Hex(0xFFB547); Good = Theme.Hex(0x3DDC97);
        MenuBg = Theme.Hex(0x151B27); MenuHover = Theme.Hex(0x222B3C); MenuText = Color.White;
        MenuSub = Theme.Hex(0x7D879A); MenuAccent = BlueLight; MenuLine = Line; MenuBorder = Theme.Hex(0x2A3446);
    }

    public override void Background(Graphics g, Rectangle r, float radius, float k)
    {
        g.Clear(Bg);
        Glow(g, new RectangleF(r.Width * 0.35f, -r.Height * 0.35f, r.Width, r.Height * 0.7f), Color.FromArgb(40, 0x2F, 0x6B, 0xFF));
        var outer = new RectangleF(r.X + k * 0.5f, r.Y + k * 0.5f, r.Width - k, r.Height - k);
        using var p = Theme.RoundRect(outer, radius);
        using var pen = new Pen(Theme.Hex(0x222A3A), k);
        g.DrawPath(pen, p);
    }

    public override void Pane(Graphics g, RectangleF r, float radius, float k, bool hover = false, bool shadow = true, float strength = 1f)
    {
        if (shadow) SoftShadow(g, r, radius, k, Color.Black, 14, 6, 3);
        using var path = Theme.RoundRect(r, radius);
        if (hover && AccentCardOnHover && strength >= 1f)
        {
            using var b = new LinearGradientBrush(r, BlueLight, Blue, 60f);
            g.FillPath(b, path);
            // quầng sáng tròn mờ như thiết kế mẫu
            var state = g.Save();
            g.SetClip(path, CombineMode.Intersect);
            Glow(g, new RectangleF(r.X + r.Width * 0.25f, r.Y - r.Height * 0.6f, r.Width * 0.9f, r.Height * 2f), Color.FromArgb(55, 255, 255, 255));
            g.Restore(state);
            return;
        }
        var fill = hover ? PanelHover : Panel;
        if (strength < 1f) fill = Theme.Blend(Bg, fill, strength);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        // vòng cung sáng mờ ở góc trên phải
        var st = g.Save();
        g.SetClip(path, CombineMode.Intersect);
        Glow(g, new RectangleF(r.X + r.Width * 0.45f, r.Y - r.Height * 0.8f, r.Width * 0.9f, r.Height * 1.8f), Color.FromArgb(14, 255, 255, 255));
        g.Restore(st);
        using var pen = new Pen(Line, k);
        g.DrawPath(pen, path);
    }

    public override void DeviceBadge(Graphics g, RectangleF r, float k, bool active, bool onAccent)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(r);
        if (onAccent)
        {
            using var b = new SolidBrush(Color.White);
            g.FillPath(b, path);
            return;
        }
        using var bb = active ? (Brush)new LinearGradientBrush(r, BlueLight, Blue, 60f) : new SolidBrush(Theme.Hex(0x232B3B));
        g.FillPath(bb, path);
    }

    public override void IconButton(Graphics g, RectangleF r, float k, bool hover)
    {
        using var path = Theme.RoundRect(r, r.Width * 0.32f);
        using var b = new SolidBrush(hover ? Theme.Hex(0x232C3E) : Theme.Hex(0x171D2A));
        g.FillPath(b, path);
        using var pen = new Pen(Line, k);
        g.DrawPath(pen, path);
    }

    public override void Bar(Graphics g, RectangleF r, int percent, Color level, float k, bool onAccent)
    {
        using (var p = Theme.RoundRect(r, r.Height / 2))
        using (var b = new SolidBrush(onAccent ? W(60) : Theme.Hex(0x232B3B))) g.FillPath(b, p);
        var fill = r with { Width = Math.Max(r.Height, r.Width * percent / 100f) };
        using var fp = Theme.RoundRect(fill, fill.Height / 2);
        using var fb = new SolidBrush(onAccent ? Color.White : level);
        g.FillPath(fb, fp);
    }

    public override void Switch(Graphics g, RectangleF r, bool on, float k)
    {
        using var path = Theme.RoundRect(r, r.Height / 2);
        using (var b = new SolidBrush(on ? Blue : Theme.Hex(0x232B3B))) g.FillPath(b, path);
        float d = r.Height - 6 * k;
        var knob = new RectangleF(on ? r.Right - 3 * k - d : r.X + 3 * k, r.Y + 3 * k, d, d);
        using var kb = new SolidBrush(on ? Color.White : Theme.Hex(0x8B95A8));
        g.FillEllipse(kb, knob);
    }

    public override void Divider(Graphics g, float x1, float x2, float y, float k)
    {
        using var pen = new Pen(Line, Math.Max(1, k));
        g.DrawLine(pen, x1, y, x2, y);
    }
}
