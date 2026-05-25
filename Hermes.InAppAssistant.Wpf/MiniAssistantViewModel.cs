using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Hermes.InAppAssistant.Wpf;

public sealed class MiniAssistantViewModel : AssistantNotifyBase
{
    private readonly AppAssistantService _service;
    private readonly Func<AppAssistantOptions> _getOptions;
    private readonly IAppAssistantContextProvider _contextProvider;
    private readonly List<AssistantChatTurn> _history = [];
    private string _userInput = string.Empty;
    private bool _isOpen;
    private bool _isBusy;
    private string _statusText = "Ask a question about the app.";

    public MiniAssistantViewModel(
        AppAssistantService service,
        Func<AppAssistantOptions> getOptions,
        IAppAssistantContextProvider contextProvider)
    {
        _service = service;
        _getOptions = getOptions;
        _contextProvider = contextProvider;
        ToggleOpenCommand = new AssistantRelayCommand(_ => IsOpen = !IsOpen);
        SendCommand = new AssistantRelayCommand(async _ => await SendAsync(), _ => CanSend);
        ClearCommand = new AssistantRelayCommand(_ => Clear(), _ => Messages.Count > 0 && !IsBusy);
    }

    public ObservableCollection<AssistantChatLineViewModel> Messages { get; } = [];

    public string UserInput
    {
        get => _userInput;
        set
        {
            if (SetField(ref _userInput, value))
            {
                Raise(nameof(CanSend));
            }
        }
    }

    public bool IsOpen
    {
        get => _isOpen;
        set => SetField(ref _isOpen, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                Raise(nameof(CanSend));
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(UserInput);

    public ICommand ToggleOpenCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand ClearCommand { get; }

    public async Task SendAsync()
    {
        var text = UserInput.Trim();
        if (text.Length == 0 || IsBusy)
        {
            return;
        }

        var options = _getOptions();
        if (string.IsNullOrWhiteSpace(options.OpenRouterApiKey))
        {
            StatusText = "Set OpenRouter API key on the Assistant tab.";
            return;
        }

        UserInput = string.Empty;
        AppendLine(isUser: true, text);
        _history.Add(new AssistantChatTurn("user", text));

        IsBusy = true;
        StatusText = "Requesting model…";
        try
        {
            var reply = await _service.AskAsync(options, _contextProvider, _history, text)
                .ConfigureAwait(true);
            AppendLine(isUser: false, reply);
            _history.Add(new AssistantChatTurn("assistant", reply));
            StatusText = "Done.";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
            AppendLine(isUser: false, $"⚠ {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLine(bool isUser, string text) =>
        Messages.Add(new AssistantChatLineViewModel(isUser, text, DateTimeOffset.Now));

    private void Clear()
    {
        Messages.Clear();
        _history.Clear();
        StatusText = "History cleared.";
    }
}
