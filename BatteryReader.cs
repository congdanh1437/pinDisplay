using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PinDisplay;

public enum DeviceKind { Other, Audio, Mouse, Keyboard, Gamepad, Pen }

public sealed record DeviceBattery(string Address, string Name, int Percent, bool Connected, DeviceKind Kind);

/// <summary>
/// Đọc % pin mà Windows đã lấy từ thiết bị Bluetooth (classic HFP + BLE)
/// qua thuộc tính PnP DEVPKEY_Bluetooth_Battery. Chỉ dùng SetupAPI, không cần thư viện ngoài.
/// </summary>
public static partial class BatteryReader
{
    private static readonly DEVPROPKEY KeyBattery = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly DEVPROPKEY KeyConnected = new(new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), 15);
    private static readonly DEVPROPKEY KeyFriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static readonly DEVPROPKEY KeyDeviceDesc = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 2);

    [GeneratedRegex(@"([0-9A-F]{12})(_C[0-9A-F]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex AddressRegex();

    [GeneratedRegex(@"\s+(Hands-Free( AG)?( Audio)?|Stereo|Avrcp Transport|A2DP SNK)$", RegexOptions.IgnoreCase)]
    private static partial Regex NameSuffixRegex();

    public static List<DeviceBattery> Read()
    {
        var byAddress = new Dictionary<string, DeviceBattery>(StringComparer.OrdinalIgnoreCase);
        // Tai nghe Bluetooth thường: % pin nằm ở nút con (Hands-Free), còn trạng thái "đã kết nối"
        // nằm ở nút cha BTHENUM\DEV_<địa chỉ>. Nên gom trạng thái kết nối theo địa chỉ từ mọi nút.
        var connectedAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumerator in new[] { "BTHLE", "BTHENUM" })
        {
            foreach (var d in ReadEnumerator(enumerator, connectedAddresses))
            {
                if (!byAddress.TryGetValue(d.Address, out var existing))
                    byAddress[d.Address] = d;
                else
                    byAddress[d.Address] = (d.Connected && !existing.Connected ? d : existing) with
                    {
                        Connected = d.Connected || existing.Connected,
                        Kind = existing.Kind != DeviceKind.Other ? existing.Kind : d.Kind,
                    };
            }
        }
        return byAddress.Values
            .Select(d => d.Connected || !connectedAddresses.Contains(d.Address) ? d : d with { Connected = true })
            .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IEnumerable<DeviceBattery> ReadEnumerator(string enumerator, HashSet<string> connectedAddresses)
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == INVALID_HANDLE_VALUE) yield break;
        try
        {
            var info = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref info); i++)
            {
                var id = GetInstanceId(set, ref info);
                var m = AddressRegex().Match(id);
                var address = m.Success ? m.Groups[1].Value : id;

                var conn = GetBytes(set, ref info, KeyConnected);
                bool connected = conn is { Length: > 0 } && conn[0] != 0;
                if (connected) connectedAddresses.Add(address);

                var battery = GetBytes(set, ref info, KeyBattery);
                if (battery is not { Length: > 0 }) continue;

                var name = GetString(set, ref info, KeyFriendlyName) ?? GetString(set, ref info, KeyDeviceDesc) ?? address;
                name = NameSuffixRegex().Replace(name.Trim(), "");

                yield return new DeviceBattery(address, name, Math.Clamp((int)battery[0], 0, 100), connected, GuessKind(name, id));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static DeviceKind GuessKind(string name, string instanceId)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("mouse") || n.Contains("chuột")) return DeviceKind.Mouse;
        if (n.Contains("keyboard") || n.Contains("bàn phím")) return DeviceKind.Keyboard;
        if (n.Contains("controller") || n.Contains("gamepad") || n.Contains("xbox") || n.Contains("dualsense")) return DeviceKind.Gamepad;
        if (Regex.IsMatch(n, @"\b(pen|stylus)\b")) return DeviceKind.Pen;
        if (Regex.IsMatch(n, "bud|pod|head|ear|sound|audio|speaker|jbl|sony|beats"))
            return DeviceKind.Audio;
        var id = instanceId.ToUpperInvariant();
        if (id.Contains("{0000111E") || id.Contains("{0000110B") || id.Contains("{00001108")) return DeviceKind.Audio;
        return DeviceKind.Other;
    }

    private static byte[]? GetBytes(IntPtr set, ref SP_DEVINFO_DATA info, DEVPROPKEY key)
    {
        var k = key;
        SetupDiGetDevicePropertyW(set, ref info, ref k, out _, null, 0, out uint size, 0);
        if (size == 0) return null;
        var buf = new byte[size];
        return SetupDiGetDevicePropertyW(set, ref info, ref k, out _, buf, size, out _, 0) ? buf : null;
    }

    private static string? GetString(IntPtr set, ref SP_DEVINFO_DATA info, DEVPROPKEY key)
    {
        var b = GetBytes(set, ref info, key);
        if (b is null || b.Length < 2) return null;
        var s = Encoding.Unicode.GetString(b).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }

    private static string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var buf = new char[512];
        return SetupDiGetDeviceInstanceIdW(set, ref info, buf, (uint)buf.Length, out uint req)
            ? new string(buf, 0, (int)Math.Max(0, req - 1))
            : "";
    }

    // ---------- P/Invoke ----------
    private const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA { public uint cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY { public Guid fmtid; public uint pid; public DEVPROPKEY(Guid g, uint p) { fmtid = g; pid = p; } }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwnd, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA info);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA info, ref DEVPROPKEY key,
        out uint type, byte[]? buffer, uint size, out uint required, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA info, char[] buffer, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
