namespace Hermes.Wpf.Models;

public sealed class WslMemoryFileSnapshot
{
    public string FileName { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public DateTime LastWriteTimeLocal { get; init; }

    public string RawContent { get; init; } = string.Empty;

    public IReadOnlyList<string> Entries { get; init; } = [];

    public int EntryCount => Entries.Count;

    public string PreviewLine
    {
        get
        {
            var source = Entries.Count > 0 ? Entries[0] : RawContent;
            foreach (var line in source.ReplaceLineEndings("\n").Split('\n'))
            {
                var t = line.Trim();
                if (t.Length > 0)
                {
                    return t.Length > 120 ? t[..120] + "…" : t;
                }
            }

            return FileName;
        }
    }
}
