using System;
using System.IO;
using System.Text.Json;

namespace BinanceWpfSpotDemoApiTerminal.Helpers
{
    public class CredentialsModel
    {
        public string ApiKey { get; set; } = string.Empty;
        public string SecretKey { get; set; } = string.Empty;
    }

    public static class ConfigManager
    {
        private const string ConfigFileName = "api_credentials.json";
        private static string _resolvedPath = null;

        private static string GetConfigPath()
        {
            if (_resolvedPath != null)
                return _resolvedPath;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentDir = baseDir;

            // Поиск api_credentials.json вверх по дереву папок (до 5 уровней)
            for (int i = 0; i < 5; i++)
            {
                if (currentDir == null) break;
                string checkPath = Path.Combine(currentDir, ConfigFileName);
                if (File.Exists(checkPath))
                {
                    _resolvedPath = checkPath;
                    return _resolvedPath;
                }
                currentDir = Directory.GetParent(currentDir)?.FullName;
            }

            // Если не найден, сохраняем в папке исполняемого файла
            _resolvedPath = Path.Combine(baseDir, ConfigFileName);
            return _resolvedPath;
        }

        public static CredentialsModel LoadCredentials()
        {
            string path = GetConfigPath();
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var creds = JsonSerializer.Deserialize<CredentialsModel>(json);
                    return creds ?? new CredentialsModel();
                }
            }
            catch
            {
                // Игнорируем ошибки загрузки
            }
            return new CredentialsModel();
        }

        public static void SaveCredentials(string apiKey, string secretKey)
        {
            string path = GetConfigPath();
            try
            {
                var creds = new CredentialsModel
                {
                    ApiKey = apiKey ?? string.Empty,
                    SecretKey = secretKey ?? string.Empty
                };
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(creds, options);
                File.WriteAllText(path, json);
            }
            catch
            {
                // Игнорируем ошибки записи
            }
        }
    }
}
