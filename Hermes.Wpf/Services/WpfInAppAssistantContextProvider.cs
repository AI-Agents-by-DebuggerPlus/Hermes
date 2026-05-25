using Hermes.InAppAssistant;

namespace Hermes.Wpf.Services;

public sealed class WpfInAppAssistantContextProvider : IAppAssistantContextProvider
{
    private readonly Func<string> _snapshot;

    public WpfInAppAssistantContextProvider(Func<string> snapshot) =>
        _snapshot = snapshot;

    public string GetLiveContextSnapshot() => _snapshot();
}
