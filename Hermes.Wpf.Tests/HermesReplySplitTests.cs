using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public class HermesReplySplitTests
{
    [Fact]
    public void Parse_SplitsInfoAndSpeak()
    {
        var raw =
            """
            [info]
            Полный отчёт по задаче.
            Вторая строка.

            [speak]
            {"ru":"Кратко готово."}
            {"ru":"Проверьте терминал."}
            """;

        var parts = HermesReplySplit.Parse(raw);
        Assert.True(parts.HasMarkers);
        Assert.Equal("Полный отчёт по задаче.\nВторая строка.", parts.Info);
        Assert.Equal("{\"ru\":\"Кратко готово.\"}\n{\"ru\":\"Проверьте терминал.\"}", parts.Speak);
    }

    [Fact]
    public void Parse_SplitsInfoAndVoice()
    {
        var raw =
            """
            [info]
            Читать это.

            [Voice]
            {"ru":"Говорить это."}
            [/Voice]
            """;

        var parts = HermesReplySplit.Parse(raw);
        Assert.True(parts.HasMarkers);
        Assert.Equal("Читать это.", parts.Info);
        Assert.Equal("{\"ru\":\"Говорить это.\"}", parts.Speak);
    }

    [Fact]
    public void ForChatDisplay_PrefersInfo()
    {
        var raw =
            """
            [info]
            Читать это.

            [speak]
            {"ru":"Говорить это."}
            """;

        Assert.Equal("Читать это.", HermesReplySplit.ForChatDisplay(raw));
    }

    [Fact]
    public void ForSpeakSource_PrefersSpeak()
    {
        var raw =
            """
            [info]
            Длинный текст.

            [speak]
            {"ru":"Коротко."}
            """;

        Assert.Equal("{\"ru\":\"Коротко.\"}", HermesReplySplit.ForSpeakSource(raw));
    }

    [Fact]
    public void ForSpeakSource_ReturnsVoiceEnvelope()
    {
        var raw =
            """
            Currently in development.

            [Voice]
            {"en":"Currently in development."}
            [/Voice]
            """;

        var speak = HermesReplySplit.ForSpeakSource(raw);
        Assert.Contains("[Voice]", speak, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{\"en\":\"Currently in development.\"}", speak, StringComparison.Ordinal);
        Assert.Equal("Currently in development.", HermesReplySplit.ForChatDisplay(raw).Trim());
    }

    [Fact]
    public void WithoutMarkers_Passthrough()
    {
        const string raw = "Просто ответ без маркеров.";
        Assert.False(HermesReplySplit.Parse(raw).HasMarkers);
        Assert.Equal(raw, HermesReplySplit.ForChatDisplay(raw));
        Assert.Equal(raw, HermesReplySplit.ForSpeakSource(raw));
    }

    [Fact]
    public void ToSupabaseContent_WrapsVoiceEnvelope()
    {
        var content = BilingualSegmentFormatter.ToSupabaseContent("{\"ru\":\"Привет\"}");
        Assert.StartsWith("[Voice]", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("{\"ru\":\"Привет\"}", content, StringComparison.Ordinal);
        Assert.EndsWith("[/Voice]", content.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
