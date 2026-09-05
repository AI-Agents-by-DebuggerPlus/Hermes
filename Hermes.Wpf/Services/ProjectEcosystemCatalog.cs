using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

/// <summary>
/// Groups Hermes projects and companion apps (trading, English learning, …).
/// Matching is by folder/name hints; override via <see cref="ProjectUiMeta.EcosystemId"/>.
/// </summary>
public static class ProjectEcosystemCatalog
{
    public static IReadOnlyList<ProjectEcosystemInfo> All { get; } =
    [
        new ProjectEcosystemInfo
        {
            Id = "trading",
            Title = "Трейдинг",
            AccentHex = "#F0B90B",
            ProjectNameHints =
            [
                "mt5", "terminal", "trading", "binance", "futures", "spot", "density", "screener", "prop", "analytics",
            ],
            Apps =
            [
                new ProjectRelatedAppInfo
                {
                    Id = "binance-futures",
                    Title = "Binance Demo Futures",
                    Description = "USDT-M Futures Demo терминал",
                    ExeFileName = "Hermes.BinanceDemoFuturesTerminal.exe",
                },
                new ProjectRelatedAppInfo
                {
                    Id = "binance-spot",
                    Title = "Binance Demo Spot",
                    Description = "Spot Demo терминал",
                    ExeFileName = "Hermes.BinanceDemoSpotTerminal.exe",
                },
                new ProjectRelatedAppInfo
                {
                    Id = "hwt",
                    Title = "HermesWpfTerminal (HWT)",
                    Description = "MT5 GUI terminal",
                    ExeFileName = "HermesWpfTerminal.exe",
                    DevRelativeExePaths =
                    [
                        @"Hermes.MT5\WpfGuiControllerTest\WpfTestApp\bin\Debug\net8.0-windows\HermesWpfTerminal.exe",
                        @"Hermes.MT5\WpfGuiControllerTest\WpfTestApp\bin\Release\net8.0-windows\HermesWpfTerminal.exe",
                    ],
                },
                new ProjectRelatedAppInfo
                {
                    Id = "remote-terminal",
                    Title = "Remote Terminal",
                    Description = "Удалённый просмотр HWT / логов",
                    ExeFileName = "Hermes.RemoteTerminal.exe",
                    DevRelativeExePaths =
                    [
                        @"Hermes.RemoteTerminal\bin\Debug\net8.0-windows\Hermes.RemoteTerminal.exe",
                        @"Hermes.RemoteTerminal\bin\Release\net8.0-windows\Hermes.RemoteTerminal.exe",
                    ],
                },
            ],
        },
        new ProjectEcosystemInfo
        {
            Id = "english",
            Title = "Английский язык",
            AccentHex = "#3FB950",
            ProjectNameHints =
            [
                "english", "tutor", "flashcard", "learning", "vocab", "lesson",
            ],
            Apps =
            [
                new ProjectRelatedAppInfo
                {
                    Id = "english-learning",
                    Title = "English Learning",
                    Description = "Уроки / навигация",
                    ExeFileName = "Hermes.EnglishLearning.exe",
                    DevRelativeExePaths =
                    [
                        @"Hermes.EnglishLearning\bin\Debug\net8.0-windows\Hermes.EnglishLearning.exe",
                        @"Hermes.EnglishLearning\bin\Release\net8.0-windows\Hermes.EnglishLearning.exe",
                    ],
                },
                new ProjectRelatedAppInfo
                {
                    Id = "english-tutor",
                    Title = "English Tutor Client",
                    Description = "Удалённый клиент репетитора",
                    ExeFileName = "Hermes.EnglishTutorClient.exe",
                    DevRelativeExePaths =
                    [
                        @"Hermes.EnglishTutorClient\bin\Debug\net48\Hermes.EnglishTutorClient.exe",
                        @"Hermes.EnglishTutorClient\bin\Release\net48\Hermes.EnglishTutorClient.exe",
                    ],
                },
                new ProjectRelatedAppInfo
                {
                    Id = "english-xp",
                    Title = "English Learning XP",
                    Description = "Клиент для WinXP",
                    ExeFileName = "Hermes.EnglishLearning.Xp.exe",
                    DevRelativeExePaths =
                    [
                        @"Hermes.EnglishLearning.Xp\bin\Debug\net40\Hermes.EnglishLearning.Xp.exe",
                        @"Hermes.EnglishLearning.Xp\bin\Release\net40\Hermes.EnglishLearning.Xp.exe",
                    ],
                },
            ],
        },
        new ProjectEcosystemInfo
        {
            Id = "ops",
            Title = "Операции / быт",
            AccentHex = "#58A6FF",
            ProjectNameHints =
            [
                "reni", "water", "vodokanal", "bio", "biohacker", "wordpress", "gallery", "personal",
            ],
            Apps =
            [
                new ProjectRelatedAppInfo
                {
                    Id = "wordpress-gallery",
                    Title = "WordPress Gallery",
                    Description = "Окно галереи в Command Center",
                    ExeFileName = null,
                },
            ],
        },
        new ProjectEcosystemInfo
        {
            Id = "platform",
            Title = "Платформа Hermes",
            AccentHex = "#A371F7",
            ProjectNameHints =
            [
                "hermes", "agent", "wpf", "cli",
            ],
            Apps =
            [
                new ProjectRelatedAppInfo
                {
                    Id = "command-center",
                    Title = "Hermes Command Center",
                    Description = "Это окно",
                    ExeFileName = "Hermes.Wpf.exe",
                },
            ],
        },
    ];

    public static ProjectEcosystemInfo? FindById(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));

    public static ProjectEcosystemInfo? Resolve(HermesProject project, ProjectUiMeta? meta)
    {
        if (!string.IsNullOrWhiteSpace(meta?.EcosystemId))
        {
            return FindById(meta.EcosystemId) ?? MatchByHints(project);
        }

        return MatchByHints(project);
    }

    private static ProjectEcosystemInfo? MatchByHints(HermesProject project)
    {
        var name = project.Name.ToLowerInvariant();
        foreach (var eco in All)
        {
            if (eco.ProjectNameHints.Any(h => name.Contains(h, StringComparison.Ordinal)))
            {
                return eco;
            }
        }

        // Path hints: skip "platform" (repo root often contains "Hermes" and would swallow everything).
        var path = project.WindowsPath.ToLowerInvariant();
        foreach (var eco in All)
        {
            if (string.Equals(eco.Id, "platform", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (eco.ProjectNameHints.Any(h => path.Contains(h, StringComparison.Ordinal)))
            {
                return eco;
            }
        }

        return null;
    }
}
