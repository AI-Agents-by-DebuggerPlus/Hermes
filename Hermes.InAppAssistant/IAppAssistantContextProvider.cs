namespace Hermes.InAppAssistant;

/// <summary>Supplies live application state appended to the assistant system prompt.</summary>
public interface IAppAssistantContextProvider
{
    string GetLiveContextSnapshot();
}
