using Elsa.Connections.Core.Models;
using Xunit;

namespace Elsa.Connections.Tests;

public sealed class AllowedHostsPolicyTests
{
    [Fact]
    public void Any_AllowsEveryHost()
    {
        Assert.True(AllowedHostsPolicy.Any.Allows(new Uri("https://evil.example/x")));
    }

    [Fact]
    public void UserDeclared_Empty_RefusesEveryHost()
    {
        Assert.False(AllowedHostsPolicy.UserDeclared.Allows(new Uri("https://api.example/x")));
    }

    [Fact]
    public void Fixed_MatchesExactAndWildcardSuffix()
    {
        var policy = AllowedHostsPolicy.Fixed("api.example", "*.googleapis.com");
        Assert.True(policy.Allows(new Uri("https://api.example/v1")));
        Assert.True(policy.Allows(new Uri("https://www.googleapis.com/drive")));
        Assert.False(policy.Allows(new Uri("https://evil.example/x")));
    }
}
