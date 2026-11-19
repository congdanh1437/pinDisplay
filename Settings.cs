using System.Text.Json;

namespace PinDisplay;

public enum TrayIconStyle { Number, Battery }

/// <summary>Cài đặt người dùng, lưu ở %APPDATA%\PinDisplay\settings.json.</summary>
public sealed class Settings
{
    public const int DefaultThreshold = 20;

    public UiStyle Ui { get; set; } = UiStyle.Glass;
    public TrayIconStyle IconStyle { get; set; } = TrayIconStyle.Number;
    public bool Notify { get; set; } = true;
    /// <summary>Ngưỡng cảnh báo theo địa chỉ thiết bị; 0 = không cảnh báo.</summary>
    public Dictionary<string, int> Thresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int ThresholdFor(string address) =>
        Thresholds.TryGetValue(address, out var v) ? v : DefaultThreshold;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PinDisplay");

    private static string FilePath => Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new();
                s.Thresholds = new(s.Thresholds, StringComparer.OrdinalIgnoreCase);
                return s;
            }
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { }
    }
}
