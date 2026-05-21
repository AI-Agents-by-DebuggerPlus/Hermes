using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

internal static class MemoryEmbeddingText
{
    internal const int MaxChars = 4000;

    internal static string ForMemory(MemoryItem item)
    {
        var tags = item.Tags.Count == 0 ? string.Empty : string.Join(' ', item.Tags);
        var raw = $"{item.Type}\n{item.Project}\n{tags}\n{item.Content}".Trim();
        if (raw.Length <= MaxChars)
        {
            return raw;
        }

        return raw[..MaxChars];
    }

    internal static string ContentHash(MemoryItem item) =>
        MemoryVectorIndex.HashText(ForMemory(item));
}
