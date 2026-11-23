namespace PinDisplay;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--list"))
        {
            // Chế độ kiểm tra: in danh sách ra console rồi thoát
            foreach (var d in BatteryReader.Read())
                Console.WriteLine($"{d.Name} | {d.Percent}% | {(d.Connected ? "connected" : "disconnected")} | {d.Kind} | {d.Address}");
            return;
        }

        if (args.Contains("--uninstall"))
        {
            // Gọi từ Settings > Apps > Installed apps
            Installer.Uninstall();
            return;
        }

        // App chạy nền: lỗi bất ngờ chỉ ghi log, không bật hộp thoại .NET làm phiền người dùng
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError(e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        if (args.Length >= 2 && args[0] == "--preview")
        {
            // Chế độ kiểm tra giao diện: xuất ảnh flyout + icon ra thư mục
            Directory.CreateDirectory(args[1]);
            TrayApp.SavePreview(args[1]);
            return;
        }

        using var mutex = new Mutex(false, @"Local\PinDisplay_SingleInstance");
        bool owned;
        try { owned = mutex.WaitOne(args.Contains("--replace") ? 5000 : 0); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned) return;

        try { Application.Run(new TrayApp(justInstalled: args.Contains("--installed"))); }
        finally { mutex.ReleaseMutex(); }
    }

    private static void LogError(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Settings.Folder);
            var path = Path.Combine(Settings.Folder, "error.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }
}
