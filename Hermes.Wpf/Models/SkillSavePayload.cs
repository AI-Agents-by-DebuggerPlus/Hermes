namespace Hermes.Wpf.Models;

public sealed record SkillSavePayload(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<string> Triggers,
    string Kind,
    string ScriptBody,
    string ScriptExtension,
    string OutboundPromptBlock,
    string TestCommand);
