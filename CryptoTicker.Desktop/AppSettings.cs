using System.IO;
using System.Text.Json;
using CryptoTicker.Core;

namespace CryptoTicker.Desktop;

public sealed class AppSettings
{
    public string Pair { get; set; } = "BTC/USDT";
    public List<string> WatchPairs { get; set; } = [];
    public List<CustomSourceSettings> CustomSources { get; set; } = [];
    public string AiEndpoint { get; set; } = "";
    public string AiModel { get; set; } = "";
    public string AiCredentialTarget { get; set; } = "CryptoTicker.AI";
    public List<PriceAlert> Alerts { get; set; } = [];
    public string UpColor { get; set; } = "#16A34A";
    public string DownColor { get; set; } = "#DC2626";
}

public sealed class CustomSourceSettings
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string PricePath { get; set; } = "";
    public string TimestampPath { get; set; } = "";
    public string SecretHeaderName { get; set; } = "";
    public string CredentialTarget { get; set; } = "";
}

public static class SettingsStore
{
    private static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CryptoTicker", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings() : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
