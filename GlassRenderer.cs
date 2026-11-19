using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PinDisplay;

/// <summary>Một tấm kính cần dựng: hình bo góc + thông số quang học.</summary>
public readonly record struct GlassPane(
    RectangleF Rect, float Radius, float Bezel, float Thickness,
    float Lift = 0.06f, float Specular = 1f, bool Shadow = true);

/// <summary>
/// Dựng kính theo mô hình vật lý đơn giản (tham khảo kube.io "Liquid Glass in the Browser"):
///  • mặt cắt viền kính dạng convex squircle  h(t) = ⁴√(1 − (1 − t)⁴)
///  • khúc xạ theo định luật Snell  n₁·sinθ₁ = n₂·sinθ₂  (không khí 1.0 → kính ~1.5),
///    độ lệch tia = chiều cao kính × tan(θ₁ − θ₂), lệch vào phía tâm tấm kính
///  • tán sắc: chiết suất khác nhau cho R/G/B → viền có ánh cầu vồng nhẹ
///  • phản xạ Fresnel (xấp xỉ Schlick, R₀ = 0.04) → mép dốc phản chiếu bầu trời sáng hơn
///  • highlight gương theo hướng nguồn sáng (trên-trái) + phản xạ trong yếu ở mép đối diện
///  • bóng đổ mềm và vệt hội tụ ánh sáng (caustic) bên dưới tấm kính
/// Mọi phép tính chạy trên mảng pixel ARGB, dùng bảng tra 256 mức theo độ sâu viền.
/// </summary>
public static class GlassRenderer
{
    private const float R0 = 0.04f;                       // ((1 − 1.5)/(1 + 1.5))²
    private static readonly float[] Ior = { 1.485f, 1.50f, 1.525f }; // R, G, B (tán sắc)
    private static readonly (float X, float Y) Light = Normalize(-0.55f, -0.83f); // ánh sáng từ trên-trái

    // ---------- I/O ----------
    public static int[] Read(Bitmap b)
    {
        var rect = new Rectangle(0, 0, b.Width, b.Height);
        var data = b.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var px = new int[b.Width * b.Height];
            for (int y = 0; y < b.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, px, y * b.Width, b.Width);
            return px;
        }
        finally { b.UnlockBits(data); }
    }

    public static Bitmap Write(int[] px, int w, int h)
    {
        var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(px, y * w, data.Scan0 + y * data.Stride, w);
        }
        finally { b.UnlockBits(data); }
        return b;
    }

    // ---------- Làm mờ (3 lượt box blur ≈ Gaussian) ----------
    public static int[] Blur(int[] src, int w, int h, int radius)
    {
        var a = (int[])src.Clone();
        if (radius < 1) return a;
        var tmp = new int[a.Length];
        for (int pass = 0; pass < 3; pass++)
        {
            BoxH(a, tmp, w, h, radius);
            BoxV(tmp, a, w, h, radius);
        }
        return a;
    }

    private static void BoxH(int[] s, int[] d, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        Parallel.For(0, h, y =>
        {
            int row = y * w, sr = 0, sg = 0, sb = 0;
            for (int i = -r; i <= r; i++)
            {
                int c = s[row + Math.Clamp(i, 0, w - 1)];
                sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255;
            }
            for (int x = 0; x < w; x++)
            {
                d[row + x] = Pack(sr * inv, sg * inv, sb * inv);
                int c1 = s[row + Math.Clamp(x - r, 0, w - 1)], c2 = s[row + Math.Clamp(x + r + 1, 0, w - 1)];
                sr += ((c2 >> 16) & 255) - ((c1 >> 16) & 255);
                sg += ((c2 >> 8) & 255) - ((c1 >> 8) & 255);
                sb += (c2 & 255) - (c1 & 255);
            }
        });
    }

    private static void BoxV(int[] s, int[] d, int w, int h, int r)
    {
        float inv = 1f / (2 * r + 1);
        Parallel.For(0, w, x =>
        {
            int sr = 0, sg = 0, sb = 0;
            for (int i = -r; i <= r; i++)
            {
                int c = s[Math.Clamp(i, 0, h - 1) * w + x];
                sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255;
            }
            for (int y = 0; y < h; y++)
            {
                d[y * w + x] = Pack(sr * inv, sg * inv, sb * inv);
                int c1 = s[Math.Clamp(y - r, 0, h - 1) * w + x], c2 = s[Math.Clamp(y + r + 1, 0, h - 1) * w + x];
                sr += ((c2 >> 16) & 255) - ((c1 >> 16) & 255);
                sg += ((c2 >> 8) & 255) - ((c1 >> 8) & 255);
                sb += (c2 & 255) - (c1 & 255);
            }
        });
    }

    /// <summary>Tăng bão hòa + phủ màu (kính màu hấp thụ một phần ánh sáng).</summary>
    public static void Grade(int[] px, float saturation, Color tint, float tintAmount, float brighten)
    {
        float tr = tint.R, tg = tint.G, tb = tint.B;
        Parallel.For(0, px.Length / 4096 + 1, chunk =>
        {
            int end = Math.Min(px.Length, (chunk + 1) * 4096);
            for (int i = chunk * 4096; i < end; i++)
            {
                int c = px[i];
                float r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
                float l = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                r = l + (r - l) * saturation; g = l + (g - l) * saturation; b = l + (b - l) * saturation;
                r += (tr - r) * tintAmount; g += (tg - g) * tintAmount; b += (tb - b) * tintAmount;
                r += (255 - r) * brighten; g += (255 - g) * brighten; b += (255 - b) * brighten;
                px[i] = Pack(r, g, b);
            }
        });
    }

    // ---------- Bóng đổ + caustic ----------
    /// <summary>Bóng mềm ngả xanh đổ về dưới-phải, kèm vệt caustic (ánh sáng hội tụ qua kính).</summary>
    public static void Shadow(int[] dst, int w, int h, GlassPane p, float k,
        float offX = 3, float offY = 8, float strength = 0.17f, float spread = 9, float reachDip = 28)
    {
        float ox = offX * k, oy = offY * k, sp = spread * k, reach = reachDip * k;
        var r = p.Rect;
        float cx = r.X + r.Width / 2 + ox, cy = r.Y + r.Height / 2 + oy, hx = r.Width / 2, hy = r.Height / 2;
        int x0 = Math.Max(0, (int)(r.X + ox - reach)), x1 = Math.Min(w, (int)(r.Right + ox + reach) + 1);
        int y0 = Math.Max(0, (int)(r.Y + oy - reach)), y1 = Math.Min(h, (int)(r.Bottom + oy + reach) + 1);
        float rad = Math.Min(p.Radius, Math.Min(hx, hy));

        Parallel.For(y0, y1, y =>
        {
            for (int x = x0; x < x1; x++)
            {
                float d = Sdf(x + 0.5f - cx, y + 0.5f - cy, hx, hy, rad, out float nx, out float ny);
                if (d > reach) continue;
                float shade = strength * (d <= 0 ? 1 : MathF.Exp(-d / sp));
                float caustic = d <= 0 ? 0
                    : 0.14f * MathF.Exp(-Sq(d - 3f * k) / (5f * k * k)) * Math.Max(0, nx * -Light.X + ny * -Light.Y);
                int i = y * w + x, c = dst[i];
                float cr = (c >> 16) & 255, cg = (c >> 8) & 255, cb = c & 255;
                // bóng của kính xanh: tối đi nhưng giữ sắc lam
                cr *= 1 - shade * 1.15f; cg *= 1 - shade * 0.85f; cb *= 1 - shade * 0.45f;
                cr += (255 - cr) * caustic; cg += (255 - cg) * caustic; cb += (255 - cb) * caustic;
                dst[i] = Pack(cr, cg, cb);
            }
        });
    }

    /// <summary>
    /// Thành kính (mặt bên) của một tấm kính dày: phần hình dời xuống-phải mà mặt trên không che.
    /// Ánh sáng đi xuyên mặt bên bị bẻ mạnh, nên mặt bên sáng, hơi xanh ngọc, tối dần ở chỗ giáp mặt trên
    /// và có một đường sáng mảnh ở mép ngoài.
    /// </summary>
    public static void Slab(int[] dst, int[] src, int w, int h, GlassPane p, float depthX, float depthY)
    {
        var r = p.Rect;
        float hx = r.Width / 2, hy = r.Height / 2;
        float cx = r.X + hx, cy = r.Y + hy;
        float rad = Math.Min(p.Radius, Math.Min(hx, hy));
        float depth = MathF.Sqrt(depthX * depthX + depthY * depthY);
        int x0 = Math.Max(0, (int)r.X - 1), x1 = Math.Min(w, (int)Math.Ceiling(r.Right + depthX) + 2);
        int y0 = Math.Max(0, (int)r.Y - 1), y1 = Math.Min(h, (int)Math.Ceiling(r.Bottom + depthY) + 2);

        Parallel.For(y0, y1, y =>
        {
            for (int x = x0; x < x1; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float dWall = Sdf(fx - cx - depthX, fy - cy - depthY, hx, hy, rad, out _, out _);
                if (dWall > 0.5f) continue;
                float dTop = Sdf(fx - cx, fy - cy, hx, hy, rad, out _, out _);
                if (dTop < -1f) continue; // mặt trên sẽ phủ lên
                float cover = Math.Clamp(0.5f - dWall, 0, 1);
                float s = Math.Clamp(dTop / depth, 0, 1); // 0: giáp mặt trên → 1: mép ngoài

                // ánh sáng qua mặt bên bị bẻ mạnh: lấy mẫu lệch lên trên
                float sr = SampleChannel(src, w, h, fx - depthX * 2f - 0.5f, fy - depthY * 2.5f - 0.5f, 16);
                float sg = SampleChannel(src, w, h, fx - depthX * 2f - 0.5f, fy - depthY * 2.5f - 0.5f, 8);
                float sb = SampleChannel(src, w, h, fx - depthX * 2f - 0.5f, fy - depthY * 2.5f - 0.5f, 0);
                // mặt bên kính: sáng, ngả xanh ngọc
                sr += (200 - sr) * 0.35f; sg += (240 - sg) * 0.4f; sb += (255 - sb) * 0.45f;
                float shade = 0.22f * (1 - s) * (1 - s);
                sr *= 1 - shade; sg *= 1 - shade; sb *= 1 - shade * 0.6f;
                float line = dWall > -1.4f ? 0.55f * (1.4f + Math.Min(0, dWall)) / 1.4f : 0;
                sr += (255 - sr) * line; sg += (255 - sg) * line; sb += (255 - sb) * line;

                int i = y * w + x, c = dst[i];
                if (cover < 1)
                {
                    sr = ((c >> 16) & 255) + (sr - ((c >> 16) & 255)) * cover;
                    sg = ((c >> 8) & 255) + (sg - ((c >> 8) & 255)) * cover;
                    sb = (c & 255) + (sb - (c & 255)) * cover;
                }
                dst[i] = Pack(sr, sg, sb);
            }
        });
    }

    // ---------- Thấu kính ----------
    public static void Lens(int[] dst, int[] src, int w, int h, GlassPane p, Color env)
    {
        var lut = BuildLut(p);
        var r = p.Rect;
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, hx = r.Width / 2, hy = r.Height / 2;
        float rad = Math.Min(p.Radius, Math.Min(hx, hy));
        int x0 = Math.Max(0, (int)r.X - 1), x1 = Math.Min(w, (int)Math.Ceiling(r.Right) + 1);
        int y0 = Math.Max(0, (int)r.Y - 1), y1 = Math.Min(h, (int)Math.Ceiling(r.Bottom) + 1);
        float er = env.R, eg = env.G, eb = env.B;

        Parallel.For(y0, y1, y =>
        {
            for (int x = x0; x < x1; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                float d = Sdf(fx - cx, fy - cy, hx, hy, rad, out float nx, out float ny);
                if (d > 0.5f) continue;
                float cover = Math.Clamp(0.5f - d, 0, 1);
                float dist = Math.Max(0, -d);
                int li = (int)(Math.Min(1f, dist / p.Bezel) * 255);
                ref readonly var e = ref lut[li];

                // khúc xạ + tán sắc: mỗi kênh màu lệch một chút khác nhau, vào phía tâm
                float rr = SampleChannel(src, w, h, fx - nx * e.DispR - 0.5f, fy - ny * e.DispR - 0.5f, 16);
                float gg = SampleChannel(src, w, h, fx - nx * e.DispG - 0.5f, fy - ny * e.DispG - 0.5f, 8);
                float bb = SampleChannel(src, w, h, fx - nx * e.DispB - 0.5f, fy - ny * e.DispB - 0.5f, 0);

                // kính mờ: một phần ánh sáng tán xạ trong khối kính
                rr += (255 - rr) * p.Lift; gg += (255 - gg) * p.Lift; bb += (255 - bb) * p.Lift;

                // Fresnel: mép càng dốc càng phản chiếu môi trường (bầu trời sáng)
                rr += (er - rr) * e.Fresnel; gg += (eg - gg) * e.Fresnel; bb += (eb - bb) * e.Fresnel;

                // highlight gương theo hướng nguồn sáng + phản xạ trong ở mép đối diện
                float ndl = nx * Light.X + ny * Light.Y;
                float spec = (Sq(Math.Max(0, ndl)) * 0.85f + Sq(Math.Max(0, -ndl)) * 0.3f) * e.Rim * p.Specular;
                if (dist < 1.5f) spec += (0.3f + 0.6f * Math.Max(0, ndl)) * p.Specular * (1.5f - dist) / 1.5f;
                spec = Math.Min(1, spec);
                rr += (255 - rr) * spec; gg += (255 - gg) * spec; bb += (255 - bb) * spec;

                int i = y * w + x;
                if (cover < 1)
                {
                    int c = dst[i];
                    rr = ((c >> 16) & 255) + (rr - ((c >> 16) & 255)) * cover;
                    gg = ((c >> 8) & 255) + (gg - ((c >> 8) & 255)) * cover;
                    bb = (c & 255) + (bb - (c & 255)) * cover;
                }
                dst[i] = Pack(rr, gg, bb);
            }
        });
    }

    private readonly record struct LutEntry(float DispR, float DispG, float DispB, float Fresnel, float Rim);

    private static LutEntry[] BuildLut(GlassPane p)
    {
        var lut = new LutEntry[256];
        for (int i = 0; i < 256; i++)
        {
            float t = i / 255f;
            float u = 1 - t;
            float height = MathF.Pow(Math.Max(0, 1 - u * u * u * u), 0.25f);                 // h(t)
            float dh = u * u * u * MathF.Pow(Math.Max(1e-4f, 1 - u * u * u * u), -0.75f);   // h'(t)
            float slope = Math.Min(dh, 8f) * p.Thickness / p.Bezel;                        // dz/dr (px/px)
            float theta1 = MathF.Atan(slope);                                              // góc tới so với pháp tuyến
            float z = p.Thickness * height;                                                // độ dày kính tại điểm này
            float Disp(float n) => z * MathF.Tan(theta1 - MathF.Asin(MathF.Sin(theta1) / n));
            float cos = MathF.Cos(theta1);
            float fresnel = R0 + (1 - R0) * MathF.Pow(1 - cos, 5);                          // Schlick
            float rim = MathF.Pow(u, 2.2f);
            lut[i] = new LutEntry(Disp(Ior[0]), Disp(Ior[1]), Disp(Ior[2]), fresnel * 0.9f, rim);
        }
        return lut;
    }

    // ---------- Hình học ----------
    /// <summary>Khoảng cách có dấu tới hình chữ nhật bo góc (âm = bên trong) + pháp tuyến hướng ra ngoài.</summary>
    private static float Sdf(float px, float py, float hx, float hy, float rad, out float nx, out float ny)
    {
        float qx = Math.Abs(px) - (hx - rad), qy = Math.Abs(py) - (hy - rad);
        float mx = Math.Max(qx, 0), my = Math.Max(qy, 0);
        float outside = MathF.Sqrt(mx * mx + my * my);
        float d = outside + Math.Min(Math.Max(qx, qy), 0) - rad;
        if (outside > 1e-4f) { nx = mx / outside * Math.Sign(px); ny = my / outside * Math.Sign(py); }
        else if (qx > qy) { nx = Math.Sign(px); ny = 0; }
        else { nx = 0; ny = Math.Sign(py); }
        return d;
    }

    private static float SampleChannel(int[] px, int w, int h, float x, float y, int shift)
    {
        x = Math.Clamp(x, 0, w - 1.001f); y = Math.Clamp(y, 0, h - 1.001f);
        int x0 = (int)x, y0 = (int)y;
        float fx = x - x0, fy = y - y0;
        int i = y0 * w + x0;
        float a = (px[i] >> shift) & 255, b = (px[i + 1] >> shift) & 255;
        float c = (px[i + w] >> shift) & 255, d = (px[i + w + 1] >> shift) & 255;
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }

    private static int Pack(float r, float g, float b) =>
        unchecked((int)0xFF000000) | (Clamp8(r) << 16) | (Clamp8(g) << 8) | Clamp8(b);

    private static int Clamp8(float v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5f);
    private static float Sq(float v) => v * v;
    private static (float, float) Normalize(float x, float y) { float l = MathF.Sqrt(x * x + y * y); return (x / l, y / l); }
}
