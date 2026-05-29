using System.Text.Json;

namespace Hermes.SpotTerminal.Data.Persistence;

public static class AtomicJsonFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Save<T>(string filePath, T value)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
        if (File.Exists(filePath))
        {
            File.Replace(temp, filePath, null);
        }
        else
        {
            File.Move(temp, filePath);
        }
    }

    public static bool TryLoad<T>(string filePath, out T? value)
    {
        value = default;
        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(filePath), JsonOptions);
            return value is not null;
        }
        catch
        {
            return false;
        }
    }
}
