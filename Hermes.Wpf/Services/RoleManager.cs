using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public sealed class RoleManager
{
    private static readonly Dictionary<string, AgentRole> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["trader"] = AgentRole.Trader,
        ["trading"] = AgentRole.Trader,
        ["трейдинг"] = AgentRole.Trader,
        ["трейдер"] = AgentRole.Trader,
        ["dev"] = AgentRole.Developer,
        ["developer"] = AgentRole.Developer,
        ["разработчик"] = AgentRole.Developer,
        ["код"] = AgentRole.Developer,
        ["english"] = AgentRole.EnglishTutor,
        ["английский"] = AgentRole.EnglishTutor,
        ["репетитор"] = AgentRole.EnglishTutor,
        ["manager"] = AgentRole.PersonalManager,
        ["productivity"] = AgentRole.PersonalManager,
        ["эффективность"] = AgentRole.PersonalManager,
        ["задачи"] = AgentRole.PersonalManager,
        ["utilities"] = AgentRole.UtilitiesManager,
        ["utility"] = AgentRole.UtilitiesManager,
        ["жкх"] = AgentRole.UtilitiesManager,
        ["водоканал"] = AgentRole.UtilitiesManager,
        ["коммунал"] = AgentRole.UtilitiesManager,
        ["biohacker"] = AgentRole.Biohacker,
        ["biohacking"] = AgentRole.Biohacker,
        ["биохакер"] = AgentRole.Biohacker,
        ["биохакинг"] = AgentRole.Biohacker,
        ["здоровье"] = AgentRole.Biohacker,
        ["бады"] = AgentRole.Biohacker,
        ["ноотропы"] = AgentRole.Biohacker,
        ["самочувствие"] = AgentRole.Biohacker,
        ["universal"] = AgentRole.Universal,
        ["общий"] = AgentRole.Universal,
        ["режим агента"] = AgentRole.Universal,
    };

    private readonly LogService _log;
    private readonly Func<HermesSettings> _settings;
    private readonly RoleAwareMemoryRouter _memoryRouter;
    private readonly RoleSkillIndex _skillIndex;

    public RoleManager(
        LogService log,
        Func<HermesSettings> settings,
        RoleAwareMemoryRouter memoryRouter,
        RoleSkillIndex skillIndex)
    {
        _log = log;
        _settings = settings;
        _memoryRouter = memoryRouter;
        _skillIndex = skillIndex;
    }

    public AgentRole CurrentRole { get; private set; } = AgentRole.Universal;

    public RoleSession? CurrentSession { get; private set; }

    public event EventHandler<RoleChangedEventArgs>? RoleChanged;

    public void SwitchRole(AgentRole newRole)
    {
        if (CurrentRole == newRole)
        {
            return;
        }

        var previous = CurrentRole;
        CurrentRole = newRole;
        _memoryRouter.CurrentRole = newRole;
        CurrentSession = new RoleSession { Role = newRole };
        _ = _skillIndex.LoadAsync();
        _skillIndex.GetSkillsForRole(newRole);
        RoleChanged?.Invoke(this, new RoleChangedEventArgs(previous, newRole));
        _log.LogInfo($"[role-manager] Switched to {newRole}");
    }

    public bool TrySwitchRole(string roleNameOrAlias)
    {
        var key = (roleNameOrAlias ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return false;
        }

        if (!Aliases.TryGetValue(key, out var role))
        {
            return false;
        }

        SwitchRole(role);
        return true;
    }

    public bool TrySwitchRoleFromMessage(string message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        foreach (var alias in Aliases.Keys.OrderByDescending(static k => k.Length))
        {
            if (text.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || text.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                if (Aliases.TryGetValue(alias, out var role))
                {
                    SwitchRole(role);
                    return true;
                }
            }
        }

        return false;
    }

    public void RecordTurn(string? userMessage, string? skillId = null) =>
        CurrentSession?.RecordTurn(userMessage, skillId);

    public Task SaveCurrentRoleAsync()
    {
        _settings().PersistedAgentRole = CurrentRole.ToString();
        return Task.CompletedTask;
    }

    public void LoadCurrentRoleFromSettings()
    {
        var raw = (_settings().PersistedAgentRole ?? string.Empty).Trim();
        if (Enum.TryParse<AgentRole>(raw, ignoreCase: true, out var role))
        {
            CurrentRole = role;
        }
        else if (_settings().TradingModeEnabled)
        {
            CurrentRole = AgentRole.Trader;
        }
        else if (_settings().EnglishTutorModeEnabled)
        {
            CurrentRole = AgentRole.EnglishTutor;
        }
        else
        {
            CurrentRole = AgentRole.Universal;
        }

        _memoryRouter.CurrentRole = CurrentRole;
        CurrentSession = new RoleSession { Role = CurrentRole };
    }

    public static IReadOnlyList<AgentRole> AllRoles { get; } =
        Enum.GetValues<AgentRole>().ToArray();

    public static string DisplayName(AgentRole role) =>
        role switch
        {
            AgentRole.Trader => "Trader",
            AgentRole.Developer => "Developer",
            AgentRole.EnglishTutor => "English Tutor",
            AgentRole.PersonalManager => "Personal Manager",
            AgentRole.UtilitiesManager => "Utilities Manager",
            AgentRole.Biohacker => "Biohacker",
            _ => "Universal",
        };

    public static string ColorHex(AgentRole role) =>
        role switch
        {
            AgentRole.Developer => "#4A9EFF",
            AgentRole.Trader => "#4CAF50",
            AgentRole.EnglishTutor => "#FF9800",
            AgentRole.PersonalManager => "#9C27B0",
            AgentRole.UtilitiesManager => "#607D8B",
            AgentRole.Biohacker => "#00BCD4",
            _ => "#808080",
        };
}
