namespace Hermes.Wpf.Services;

public enum DesktopVisionIntent
{
    /// <summary>Default after «скриншот»: rich context for agent, brief line in chat.</summary>
    InternalCapture,

    /// <summary>User asked for a detailed on-screen report.</summary>
    DescribeScreen,

    /// <summary>User asked to focus / work with a specific window — list interactive regions.</summary>
    FocusWindow,
}
