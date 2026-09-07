using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public sealed class HermesCliStderrClassifierTests
{
    [Theory]
    [InlineData("I received the test message. Code : 51.")]
    [InlineData("Session resumed successfully.")]
    [InlineData("Done.")]
    public void NormalAssistantText_IsNotSystemError(string line)
    {
        Assert.False(HermesCliStderrClassifier.LooksLikeSystemError(line));
        Assert.Equal(line, HermesCliStderrClassifier.FormatForUi(line));
    }

    [Theory]
    [InlineData("Error: No such file or directory")]
    [InlineData("Permission denied")]
    [InlineData("Timed out waiting for Hermes (wsl/bash).")]
    [InlineData("Failed to start browser_vision tool")]
    [InlineData("Traceback (most recent call last):")]
    [InlineData("  File \"/home/user/x.py\", line 12, in <module>")]
    [InlineData("Could not find the required path")]
    [InlineData("[exit 1]")]
    public void TypicalFailures_AreSystemError(string line)
    {
        Assert.True(HermesCliStderrClassifier.LooksLikeSystemError(line));
        Assert.StartsWith(HermesCliStreamLabels.SystemErrorPrefix, HermesCliStderrClassifier.FormatForUi(line));
    }
}
