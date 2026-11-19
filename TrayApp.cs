using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PinDisplay;

/// <summary>Mỗi thiết bị đang kết nối = một icon trên khay hệ thống. Bấm vào icon để mở bảng chi tiết.</summary>
public sealed class TrayApp : ApplicationContext
{
    private const string CombinedKey = "*combined*";
    private readonly Dictionary<string, NotifyIcon> _icons = new();
    private readonly HashSet<string> _lowNotified = new();
    // Cắm/ngắt thiết bị được báo ngay qua sự kiện; quét định kỳ chỉ để cập nhật % pin (thay đổi chậm).
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 90_000 };
    private readonly DeviceChangeWatcher _watcher = new();
    private readonly Flyout _flyout;
    private NotifyIcon? _placeholder;
    private List<DeviceBattery> _last = new();
    private DateTime _updated;
    private bool _busy, _pending;

    public Settings Settings { get; } = Settings.Load();
    public bool AutoStart => Installer.IsAutoStart();

    public TrayApp(bool justInstalled = false)
    {
        _flyout = new Flyout(this);
        _ = _flyout.Handle; // tạo handle sớm để lần mở đầu tiên mượt

        _watcher.Changed += RequestRefresh;
        SystemEvents.UserPreferenceChanged += OnThemeChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        _timer.Tick += (_, _) => RequestRefresh();
        _timer.Start();
        RequestRefresh();

        if (justInstalled)
            ShowMessage("Đã cài đặt PinDisplay", "Bạn có thể mở lại app từ Start Menu bất cứ lúc nào.");
    }

    // ---------- Làm mới ----------
    public void RequestRefresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_busy) { _pending = true; return; }
        _busy = true;
        try
        {
            do
            {
                _pending = false;
                _last = await Task.Run(BatteryReader.Read);
                _updated = DateTime.Now;
                Apply();
                _flyout.SetData(_last, _updated);
            } while (_pending);
        }
        catch { /* bỏ qua, lần sau thử lại */ }
        finally { _busy = false; }
    }

    private void Apply()
    {
        var connected = _last.Where(d => d.Connected).ToList();
        bool lightBar = Theme.TaskbarIsLight();
        bool combine = Settings.Combine && connected.Count >= 2;
        var wanted = combine ? new[] { CombinedKey } : connected.Select(d => d.Address).ToArray();

        foreach (var key in _icons.Keys.Except(wanted).ToList())
        {
            DisposeIcon(_icons[key]);
            _icons.Remove(key);
        }
        _lowNotified.IntersectWith(connected.Select(d => d.Address));

        NotifyIcon GetIcon(string key)
        {
            if (!_icons.TryGetValue(key, out var ni)) _icons[key] = ni = NewIcon();
            ni.Visible = true;
            return ni;
        }

        if (combine)
        {
            var ni = GetIcon(CombinedKey);
            SetIcon(ni, connected.Select(d => (int?)d.Percent).ToList(), lightBar);
            ni.Text = Trim(string.Join("\n", connected.Select(d => $"{d.Name}: {d.Percent}%")));
        }
        else
        {
            foreach (var d in connected)
            {
                var ni = GetIcon(d.Address);
                SetIcon(ni, [d.Percent], lightBar);
                ni.Text = Trim($"{d.Name} — {d.Percent}%");
            }
        }

        foreach (var d in connected)
        {
            int threshold = Settings.ThresholdFor(d.Address);
            bool low = threshold > 0 && d.Percent <= threshold;
            if (low && Settings.Notify && _lowNotified.Add(d.Address))
                _icons[combine ? CombinedKey : d.Address]
                    .ShowBalloonTip(5000, "Pin yếu", $"{d.Name} chỉ còn {d.Percent}%", ToolTipIcon.Warning);
            else if (!low)
                _lowNotified.Remove(d.Address);
        }

        if (connected.Count == 0)
        {
            _placeholder ??= NewIcon();
            _placeholder.Text = "PinDisplay — chưa có thiết bị Bluetooth báo pin";
            SetIcon(_placeholder, [null], lightBar);
            _placeholder.Visible = true;
        }
        else if (_placeholder is not null)
        {
            DisposeIcon(_placeholder);
            _placeholder = null;
        }
    }

    private NotifyIcon NewIcon()
    {
        var ni = new NotifyIcon();
        ni.MouseUp += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) _flyout.Toggle();
        };
        return ni;
    }

    private void ShowMessage(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        var ni = _icons.Values.FirstOrDefault() ?? _placeholder;
        if (ni is null)
        {
            // Chưa quét xong lần đầu: đợi một chút rồi thử lại
            var t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += (_, _) => { t.Dispose(); ShowMessage(title, text, icon); };
            t.Start();
            return;
        }
        ni.ShowBalloonTip(5000, title, text, icon);
    }

    // ---------- Cài đặt (gọi từ Flyout) ----------
    public void ToggleAutoStart() => Installer.SetAutoStart(null, !AutoStart);

    public void SetThreshold(string address, int value)
    {
        if (value == Settings.DefaultThreshold) Settings.Thresholds.Remove(address);
        else Settings.Thresholds[address] = value;
        Settings.Save();
        _lowNotified.Remove(address);
        Apply();
        _flyout.SetData(_last, _updated);
    }

    public void SetIconStyle(TrayIconStyle style)
    {
        Settings.IconStyle = style;
        Settings.Save();
        Apply();
    }

    public void SetUiStyle(UiStyle style)
    {
        Settings.Ui = style;
        Settings.Save();
        _flyout.ApplyTheme();
    }

    public void SetCombine(bool on)
    {
        Settings.Combine = on;
        Settings.Save();
        Apply();
    }

    public void SetNotify(bool on)
    {
        Settings.Notify = on;
        Settings.Save();
        if (!on) _lowNotified.Clear();
        Apply();
    }

    public void Install()
    {
        try
        {
            Installer.Install(AutoStart);
        }
        catch (Exception ex)
        {
            ShowMessage("Không cài đặt được", ex.Message, ToolTipIcon.Error);
            return;
        }
        if (Installer.RunningInstalled) { ShowMessage("Đã cài đặt PinDisplay", "Đã thêm vào Start Menu."); return; }
        // Chuyển sang chạy bản đã cài
        Process.Start(new ProcessStartInfo(Installer.ExePath, "--replace --installed") { UseShellExecute = false });
        ExitThread();
    }

    public void Uninstall()
    {
        var ok = MessageBox.Show("Gỡ PinDisplay khỏi máy?\nLối tắt Start Menu, tự khởi động và cài đặt sẽ bị xóa.",
            "Gỡ cài đặt PinDisplay", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (ok != DialogResult.OK) return;
        Installer.Uninstall();
        ExitThread();
    }

    public void Exit() => ExitThread();

    // ---------- Sự kiện hệ thống ----------
    private void OnThemeChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Color) return;
        _flyout.ApplyTheme();
        Apply();
    }

    private void OnPowerChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) RequestRefresh();
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        _timer.Stop();
        _watcher.Dispose();
        foreach (var ni in _icons.Values) DisposeIcon(ni);
        _icons.Clear();
        if (_placeholder is not null) DisposeIcon(_placeholder);
        _flyout.Dispose();
        base.ExitThreadCore();
    }

    // ---------- Icon trên khay ----------
    private void SetIcon(NotifyIcon ni, IReadOnlyList<int?> levels, bool lightBar)
    {
        var old = ni.Icon;
        ni.Icon = Settings.IconStyle == TrayIconStyle.Battery
            ? RenderBattery(levels, lightBar)
            : RenderNumber(levels, lightBar);
        old?.Dispose();
    }

    /// <summary>Icon nhỏ chỉ đủ chỗ cho tối đa 3 hàng: giữ 3 thiết bị pin thấp nhất, theo thứ tự ban đầu.</summary>
    private static List<int?> Fit(IReadOnlyList<int?> levels, int max = 3) =>
        levels.Count <= max
            ? levels.ToList()
            : levels.Select((p, i) => (p, i)).OrderBy(x => x.p ?? 101).Take(max).OrderBy(x => x.i).Select(x => x.p).ToList();

    private static void FillPill(Graphics g, RectangleF r, Color c)
    {
        using var p = Theme.RoundRect(r, r.Height / 2);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    private static void DisposeIcon(NotifyIcon ni)
    {
        ni.Visible = false;
        ni.Icon?.Dispose();
        ni.Dispose();
    }

    private static (Color fg, Color level) Colors(int? percent, bool lightBar)
    {
        var fg = lightBar ? Color.FromArgb(255, 24, 24, 24) : Color.White;
        var level = percent switch
        {
            null => Color.FromArgb(140, fg),
            <= 20 => lightBar ? Theme.Hex(0xC42B1C) : Theme.Hex(0xFF6B6B),
            < 50 => lightBar ? Theme.Hex(0xC27C0E) : Theme.Hex(0xFFC83D),
            _ => lightBar ? Theme.Hex(0x0F7B0F) : Theme.Hex(0x6CCB5F),
        };
        return (fg, level);
    }

    private static Icon ToIcon(Bitmap bmp)
    {
        var h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }

    private static Graphics Prepare(Bitmap bmp)
    {
        var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        return g;
    }

    /// <summary>
    /// Kiểu 1: số % + thanh pin nhỏ bên dưới. Nhiều thiết bị: số là % thấp nhất,
    /// mỗi thiết bị một thanh xếp chồng bên dưới.
    /// </summary>
    private static Icon RenderNumber(IReadOnlyList<int?> levels, bool lightBar)
    {
        int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
        var rows = Fit(levels);
        int n = rows.Count;
        int? percent = rows.Any(p => p.HasValue) ? rows.Min() : null;
        var (fg, level) = Colors(percent, lightBar);
        var textColor = percent is <= 20 ? level : fg;

        using var bmp = new Bitmap(s, s);
        using (var g = Prepare(bmp))
        {
            float barH = n == 1 ? Math.Max(2.5f, s * 0.16f) : Math.Max(2f, s * 0.11f);
            float gap = Math.Max(1f, s * 0.06f);
            float barsTop = s - s * 0.02f - n * barH - (n - 1) * gap;
            var track = new RectangleF(s * 0.06f, barsTop, s * 0.88f, barH);
            for (int i = 0; i < n; i++)
            {
                var row = track with { Y = barsTop + i * (barH + gap) };
                FillPill(g, row, Color.FromArgb(lightBar ? 60 : 80, fg));
                if (rows[i] is int pct)
                    FillPill(g, row with { Width = Math.Max(barH, row.Width * pct / 100f) }, Colors(pct, lightBar).level);
            }

            // Số %: vẽ bằng path rồi co cho vừa khung phía trên thanh pin
            var text = percent?.ToString() ?? "–";
            using var path = new GraphicsPath();
            float digitH;
            using (var fam = new FontFamily(Theme.TextFamily(true)))
            {
                path.AddString(text, fam, (int)FontStyle.Bold, 100, PointF.Empty, StringFormat.GenericTypographic);
                using var probe = new GraphicsPath();
                probe.AddString("0", fam, (int)FontStyle.Bold, 100, PointF.Empty, StringFormat.GenericTypographic);
                digitH = probe.GetBounds().Height;
            }
            var bounds = path.GetBounds();
            var box = new RectangleF(0, 0, s, track.Top - s * 0.08f);
            float scale = Math.Min(box.Width / bounds.Width, box.Height / digitH);
            using var m = new Matrix();
            m.Translate(box.X + (box.Width - bounds.Width * scale) / 2, box.Y + (box.Height - bounds.Height * scale) / 2);
            m.Scale(scale, scale);
            m.Translate(-bounds.X, -bounds.Y);
            path.Transform(m);
            using var tb = new SolidBrush(textColor);
            g.FillPath(tb, path);
        }
        return ToIcon(bmp);
    }

    /// <summary>
    /// Kiểu 2: hình cục pin nằm ngang, phần ruột tô theo % pin.
    /// 2 thiết bị: hai cục pin xếp chồng; 3 thiết bị: ba thanh pin.
    /// </summary>
    private static Icon RenderBattery(IReadOnlyList<int?> levels, bool lightBar)
    {
        int s = Math.Max(16, SystemInformation.SmallIconSize.Width);
        var rows = Fit(levels);
        int n = rows.Count;
        var fg = Colors(null, lightBar).fg;

        using var bmp = new Bitmap(s, s);
        using (var g = Prepare(bmp))
        {
            if (n >= 3)
            {
                // Quá chật cho viền pin: chỉ vẽ thanh nền + phần pin còn lại
                float gap = Math.Max(1.5f, s * 0.09f);
                float barH = (s * 0.92f - gap * (n - 1)) / n;
                float top = (s - (n * barH + (n - 1) * gap)) / 2;
                for (int i = 0; i < n; i++)
                {
                    var row = new RectangleF(s * 0.04f, top + i * (barH + gap), s * 0.92f, barH);
                    FillPill(g, row, Color.FromArgb(lightBar ? 60 : 80, fg));
                    if (rows[i] is int pct)
                        FillPill(g, row with { Width = Math.Max(barH, row.Width * pct / 100f) }, Colors(pct, lightBar).level);
                }
                return ToIcon(bmp);
            }

            float stroke = n == 1 ? Math.Max(1.25f, s / 13f) : Math.Max(1f, s / 16f);
            float nubW = Math.Max(1.5f, s * 0.1f);
            float gapY = Math.Max(2.5f, s * 0.16f);
            float h = n == 1 ? s * 0.6f : (s - stroke - gapY) / 2;
            float top0 = n == 1 ? (s - h) / 2 : stroke / 2;
            for (int i = 0; i < n; i++)
            {
                var body = new RectangleF(stroke / 2 + 0.25f, top0 + i * (h + gapY), s - nubW - stroke - 0.75f, h);
                using (var p = Theme.RoundRect(body, Math.Min(s * 0.14f, h * 0.3f)))
                using (var pen = new Pen(fg, stroke))
                    g.DrawPath(pen, p);

                var nub = new RectangleF(body.Right + stroke / 2 + 0.25f, body.Y + h * 0.3f, nubW - 0.25f, h * 0.4f);
                using (var p = Theme.RoundRect(nub, Math.Min(nubW, nub.Height) / 2))
                using (var b = new SolidBrush(fg))
                    g.FillPath(b, p);

                if (rows[i] is int pct)
                {
                    float inset = stroke + Math.Max(n == 1 ? 0.75f : 0.6f, s / 24f);
                    var inner = RectangleF.Inflate(body, -inset, -inset);
                    var fill = inner with { Width = Math.Max(inner.Height * 0.35f, inner.Width * pct / 100f) };
                    using var p = Theme.RoundRect(fill, Math.Min(s * 0.06f, fill.Height / 2));
                    using var b = new SolidBrush(Colors(pct, lightBar).level);
                    g.FillPath(b, p);
                }
            }
        }
        return ToIcon(bmp);
    }

    private static string Trim(string s) => s.Length <= 127 ? s : s[..127];

    // ---------- Chế độ xem trước (--preview) ----------
    internal static void SavePreview(string dir)
    {
        var app = new TrayApp();
        app._last = BatteryReader.Read();
        app._updated = DateTime.Now;
        app._flyout.SetData(app._last, app._updated);
        var original = app.Settings.Ui;
        foreach (var style in Enum.GetValues<UiStyle>())
        {
            app.Settings.Ui = style;
            app._flyout.ApplyTheme();
            using var shot = app._flyout.CaptureForPreview(hoverFirst: style == UiStyle.Modern);
            shot.Save(Path.Combine(dir, $"flyout-{style.ToString().ToLowerInvariant()}.png"));
            File.AppendAllText(Path.Combine(dir, "timing.txt"), $"{style}: repaint {app._flyout.PreviewRepaintMs:F1} ms | {GlassSkin.LastTimings}" + Environment.NewLine);
        }
        app.Settings.Ui = original;

        foreach (var light in new[] { false, true })
        using (var strip = new Bitmap(9 * 40, 80))
        {
            using (var g = Graphics.FromImage(strip))
            {
                g.Clear(light ? Theme.Hex(0xEEEEEE) : Theme.Hex(0x1C1C1C));
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                int i = 0;
                var samples = new int?[][] { [100], [85], [50], [35], [12], [null], [80, 45], [90, 15], [70, 40, 10] };
                foreach (var p in samples)
                {
                    using var a = RenderNumber(p, light);
                    using var b = RenderBattery(p, light);
                    g.DrawImage(a.ToBitmap(), i * 40 + 4, 4, 32, 32);
                    g.DrawImage(b.ToBitmap(), i * 40 + 4, 44, 32, 32);
                    i++;
                }
            }
            strip.Save(Path.Combine(dir, light ? "icons-light.png" : "icons-dark.png"));
        }
        app.ExitThread();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr h);
}
