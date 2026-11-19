using System.Diagnostics;
using Microsoft.Win32;

namespace PinDisplay;

/// <summary>Cài đặt cho riêng người dùng hiện tại (không cần quyền admin):
/// %LOCALAPPDATA%\Programs\PinDisplay + lối tắt Start Menu + mục trong "Installed apps".</summary>
public static class Installer
{
    public const string Version = "1.2.0";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\PinDisplay";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string AppName = "PinDisplay";

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PinDisplay");
    public static string ExePath => Path.Combine(Dir, "PinDisplay.exe");
    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), "PinDisplay.lnk");

    public static bool RunningInstalled =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), ExePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Chép exe vào thư mục cài đặt, tạo lối tắt và đăng ký gỡ cài đặt.</summary>
    public static void Install(bool autoStart)
    {
        Directory.CreateDirectory(Dir);
        if (!RunningInstalled) File.Copy(Environment.ProcessPath!, ExePath, overwrite: true);

        CreateShortcut(ShortcutPath, ExePath);

        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", "PinDisplay");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "PinDisplay");
            k.SetValue("DisplayIcon", ExePath);
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", $"\"{ExePath}\" --uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(ExePath).Length / 1024), RegistryValueKind.DWord);
        }

        if (autoStart) SetAutoStart(ExePath, true);
    }

    /// <summary>Gỡ lối tắt, khóa registry, cài đặt; thư mục cài đặt được xóa sau khi app thoát.</summary>
    public static void Uninstall()
    {
        foreach (var p in Process.GetProcessesByName("PinDisplay"))
        {
            if (p.Id == Environment.ProcessId) continue;
            try { p.Kill(); p.WaitForExit(2000); } catch { }
        }
        TryDo(() => File.Delete(ShortcutPath));
        TryDo(() => Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false));
        SetAutoStart(null, false);
        TryDo(() => Directory.Delete(Settings.Folder, true));

        if (Directory.Exists(Dir))
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{Dir}\"")
            { CreateNoWindow = true, UseShellExecute = false });
        }
    }

    public static bool IsAutoStart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(AppName) is string;
    }

    public static void SetAutoStart(string? exe, bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(AppName, $"\"{exe ?? Environment.ProcessPath}\"");
        else k.DeleteValue(AppName, false);
    }

    private static void CreateShortcut(string path, string target)
    {
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell");
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic lnk = shell.CreateShortcut(path);
        lnk.TargetPath = target;
        lnk.WorkingDirectory = Path.GetDirectoryName(target);
        lnk.Description = "Hiển thị % pin thiết bị Bluetooth trên taskbar";
        lnk.IconLocation = target + ",0";
        lnk.Save();
    }

    private static void TryDo(Action a) { try { a(); } catch { } }
}
