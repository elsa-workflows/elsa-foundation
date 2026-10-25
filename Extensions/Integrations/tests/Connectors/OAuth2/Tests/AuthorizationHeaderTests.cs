using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class AuthorizationHeaderTests
{
    [Theory]
    [InlineData("bot", "bot")]
    [InlineData("bearer", "bearer")]
    [InlineData("Bearer", "Bearer")]
    [InlineData(null, null)]
    public async Task Non_standard_or_bearer_token_type_sends_Bearer(string? issuedType, string? storedType)
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.ClientCredentialsScheme(preview),
            configureServer: mock =>
            {
                if (issuedType is null)
                    mock.OmitTokenType = true;
                else
                    mock.IssuedTokenType = issuedType;
            });

        var completion = await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);
        Assert.Equal(200, completion.Result.StatusCode);
        Assert.Equal(OAuth2Authorization.BearerScheme, harness.Server.LastAuthorizationScheme);
        Assert.StartsWith("Bearer ", harness.Server.LastAuthorizationHeader, StringComparison.Ordinal);

        var grant = await harness.SecretValues.ResolveAsync(
            OAuth2TestSupport.TenantId,
            new SecretReference(OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName)));
        var expectedStored = storedType ?? "Bearer";
        Assert.Contains($"\"token_type\":\"{expectedStored}\"", grant.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenHeaderScheme_overrides_the_authorization_scheme()
    {
        var preview = new MockOAuthServer();
        var scheme = OAuth2TestSupport.ClientCredentialsScheme(preview) with { TokenHeaderScheme = "Token" };
        await using var harness = await OAuth2TestSupport.StartAsync(scheme);

        var completion = await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);
        Assert.Equal(200, completion.Result.StatusCode);
        Assert.Equal("Token", harness.Server.LastAuthorizationScheme);
        Assert.StartsWith("Token ", harness.Server.LastAuthorizationHeader, StringComparison.Ordinal);
    }
}
