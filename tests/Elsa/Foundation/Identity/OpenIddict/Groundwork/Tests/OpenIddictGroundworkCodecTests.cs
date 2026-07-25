using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkCodecTests
{
    [Theory]
    [InlineData("Application")]
    [InlineData("Authorization")]
    [InlineData("Scope")]
    [InlineData("Token")]
    public void Every_record_kind_has_a_versioned_codec_contract(string recordKind)
    {
        var policy = recordKind switch
        {
            "Application" => OpenIddictGroundworkJson.CreateApplicationPolicy(),
            "Authorization" => OpenIddictGroundworkJson.CreateAuthorizationPolicy(),
            "Scope" => OpenIddictGroundworkJson.CreateScopePolicy(),
            "Token" => OpenIddictGroundworkJson.CreateTokenPolicy(),
            _ => throw new ArgumentOutOfRangeException(nameof(recordKind))
        };

        Assert.Equal(1, policy.MinimumReadableVersion);
        Assert.Equal(1, policy.CurrentVersion);
    }

    [Fact]
    public void Codec_contract_exposes_current_minimum_readable_and_upcast_policies()
    {
        Assert.Equal(4, OpenIddictGroundworkJson.Policies.Count);
        Assert.Empty(OpenIddictGroundworkJson.Upcasters);
    }
}
