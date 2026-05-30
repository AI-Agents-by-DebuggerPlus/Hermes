using System.IO;
using System.Text.Json;

namespace Hermes.BinanceDemoSpotTerminal.Helpers;

public sealed class CredentialsModel
{
    public string ApiKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}

public static class ConfigManager
{
    private const string ConfigFileName = "api_credentials.json";

    private static string ConfigPath
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HermesBinanceDemoSpot");
            Directory.CreateDirectory(root);
            return Path.Combine(root, ConfigFileName);
        }
    }

    public static CredentialsModel LoadCredentials()
    {
        try
        {
            var path = ConfigPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<CredentialsModel>(json) ?? new CredentialsModel();
            }
        }
        catch
        {
            // ignore load errors
        }

        return new CredentialsModel();
    }

    public static void SaveCredentials(string apiKey, string secretKey)
    {
        try
        {
            var creds = new CredentialsModel
            {
                ApiKey = apiKey ?? string.Empty,
                SecretKey = secretKey ?? string.Empty,
            };
            var json = JsonSerializer.Serialize(creds, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // ignore save errors
        }
    }
}
