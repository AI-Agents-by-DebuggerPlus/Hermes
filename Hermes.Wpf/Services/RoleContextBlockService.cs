using System.Text;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class RoleContextBlockService
{
    private readonly Func<HermesSettings> _settings;

    public RoleContextBlockService(Func<HermesSettings> settings) => _settings = settings;

    public bool IsEnabled => _settings().RoleContextBlockEnabled;

    public string BuildRoleContextBlock(
        AgentRole role,
        RoleSession? session,
        int memoryItemsLoaded = 0,
        IReadOnlyList<string>? memoryTags = null)
    {
        if (role == AgentRole.Universal || session is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"--- ROLE CONTEXT: {RoleManager.DisplayName(role)} ---");
        var local = session.StartedAt.ToLocalTime();
        sb.AppendLine($"Active since: {local:HH:mm} ({session.TurnCount} turns)");

        if (session.RecentSkillIds.Count > 0)
        {
            sb.AppendLine("Recent skills used: " + string.Join(", ", session.RecentSkillIds.Take(5)));
        }

        if (session.RecentTopics.Count > 0)
        {
            sb.AppendLine("Recent topics: " + string.Join("; ", session.RecentTopics.Take(4)));
        }

        if (memoryItemsLoaded > 0)
        {
            var tags = memoryTags is { Count: > 0 }
                ? string.Join(", ", memoryTags.Take(8))
                : RoleAwareMemoryRouterForTags(role);
            sb.AppendLine($"Relevant memory loaded: {memoryItemsLoaded} items (tags: {tags})");
        }

        sb.AppendLine("---");
        return sb.ToString().TrimEnd();
    }

    private static string RoleAwareMemoryRouterForTags(AgentRole role) =>
        role switch
        {
            AgentRole.Trader => "trading, strategy, risk",
            AgentRole.Developer => "dotnet, code, wpf",
            AgentRole.EnglishTutor => "english, vocabulary",
            AgentRole.PersonalManager => "task, productivity",
            _ => "general",
        };
}
