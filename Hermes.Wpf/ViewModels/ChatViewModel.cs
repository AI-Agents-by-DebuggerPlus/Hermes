using System.Collections.ObjectModel;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.ViewModels;

public sealed class ChatViewModel : BaseViewModel
{
    private string _userInput = string.Empty;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public string UserInput
    {
        get => _userInput;
        set => SetProperty(ref _userInput, value);
    }
}
