using DocumentIDE.Api.Security;
using Xunit;

namespace DocumentIDE.SecurityTests;

public class ControlTokenTests
{
    [Fact]
    public void Control_token_is_valid_and_distinct_from_the_session_token()
    {
        var provider = new SessionTokenProvider();

        Assert.NotEqual(provider.SessionToken, provider.ControlToken);
        Assert.True(provider.ValidateControlToken(provider.ControlToken));
        Assert.False(provider.ValidateControlToken(provider.SessionToken));
        Assert.False(provider.ValidateToken(provider.ControlToken));
    }
}
