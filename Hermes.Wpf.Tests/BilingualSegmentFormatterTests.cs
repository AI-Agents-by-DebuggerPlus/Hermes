using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public class BilingualSegmentFormatterTests
{
    [Fact]
    public void FormatPlainAsSentenceLines_SplitsLatinBrands()
    {
        var result = BilingualSegmentFormatter.FormatPlainAsSentenceLines(
            "Кефир с cardamom полезен. Это BioStack.");
        Assert.True(
            result.Contains("\"en\"", StringComparison.Ordinal),
            "expected en fragments, got: " + result);
        Assert.Contains("cardamom", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BioStack", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains('\n', result);
    }

    [Fact]
    public void ToSupabaseContent_ReformatsSingleHugeRuObject()
    {
        var input = "{\"ru\":\"Грамотный подход. Спасательный жилет passive mode это решение.\"}";
        var result = BilingualSegmentFormatter.ToSupabaseContent(input);
        Assert.Contains('\n', result);
        Assert.DoesNotContain("{\"ru\":\"Грамотный подход. Спасательный", result);
        Assert.Contains("\"en\":\"passive mode\"", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldPublishAsRawJson_FalseForSingleRuBlob()
    {
        var input = "{\"ru\":\"Длинный текст без отдельных TTS-строк и с запасом длины чтобы считался дампом а не коротким ответом для озвучки Android.\"}";
        Assert.False(BilingualSegmentFormatter.ShouldPublishAsRawJson(input));
    }

    [Fact]
    public void ToSupabaseContent_PreservesScreenshotTtsProtocol()
    {
        var input =
            "{\"ru\":\"скриншот создан\"}\n" +
            "file:///D:/tmp/a.png";
        Assert.True(BilingualSegmentFormatter.LooksLikeAndroidChatTtsProtocol(input));
        var result = BilingualSegmentFormatter.ToSupabaseContent(input);
        Assert.StartsWith("{\"ru\":\"скриншот создан\"}", result);
        Assert.Contains("file:///D:/tmp/a.png", result);
    }
}
