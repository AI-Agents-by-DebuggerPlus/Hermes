using Hermes.Wpf.Services;
using Xunit;

namespace Hermes.Wpf.Tests;

public sealed class HermesProjectRecipientRouterTests
{
    [Fact]
    public void IsHermesBound_PlainHermes()
    {
        Assert.True(HermesProjectRecipientRouter.IsHermesBoundRecipient("Hermes"));
        Assert.True(HermesProjectRecipientRouter.IsHermesBoundRecipient("hermes"));
    }

    [Fact]
    public void IsHermesBound_ProjectSuffix()
    {
        Assert.True(HermesProjectRecipientRouter.IsHermesBoundRecipient("Hermes.Utilities"));
        Assert.True(HermesProjectRecipientRouter.IsHermesBoundRecipient("Hermes/Utilities"));
        Assert.False(HermesProjectRecipientRouter.IsHermesBoundRecipient("Android"));
        Assert.False(HermesProjectRecipientRouter.IsHermesBoundRecipient("WpfChat"));
    }

    [Fact]
    public void TryParseProjectSuffix_ExtractsName()
    {
        Assert.True(HermesProjectRecipientRouter.TryParseProjectSuffix("Hermes.Utilities", out var name));
        Assert.Equal("Utilities", name);

        Assert.True(HermesProjectRecipientRouter.TryParseProjectSuffix("Hermes.English Tutor", out name));
        Assert.Equal("English Tutor", name);

        Assert.False(HermesProjectRecipientRouter.TryParseProjectSuffix("Hermes", out _));
    }
}
