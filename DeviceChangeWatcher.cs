using System.Runtime.InteropServices;

namespace PinDisplay;

/// <summary>
/// Cửa sổ ẩn nhận WM_DEVICECHANGE từ Windows. Không tốn CPU khi không có gì xảy ra.
/// Một lần cắm/ngắt sinh ra nhiều sự kiện liên tiếp nên gộp lại (debounce) thành một lần báo.
/// </summary>
public sealed class DeviceChangeWatcher : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x4;

    public event Action? Changed;

    private readonly System.Windows.Forms.Timer _debounce;
    private IntPtr _notify;

    public DeviceChangeWatcher(int debounceMs = 1500)
    {
        _debounce = new System.Windows.Forms.Timer { Interval = debounceMs };
        _debounce.Tick += (_, _) => { _debounce.Stop(); Changed?.Invoke(); };

        CreateHandle(new CreateParams()); // cửa sổ top-level ẩn để nhận broadcast
        var filter = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf<DEV_BROADCAST_DEVICEINTERFACE>(),
            dbcc_devicetype = 5, // DBT_DEVTYP_DEVICEINTERFACE
        };
        _notify = RegisterDeviceNotificationW(Handle, ref filter, DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE)
        {
            int ev = (int)m.WParam;
            if (ev is DBT_DEVNODES_CHANGED or DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE)
            {
                _debounce.Stop();
                _debounce.Start();
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        _debounce.Dispose();
        if (_notify != IntPtr.Zero) { UnregisterDeviceNotification(_notify); _notify = IntPtr.Zero; }
        DestroyHandle();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size, dbcc_devicetype, dbcc_reserved;
        public Guid dbcc_classguid;
        public char dbcc_name;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotificationW(IntPtr hRecipient, ref DEV_BROADCAST_DEVICEINTERFACE filter, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);
}
