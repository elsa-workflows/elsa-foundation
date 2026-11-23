using System.Text.Json;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class TokenErrorTests
{
    private const string LeakedSecret = "canary-client-secret-2727";
    private const string LeakedCode = "leaked-auth-code-3322";
    private const string LeakedAccess = "canary-access-token-2727-leaked";
    private const string LeakedRefresh = "canary-refresh-token-2727-leaked";

    [Fact]
    public async Task Missing_access_token_surfaces_error_fields_and_not_secrets()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(
            OAuth2TestSupport.ClientCredentialsScheme(preview),
            configureServer: mock => mock.TokenErrorBody = new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["error"] = "invalid_code",
                ["error_description"] = "The authorization code is no longer valid",
                ["client_secret"] = LeakedSecret,
                ["code"] = LeakedCode,
                ["access_token"] = "",
                ["refresh_token"] = LeakedRefresh,
                ["hint"] = $"echo {LeakedAccess} and {LeakedSecret}",
                ["debug"] = new Dictionary<string, string>
                {
                    ["access_token"] = LeakedAccess,
                    ["client_secret"] = LeakedSecret
                }
            });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint));

        Assert.Contains("no access_token", error.Message, StringComparison.Ordinal);
        Assert.Contains("invalid_code", error.Message, StringComparison.Ordinal);
        Assert.Contains("The authorization code is no longer valid", error.Message, StringComparison.Ordinal);
        AssertLeakedValuesAbsent(error.Message);
        foreach (var line in harness.Logs.Lines)
            AssertLeakedValuesAbsent(line);
    }

    [Fact]
    public void Error_helper_takes_only_error_fields_and_truncates()
    {
        var json = """
            {
              "error": "invalid_code",
              "error_description": "short",
              "client_secret": "secret-value",
              "access_token": "token-value",
              "refresh_token": "refresh-value",
              "code": "code-value"
            }
            """;
        using var document = JsonDocument.Parse(json);
        var message = OAuth2TokenError.MissingAccessTokenMessage(document.RootElement);
        Assert.Contains("invalid_code", message, StringComparison.Ordinal);
        Assert.Contains("short", message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("token-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("code-value", message, StringComparison.Ordinal);

        var longError = new string('z', OAuth2TokenError.MaxFieldLength + 50);
        using var longDocument = JsonDocument.Parse($$"""{ "error": "{{longError}}" }""");
        var truncated = OAuth2TokenError.MissingAccessTokenMessage(longDocument.RootElement);
        Assert.DoesNotContain(longError, truncated, StringComparison.Ordinal);
        Assert.Contains(new string('z', OAuth2TokenError.MaxFieldLength), truncated, StringComparison.Ordinal);
        Assert.Equal(OAuth2TokenError.MaxFieldLength, truncated.Count(c => c == 'z'));
    }

    private static void AssertLeakedValuesAbsent(string text)
    {
        Assert.DoesNotContain(LeakedSecret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakedCode, text, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakedAccess, text, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakedRefresh, text, StringComparison.Ordinal);
    }
}
