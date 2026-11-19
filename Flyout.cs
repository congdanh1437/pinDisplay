using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PinDisplay;

/// <summary>Bảng popup kiểu Windows 11 (nền Acrylic) hiện ở góc phải trên taskbar.</summary>
public sealed class Flyout : Form
{
    private readonly TrayApp _app;
    private List<DeviceBattery> _devices = new();
    private DateTime _updated;
    private Skin _t;
    private string? _hover;
    private readonly List<(string Key, Rectangle Rect)> _hit = new();
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 10 };
    private DateTime _animStart;
    private int _targetTop;
    private DateTime _hiddenAt = DateTime.MinValue;
    private bool _menuOpen;
    private Bitmap? _backdrop;          // ảnh màn hình phía sau popup (chỉ giữ trong bộ nhớ)
    private Rectangle _backdropBounds, _workArea;

    // Kích thước gốc (DIP), nhân với K khi vẽ
    private const int W = 372, Pad = 16, HeaderH = 78, RowH = 70, RowGap = 10, FooterH = 62, EmptyH = 120;
    private float K => DeviceDpi / 96f;
    private int S(float v) => (int)Math.Round(v * K);

    public Flyout(TrayApp app)
    {
        _app = app;
        _t = Skin.Create(_app.Settings.Ui);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = false; // tự vẽ qua bitmap ARGB để giữ kênh alpha cho nền Acrylic
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _anim.Tick += (_, _) => AnimStep();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: không hiện trong Alt+Tab
            return cp;
        }
    }

    // ---------- API ----------
    public void SetData(List<DeviceBattery> devices, DateTime updated)
    {
        _devices = devices.OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToList();
        _updated = updated;
        if (Visible) { Relayout(animate: false); Invalidate(); }
    }

    public void Toggle()
    {
        if (Visible) { HideFlyout(); return; }
        // Bấm icon khi flyout đang mở -> Deactivate đã ẩn nó; đừng mở lại ngay.
        if ((DateTime.Now - _hiddenAt).TotalMilliseconds < 300) return;
        ShowFlyout();
    }

    public void ApplyTheme()
    {
        _t = Skin.Create(_app.Settings.Ui);
        _t.Backdrop = _backdrop;
        _t.BackdropBounds = _backdropBounds;
        _t.WindowLocation = new Point(Left, _targetTop);
        if (IsHandleCreated) ApplyDwm();
        Invalidate();
    }

    internal double PreviewRepaintMs;
    private bool _previewMode;

    /// <summary>Hiện flyout rồi chụp màn hình vùng đó (chế độ --preview).</summary>
    internal Bitmap CaptureForPreview(bool hoverFirst = false)
    {
        _previewMode = true;
        ShowFlyout();
        if (hoverFirst && _devices.Count > 0) { _hover = "card:" + _devices[0].Address; Invalidate(); }
        var until = DateTime.Now.AddMilliseconds(700);
        while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
        var bmp = new Bitmap(Width, Height);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(Location, Point.Empty, Size);
        // đo thời gian vẽ lại khi hover đổi (bộ đệm bị bỏ qua)
        var sw = Stopwatch.StartNew();
        _hover = "settings"; Invalidate(); Update();
        _hover = "refresh"; Invalidate(); Update();
        sw.Stop();
        PreviewRepaintMs = sw.Elapsed.TotalMilliseconds / 2;
        HideFlyout();
        _previewMode = false;
        return bmp;
    }

    // ---------- Hiện / ẩn ----------
    private void ShowFlyout()
    {
        ApplyTheme();
        Relayout(animate: true);
        CaptureBackdrop();
        Show();
        Activate();
        SetForegroundWindow(Handle);
        _animStart = DateTime.Now;
        _anim.Start();
        _app.RequestRefresh();
    }

    private void HideFlyout()
    {
        _anim.Stop();
        Hide();
        _hover = null;
        _hiddenAt = DateTime.Now;
    }

    private void Relayout(bool animate)
    {
        int n = _devices.Count;
        int contentH = n == 0 ? EmptyH : n * RowH + (n - 1) * RowGap;
        var size = new Size(S(W), S(HeaderH + contentH + 16 + FooterH));
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        int margin = S(12);
        _targetTop = wa.Bottom - size.Height - margin;
        _workArea = wa;
        Bounds = new Rectangle(wa.Right - size.Width - margin, animate ? _targetTop + S(24) : _targetTop, size.Width, size.Height);
        _t.WindowLocation = new Point(Bounds.X, _targetTop);
    }

    /// <summary>Chụp cột màn hình phía sau popup (trước khi hiện) để skin kính tự khúc xạ / làm mờ.</summary>
    private void CaptureBackdrop()
    {
        if (!_t.NeedsBackdrop) return;
        var col = new Rectangle(Left, _workArea.Top, Width, _workArea.Height);
        try
        {
            var bmp = new Bitmap(col.Width, col.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(col.Location, Point.Empty, col.Size);
            _backdrop?.Dispose();
            _backdrop = bmp;
            _backdropBounds = col;
        }
        catch { /* không chụp được (vd. màn hình khóa) -> skin dùng nền dự phòng */ }
        _t.Backdrop = _backdrop;
        _t.BackdropBounds = _backdropBounds;
    }

    private void AnimStep()
    {
        double t = Math.Min(1, (DateTime.Now - _animStart).TotalMilliseconds / 200);
        double e = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        Top = _targetTop + (int)Math.Round(S(24) * (1 - e));
        if (t >= 1) _anim.Stop();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_menuOpen && !_previewMode) HideFlyout();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) HideFlyout();
        else if (e.KeyCode == Keys.F5) _app.RequestRefresh();
        base.OnKeyDown(e);
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyDwm(); }

    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); if (Visible) Relayout(false); }

    private void ApplyDwm()
    {
        int v = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(Handle, 33, ref v, 4);
        v = _t.Dark ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref v, 4);
        v = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: bỏ viền hệ thống, tự vẽ viền kính
        DwmSetWindowAttribute(Handle, 34, ref v, 4);

        var margins = new MARGINS(_t.Translucent ? -1 : 0);
        DwmExtendFrameIntoClientArea(Handle, ref margins);
        v = _t.Translucent ? 3 : 1; // DWMSBT_TRANSIENTWINDOW (Acrylic) / none
        DwmSetWindowAttribute(Handle, 38, ref v, 4);
    }

    // ---------- Vẽ ----------
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        if (w <= 0 || h <= 0) return;
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);
            if (_t.NeedsCollectPass)
            {
                // lượt 1: skin ghi lại vị trí các tấm kính để dựng nền một lần
                _t.BeginCollect();
                PaintContent(g, w, h);
                _t.EndCollect();
                g.Clear(Color.Transparent);
            }
            PaintContent(g, w, h);
        }
        e.Graphics.CompositingMode = CompositingMode.SourceCopy;
        e.Graphics.DrawImageUnscaled(bmp, 0, 0);
    }

    private void PaintContent(Graphics g, int w, int h)
    {
        _hit.Clear();
        float k = K;
        _t.Background(g, new Rectangle(0, 0, w, h), S(8), k);

        bool lt = _t.LightType;
        string semi = Theme.SemiboldFamily;
        using var fTitle = lt ? new Font(semi, S(19), FontStyle.Regular, GraphicsUnit.Pixel)
                              : new Font(Theme.TextFamily(true), S(21), FontStyle.Bold, GraphicsUnit.Pixel);
        using var fName = lt ? new Font(semi, S(13.5f), FontStyle.Regular, GraphicsUnit.Pixel)
                             : new Font(Theme.TextFamily(), S(14), FontStyle.Bold, GraphicsUnit.Pixel);
        using var fBody = new Font(Theme.TextFamily(), S(lt ? 11.5f : 12), FontStyle.Regular, GraphicsUnit.Pixel);
        using var fPct = lt ? new Font(semi, S(19), FontStyle.Regular, GraphicsUnit.Pixel)
                            : new Font(Theme.TextFamily(true), S(21), FontStyle.Bold, GraphicsUnit.Pixel);
        using var fIcon = new Font(Theme.IconFamily, S(16), GraphicsUnit.Pixel);
        using var fIconBig = new Font(Theme.IconFamily, S(32), GraphicsUnit.Pixel);
        int pad = S(Pad);

        // Header
        _t.Label(g, "Pin thiết bị", fTitle, new RectangleF(pad - S(2), S(16), w - pad * 2 - S(90), S(30)), _t.Text);
        int connected = _devices.Count(d => d.Connected);
        var sub = connected == 0 ? "Không có thiết bị nào đang kết nối" : $"{connected} thiết bị đang kết nối";
        if (_updated != default) sub += $"  ·  {_updated:HH:mm}";
        _t.Label(g, sub, fBody, new RectangleF(pad - S(1), S(47), w - pad * 2, S(18)), _t.Sub);

        var rSettings = new Rectangle(w - pad - S(36), S(18), S(36), S(36));
        var rRefresh = new Rectangle(rSettings.X - S(44), S(18), S(36), S(36));
        RoundButton(g, "refresh", rRefresh, "", fIcon);
        RoundButton(g, "settings", rSettings, "", fIcon);

        // Danh sách
        int y = S(HeaderH);
        if (_devices.Count == 0)
        {
            var r = new Rectangle(pad, y, w - pad * 2, S(EmptyH));
            _t.Pane(g, r, S(_t.CardRadius), k);
            _t.Label(g, "", fIconBig, new RectangleF(r.X, r.Y + S(16), r.Width, S(44)), _t.Sub, StringAlignment.Center, StringAlignment.Center);
            _t.Label(g, "Kết nối tai nghe, chuột hoặc bàn phím Bluetooth\nđể xem % pin tại đây", fBody,
                new RectangleF(r.X + S(8), r.Y + S(66), r.Width - S(16), S(40)), _t.Sub, StringAlignment.Center, wrap: true);
        }
        foreach (var d in _devices)
        {
            DrawRow(g, d, new Rectangle(pad, y, w - pad * 2, S(RowH)), fName, fBody, fPct, fIcon);
            y += S(RowH + RowGap);
        }

        // Footer: đường kẻ sáng mờ hai đầu
        int fy = h - S(FooterH);
        _t.Divider(g, pad, w - pad, fy, k);

        var rToggle = new Rectangle(pad - S(6), fy + S(12), S(232), S(36));
        _hit.Add(("autostart", rToggle));
        if (_hover == "autostart") _t.Pane(g, rToggle, S(18), k, shadow: false, strength: 0.6f);
        var sw = new RectangleF(rToggle.X + S(8), rToggle.Y + S(8), S(40), S(20));
        _t.Switch(g, sw, _app.AutoStart, k);
        _t.Label(g, "Khởi động cùng Windows", fBody, new RectangleF(sw.Right + S(10), rToggle.Y, S(175), rToggle.Height), _t.Text, v: StringAlignment.Center);

        var rExit = new Rectangle(w - pad - S(86), fy + S(12), S(86), S(36));
        _hit.Add(("exit", rExit));
        _t.Pane(g, rExit, S(18), k, hover: _hover == "exit", shadow: false, strength: _hover == "exit" ? 1f : 0.7f);
        _t.Label(g, "", fIcon, new RectangleF(rExit.X + S(10), rExit.Y, S(22), rExit.Height), _t.Text, StringAlignment.Center, StringAlignment.Center);
        _t.Label(g, "Thoát", fBody, new RectangleF(rExit.X + S(38), rExit.Y, S(46), rExit.Height), _t.Text, v: StringAlignment.Center);
    }

    private void DrawRow(Graphics g, DeviceBattery d, Rectangle r, Font fName, Font fBody, Font fPct, Font fIcon)
    {
        float k = K;
        string key = "card:" + d.Address;
        _hit.Add((key, r));
        bool hover = _hover == key;
        bool onAccent = hover && _t.AccentCardOnHover;
        _t.Pane(g, r, S(_t.CardRadius), k, hover: hover, strength: d.Connected || hover ? 1f : 0.7f);
        var level = d.Connected ? _t.Level(d.Percent) : Color.FromArgb(_t.Dark ? 55 : 110, 255, 255, 255);
        var text = d.Connected || onAccent ? _t.Text : _t.Sub;
        var sub = onAccent ? Color.FromArgb(225, 255, 255, 255) : _t.Sub;
        int threshold = _app.Settings.ThresholdFor(d.Address);

        // Bong bóng icon
        int ic = S(42);
        var rc = new Rectangle(r.X + S(12), r.Y + (r.Height - ic) / 2, ic, ic);
        _t.DeviceBadge(g, rc, k, d.Connected, onAccent);
        var glyphColor = onAccent && _t is ModernSkin ? Theme.Hex(0x2F6BFF) : text;
        _t.Label(g, Theme.Glyph(d.Kind), fIcon, rc, glyphColor, StringAlignment.Center, StringAlignment.Center);

        int x = rc.Right + S(12), pctW = S(66), right = r.Right - S(14);
        _t.Label(g, d.Name, fName, new RectangleF(x - S(2), r.Y + S(10), right - pctW - x, S(21)), text, ellipsis: true);

        bool low = d.Connected && threshold > 0 && d.Percent <= threshold;
        string status = !d.Connected ? "Chưa kết nối · mức pin lần cuối"
            : low ? "Pin yếu — nên sạc"
            : threshold == 0 ? "Đã kết nối · tắt cảnh báo"
            : threshold != Settings.DefaultThreshold ? $"Đã kết nối · cảnh báo ≤ {threshold}%"
            : "Đã kết nối";
        _t.Label(g, status, fBody, new RectangleF(x - S(1), r.Y + S(31), right - pctW - x, S(17)), low && !onAccent ? _t.Low : sub, ellipsis: true);

        _t.Label(g, $"{d.Percent}%", fPct, new RectangleF(right - pctW - S(6), r.Y + S(7), pctW + S(8), S(30)),
            text, StringAlignment.Far, StringAlignment.Center);

        _t.Bar(g, new RectangleF(x, r.Bottom - S(16), right - x, S(6)), d.Percent, level, k, onAccent);
    }

    private void RoundButton(Graphics g, string key, Rectangle r, string glyph, Font f)
    {
        _hit.Add((key, r));
        _t.IconButton(g, r, K, _hover == key);
        _t.Label(g, glyph, f, r, _t.Text, StringAlignment.Center, StringAlignment.Center);
    }

    // ---------- Chuột ----------
    private string? HitTest(Point p) => _hit.LastOrDefault(h => h.Rect.Contains(p)).Key;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hover = HitTest(e.Location);
        if (hover == _hover) return;
        _hover = hover;
        Cursor = hover is null ? Cursors.Default : Cursors.Hand;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is null || _menuOpen) return;
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var key = HitTest(e.Location);
        if (key is null) return;

        if (key.StartsWith("card:"))
        {
            var d = _devices.FirstOrDefault(x => "card:" + x.Address == key);
            if (d is null) return;
            if (e.Button == MouseButtons.Left) OpenBluetoothSettings();
            else if (e.Button == MouseButtons.Right) ShowDeviceMenu(d, e.Location);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        switch (key)
        {
            case "refresh": _app.RequestRefresh(); break;
            case "settings": ShowSettingsMenu(); break;
            case "autostart": _app.ToggleAutoStart(); Invalidate(); break;
            case "exit": _app.Exit(); break;
        }
    }

    // ---------- Menu ----------
    private void ShowDeviceMenu(DeviceBattery d, Point at)
    {
        var m = ModernMenu.Create(_t);
        int cur = _app.Settings.ThresholdFor(d.Address);
        m.Header($"Cảnh báo khi {d.Name} còn");
        foreach (var v in new[] { 10, 15, 20, 25, 30 })
            m.Item($"{v}%" + (v == Settings.DefaultThreshold ? " (mặc định)" : ""), () => _app.SetThreshold(d.Address, v), cur == v);
        m.Item("Không cảnh báo", () => _app.SetThreshold(d.Address, 0), cur == 0);
        m.Separator();
        m.Item("Mở cài đặt Bluetooth", OpenBluetoothSettings);
        Popup(m, PointToScreen(at));
    }

    private void ShowSettingsMenu()
    {
        var s = _app.Settings;
        var m = ModernMenu.Create(_t);
        m.Header("Giao diện");
        m.Item("Kính (Glass)", () => _app.SetUiStyle(UiStyle.Glass), s.Ui == UiStyle.Glass);
        m.Item("Hiện đại (Tối)", () => _app.SetUiStyle(UiStyle.Modern), s.Ui == UiStyle.Modern);
        m.Separator();
        m.Header("Biểu tượng trên taskbar");
        m.Item("Số % và thanh pin", () => _app.SetIconStyle(TrayIconStyle.Number), s.IconStyle == TrayIconStyle.Number);
        m.Item("Hình cục pin", () => _app.SetIconStyle(TrayIconStyle.Battery), s.IconStyle == TrayIconStyle.Battery);
        m.Separator();
        m.Item("Thông báo khi pin yếu", () => _app.SetNotify(!s.Notify), s.Notify);
        m.Item("Mở cài đặt Bluetooth", OpenBluetoothSettings);
        m.Separator();
        if (Installer.RunningInstalled)
            m.Item("Gỡ cài đặt PinDisplay…", () => { HideFlyout(); _app.Uninstall(); });
        else
            m.Item("Cài vào máy (Start Menu)", () => { HideFlyout(); _app.Install(); });
        var r = _hit.First(h => h.Key == "settings").Rect;
        Popup(m, PointToScreen(new Point(r.Right, r.Bottom + S(4))), alignRight: true);
    }

    private void Popup(ContextMenuStrip m, Point screen, bool alignRight = false)
    {
        _menuOpen = true;
        m.Closed += (_, _) =>
        {
            _menuOpen = false;
            BeginInvoke(() =>
            {
                m.Dispose();
                if (Visible && ActiveForm != this) HideFlyout();
                else Invalidate();
            });
        };
        if (alignRight) m.Show(screen, ToolStripDropDownDirection.BelowLeft);
        else m.Show(screen);
    }

    private void OpenBluetoothSettings()
    {
        HideFlyout();
        try { Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _anim.Dispose(); _backdrop?.Dispose(); }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int L, R, T, B;
        public MARGINS(int all) { L = R = T = B = all; }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
