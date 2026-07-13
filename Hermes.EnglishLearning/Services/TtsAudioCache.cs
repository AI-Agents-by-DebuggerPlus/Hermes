using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Hermes.EnglishLearning.Services;

public static class TtsAudioCache
{
    public static string CacheDirectory =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tts-cache");

    public static string GetPath(string voice, string locale, string text)
    {
        var key = (voice ?? string.Empty).Trim() + "|" +
                  (locale ?? string.Empty).Trim() + "|" +
                  (text ?? string.Empty).Trim();
        var hash = Sha1Hex(key);
        var safeVoice = SanitizeFilePart(voice);
        return Path.Combine(CacheDirectory, safeVoice + "_" + hash + ".mp3");
    }

    public static bool TryGetExisting(string voice, string locale, string text, out string path)
    {
        path = GetPath(voice, locale, text);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    public static void Save(string path, byte[] audio)
    {
        if (audio == null || audio.Length == 0)
        {
            throw new InvalidOperationException("Empty audio payload");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? CacheDirectory);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, audio);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        File.Move(tmp, path);
    }

    private static string Sha1Hex(string s)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }

    private static string SanitizeFilePart(string? voice)
    {
        var v = string.IsNullOrWhiteSpace(voice) ? "voice" : voice!.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            v = v.Replace(c, '_');
        }

        return v.Length > 48 ? v.Substring(0, 48) : v;
    }
}
