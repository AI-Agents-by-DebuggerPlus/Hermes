namespace Hermes.Wpf.Services;

/// <summary>Outcome of a <c>wsl … bash -lc</c> Hermes invocation.</summary>
public sealed class HermesExecutionResult
{
    public int ExitCode { get; init; }

    /// <summary>Merged stdout and stderr as captured for the terminal buffer.</summary>
    public string CombinedText { get; init; } = string.Empty;

    /// <summary>Last raw stderr line (before the <c>[stderr]</c> terminal prefix).</summary>
    public string? LastStderrLine { get; init; }

    public bool Success => ExitCode == 0;
}
