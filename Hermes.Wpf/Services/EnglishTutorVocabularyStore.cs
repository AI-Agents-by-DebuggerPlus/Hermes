using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>Merges model tutor session JSON (HERMES_TUTOR_SESSION_* markers or legacy ``` fence) into local telemetry.</summary>
public sealed class EnglishTutorVocabularyStore
{
    private static readonly Regex SessionFenceLegacy = new(
        @"```english-tutor-session\s*\r?\n(?<payload>.*?)\r?\n```",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly string _storePath;

    /// <remarks>Keyed by lowercase lemma EN.</remarks>
    private Dictionary<string, EnglishTutorWordState> Words { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public EnglishTutorVocabularyStore()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HermesWpf");
        Directory.CreateDirectory(root);
        _storePath = Path.Combine(root, "english_tutor_vocab.json");
        LoadFromDiskSync();
    }

    public string CompactSummaryRu()
    {
        lock (Words)
        {
            var n = Words.Values.Count(static w =>
                string.Equals(w.Tier, "known", StringComparison.OrdinalIgnoreCase));
            var r = Words.Values.Count(static w =>
                string.Equals(w.Tier, "review", StringComparison.OrdinalIgnoreCase));
            var l = Words.Values.Count(static w =>
                string.Equals(w.Tier, "learning", StringComparison.OrdinalIgnoreCase));
            if (Words.Count == 0)
            {
                return "(пока нет сохранённых слов с прошлых занятий)";
            }

            return $"слова учтены: усвоено≈{n}, на повторе={r}, в изучении={l}.";
        }
    }



    /// <summary>Median-ish exposures для learning/leads (rough proxy «сколько нужно показов»).</summary>

    public string ExposureStatsRu()

    {

        lock (Words)

        {

            var learning = Words.Values

                .Where(static w =>

                    string.Equals(w.Tier, "learning", StringComparison.OrdinalIgnoreCase)

                    || string.Equals(w.Tier, "review", StringComparison.OrdinalIgnoreCase))

                .Select(static w => w.ExposureCount)

                .Where(static n => n > 0)

                .Order()

                .ToList();

            if (learning.Count == 0)

            {

                return string.Empty;

            }



            var med = learning[learning.Count / 2];

            return $"ориентировочное число повторений/показов для «трудных» слов (медиана по активным в словаре): ≈{med}.";

        }

    }



    public async Task TryMergeAssistantTailAsync(string combinedAssistantText)

    {

        if (string.IsNullOrWhiteSpace(combinedAssistantText))

        {

            return;

        }



        var text = (combinedAssistantText ?? string.Empty).ReplaceLineEndings("\n");

        var payload = TryExtractMarkerPayload(text) ?? TryExtractLegacyFencePayload(text);
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }



        TutorSessionRoot? dto;

        try

        {

            dto = JsonSerializer.Deserialize<TutorSessionRoot>(

                payload.Trim(),

                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip });

        }

        catch

        {

            return;

        }



        if (dto?.Words is null && dto?.PerWordNote is null)

        {

            return;

        }



        await _ioGate.WaitAsync().ConfigureAwait(false);

        try

        {

            lock (Words)

            {

                if (dto!.Words is not null)


                {

                    ApplyBucket(dto.Words.Mastered, "known");

                    ApplyBucket(dto.Words.NeedsReview, "review");

                    ApplyBucket(dto.Words.Learning, "learning");

                }



                if (dto.PerWordNote is not null)

                {

                    foreach (var (raw, pv) in dto.PerWordNote)

                    {

                        var key = CanonicalLemma(raw);

                        if (key.Length == 0)

                        {

                            continue;

                        }



                        UpsertBare(key);

                        var w = Words[key];

                        var exp = pv?.Exposures ?? 1;

                        w.ExposureCount += Math.Max(0, exp);

                        if (pv?.RecallHit is true)

                        {

                            w.SuccessStreak++;

                        }

                        else if (pv?.RecallHit is false)

                        {

                            w.SuccessStreak = 0;

                        }



                        w.LastUtc = DateTime.UtcNow;

                    }

                }

            }



            await SaveUnsafeAsync().ConfigureAwait(false);

        }

        finally

        {

            _ioGate.Release();

        }

    }



    private void LoadFromDiskSync()
    {
        try
        {
            if (!File.Exists(_storePath))
            {
                return;
            }

            var txt = File.ReadAllText(_storePath);
            var root = JsonSerializer.Deserialize<StoreDiskRoot>(
                    txt,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (root?.Lemmas is null)
            {
                return;
            }

            lock (Words)
            {
                Words.Clear();
                foreach (var w in root.Lemmas.Where(static z => z.Lemma.Length > 0))
                {
                    Words[CanonicalLemma(w.Lemma)] = w;
                }
            }
        }
        catch
        {
            lock (Words)
            {
                Words.Clear();
            }
        }
    }



    private async Task SaveUnsafeAsync()

    {

        StoreDiskRoot root;

        lock (Words)

        {

            root = new StoreDiskRoot { Lemmas = [.. Words.Values] };

        }



        await using var fs = File.Create(_storePath);

        await JsonSerializer.SerializeAsync(

                fs,

                root,

                new JsonSerializerOptions { WriteIndented = true })

            .ConfigureAwait(false);

    }



    private static string? TryExtractMarkerPayload(string text)

    {

        const string begin = "HERMES_TUTOR_SESSION_BEGIN";

        const string end = "HERMES_TUTOR_SESSION_END";

        var i = text.IndexOf(begin, StringComparison.OrdinalIgnoreCase);

        var j = text.IndexOf(end, StringComparison.OrdinalIgnoreCase);

        if (i < 0 || j < 0 || j <= i)

        {

            return null;

        }

        var slice = text[(i + begin.Length)..j].Trim();

        var fb = slice.IndexOf('{');

        var lb = slice.LastIndexOf('}');

        if (fb < 0 || lb < fb)

        {

            return null;

        }

        return slice[fb..(lb + 1)];

    }



    private static string? TryExtractLegacyFencePayload(string text)

    {

        var m = SessionFenceLegacy.Match(text);

        return m.Success ? m.Groups["payload"].Value : null;

    }



    private void ApplyBucket(IReadOnlyList<string>? list, string tier)

    {

        if (list is null)

        {

            return;

        }



        foreach (var raw in list)

        {

            var key = CanonicalLemma(raw);

            if (key.Length == 0)

            {

                continue;

            }



            UpsertBare(key);

            Words[key].Tier = tier.ToLowerInvariant();

            Words[key].LastUtc = DateTime.UtcNow;

        }

    }



    private static string CanonicalLemma(string s) =>

        string.IsNullOrWhiteSpace(s)

            ? string.Empty

            : s.Trim().ToLowerInvariant();



    private void UpsertBare(string key)

    {

        if (!Words.TryGetValue(key, out var w))

        {

            Words[key] = new EnglishTutorWordState { Lemma = key, Tier = "learning", ExposureCount = 0 };

        }

        else if (string.IsNullOrWhiteSpace(w.Lemma))

        {

            w.Lemma = key;

        }

    }



    private sealed class StoreDiskRoot

    {

        public List<EnglishTutorWordState>? Lemmas { get; set; }

    }



    private sealed class TutorSessionRoot

    {

        [JsonPropertyName("phase")]

        public string? Phase { get; set; }



        [JsonPropertyName("placement_index")]

        public int PlacementIndex { get; set; }



        [JsonPropertyName("words")]

        public SessionWords? Words { get; set; }



        [JsonPropertyName("per_word_note")]

        public Dictionary<string, TutorWordNote>? PerWordNote { get; set; }



        internal sealed class SessionWords

        {

            [JsonPropertyName("mastered")]

            public List<string>? Mastered { get; set; }



            [JsonPropertyName("needs_review")]

            public List<string>? NeedsReview { get; set; }



            [JsonPropertyName("learning")]

            public List<string>? Learning { get; set; }

        }



        internal sealed class TutorWordNote

        {

            [JsonPropertyName("exposures")]

            public int? Exposures { get; set; }



            [JsonPropertyName("recall_hit")]

            public bool? RecallHit { get; set; }

        }

    }

}

