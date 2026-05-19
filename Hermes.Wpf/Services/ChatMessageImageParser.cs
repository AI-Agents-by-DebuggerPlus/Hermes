using System.Text.RegularExpressions;
using Hermes.Wpf.Models;

namespace Hermes.Wpf.Services;

public static class ChatMessageImageParser
{
    private static readonly Regex ImageSuffix = new(
        @"\s*\[image:(.+?)\]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Splits <c>[image:path]</c> suffix into <see cref="ChatMessage.ImagePath"/> for UI binding.</summary>
    public static ChatMessage Normalize(ChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.ImagePath))
        {
            return message;
        }

        var match = ImageSuffix.Match(message.Text ?? string.Empty);
        if (!match.Success)
        {
            return message;
        }

        var path = match.Groups[1].Value.Trim();
        var text = (message.Text ?? string.Empty)[..match.Index].TrimEnd();
        return new ChatMessage
        {
            Role = message.Role,
            Text = text,
            ImagePath = path,
            Timestamp = message.Timestamp,
        };
    }
}
