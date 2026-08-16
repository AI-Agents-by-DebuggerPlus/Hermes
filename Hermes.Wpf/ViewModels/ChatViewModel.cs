using System.Collections.ObjectModel;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.ViewModels;

public sealed class ChatViewModel : BaseViewModel
{
    private string _userInput = string.Empty;

    public ChatViewModel()
    {
        PendingAttachments.CollectionChanged += (_, __) =>
        {
            RaisePropertyChanged(nameof(HasPendingAttachments));
            RaisePropertyChanged(nameof(PendingAttachmentsSummary));
        };
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public ObservableCollection<ChatAttachment> PendingAttachments { get; } = [];

    public bool HasPendingAttachments => PendingAttachments.Count > 0;

    public string PendingAttachmentsSummary =>
        PendingAttachments.Count == 0
            ? string.Empty
            : string.Join(", ", PendingAttachments.Select(a => a.DisplayName));

    public string UserInput
    {
        get => _userInput;
        set => SetProperty(ref _userInput, value);
    }

    public void ClearPendingAttachments() => PendingAttachments.Clear();
}
