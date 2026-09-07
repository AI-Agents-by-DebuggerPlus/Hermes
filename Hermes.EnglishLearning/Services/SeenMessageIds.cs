using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Hermes.EnglishLearning.Services;

/// <summary>Dedup lessons/nav across Realtime WS and REST poll.</summary>
internal static class SeenMessageIds
{
    private static readonly ConcurrentDictionary<Guid, long> Seen = new();
    private const int MaxEntries = 400;

    public static bool TryMark(Guid id)
    {
        if (id == Guid.Empty) return true;
        var added = Seen.TryAdd(id, DateTime.UtcNow.Ticks);
        if (added && Seen.Count > MaxEntries)
            Trim();
        return added;
    }

    private static void Trim()
    {
        try
        {
            var list = new List<KeyValuePair<Guid, long>>(Seen);
            list.Sort((a, b) => a.Value.CompareTo(b.Value));
            var remove = list.Count - MaxEntries / 2;
            for (var i = 0; i < remove && i < list.Count; i++)
                Seen.TryRemove(list[i].Key, out _);
        }
        catch
        {
            // ignore
        }
    }
}
