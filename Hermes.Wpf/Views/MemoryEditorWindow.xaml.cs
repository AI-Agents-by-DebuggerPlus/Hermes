using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using Hermes.Wpf.Models;
using Hermes.Wpf.Services;

namespace Hermes.Wpf.Views;

public partial class MemoryEditorWindow : Window
{
    private readonly ExternalBrainService _brain;
    private readonly LogService _log;

    public MemoryEditorWindow(
        ExternalBrainService brain,
        LogService log,
        MemoryDraft? initialDraft,
        string defaultProject)
    {
        InitializeComponent();
        _brain = brain;
        _log = log;

        TypeCombo.ItemsSource = new[] { "procedural", "semantic", "episodic", "identity" };
        TypeCombo.SelectedItem = NormalizeType(initialDraft?.Type ?? "procedural");

        TagsBox.Text = (initialDraft?.Tags.Count ?? 0) > 0 ? string.Join(", ", initialDraft!.Tags) : string.Empty;

        ProjectBox.Text = string.IsNullOrWhiteSpace(initialDraft?.Project)
            ? (defaultProject ?? string.Empty).Trim()
            : initialDraft!.Project.Trim();

        var imp = Math.Clamp(initialDraft?.Importance ?? 3, 1, 5);
        ImportanceSlider.Value = imp;
        ImportanceLabel.Text = imp.ToString(CultureInfo.InvariantCulture);
        ImportanceSlider.ValueChanged += (_, _) =>
        {
            ImportanceLabel.Text = ((int)Math.Round(ImportanceSlider.Value)).ToString(CultureInfo.InvariantCulture);
        };

        ContentBox.Text = BuildInitialEditorBody(initialDraft);
        Loaded += (_, _) =>
        {
            var p = _brain.ResolveEffectiveMemoryPath().Trim();
            if (p.Length == 0)
            {
                Title = "Save experience";
                return;
            }

            var take = Math.Min(56, p.Length);
            Title = "Save experience · …" + p[^take..];
        };
    }

    private static string NormalizeType(string t)
    {
        t = (t ?? "procedural").Trim().ToLowerInvariant();
        return t is "procedural" or "semantic" or "episodic" or "identity" ? t : "procedural";
    }

    private static string BuildInitialEditorBody(MemoryDraft? d)
    {
        if (d is null || (string.IsNullOrWhiteSpace(d.Problem) && string.IsNullOrWhiteSpace(d.Solution)))
        {
            return "# Title\r\n\r\n## Problem\r\n\r\n\r\n## Solution\r\n\r\n\r\n## Reusable\r\n\r\n";
        }

        var full = new MemoryExtractorService().GenerateMarkdown(d);
        return ExternalBrainMarkdown.MarkdownBodyWithoutYaml(full).TrimEnd() + Environment.NewLine;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        var root = (_brain.ResolveEffectiveMemoryPath() ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            MessageBox.Show(
                this,
                "Укажите папку External Brain (Memory) в настройках или через externalBrain.json.",
                "Hermes",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var type = TypeCombo.SelectedItem as string ?? "procedural";
        type = NormalizeType(type);
        var tags = ParseTags(TagsBox.Text);
        var project = (ProjectBox.Text ?? string.Empty).Trim();
        var importance = (int)Math.Round(ImportanceSlider.Value);
        importance = Math.Clamp(importance, 1, 5);
        var body = ContentBox.Text ?? string.Empty;

        var draft = new MemoryDraft
        {
            Type = type,
            Tags = tags,
            Project = project,
            Importance = importance,
            TimestampUtc = DateTime.UtcNow,
            Problem = string.Empty,
            Solution = string.Empty,
            Reusable = string.Empty,
        };

        var yaml = BuildYamlSection(draft);
        var full = yaml + body.TrimEnd() + Environment.NewLine;

        var sub = MemoryExtractorService.MemorySubfolderForType(type);
        var dir = Path.Combine(root, sub);
        Directory.CreateDirectory(dir);

        var name = MemoryExtractorService.BuildSaveFileName(type, draft.TimestampUtc);
        name = MemoryExtractorService.SanitizeFileName(name);
        var path = Path.Combine(dir, name);
        try
        {
            File.WriteAllText(path, full, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex)
        {
            _log.LogError($"[memory-editor] save failed: {ex.Message}");
            MessageBox.Show(this, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _log.LogInfo($"[memory-editor] saved {path}");
        _brain.RestartWatcherAndReload("memory-editor");
        MessageBox.Show(this, path, "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private static string BuildYamlSection(MemoryDraft d)
    {
        var type = NormalizeType(d.Type);
        var imp = Math.Clamp(d.Importance, 1, 5);
        var stamp = d.TimestampUtc == default ? DateTime.UtcNow : d.TimestampUtc;
        if (stamp.Kind == DateTimeKind.Local)
        {
            stamp = stamp.ToUniversalTime();
        }
        else if (stamp.Kind == DateTimeKind.Unspecified)
        {
            stamp = DateTime.SpecifyKind(stamp, DateTimeKind.Utc);
        }

        var iso = stamp.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var tagsJson = System.Text.Json.JsonSerializer.Serialize(
            d.Tags.Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(static t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var projectEsc = (d.Project ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ").Trim();

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.Append("type: ").AppendLine(type);
        sb.Append("timestamp: ").AppendLine(iso);
        sb.Append("tags: ").AppendLine(tagsJson);
        sb.Append("project: ").AppendLine(projectEsc);
        sb.Append("importance: ").AppendLine(imp.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("---");
        sb.AppendLine();
        return sb.ToString();
    }

    private static List<string> ParseTags(string text)
    {
        return (text ?? string.Empty)
            .ReplaceLineEndings("\n")
            .Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static s => s.TrimStart('#').Trim().ToLowerInvariant())
            .Where(static s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
