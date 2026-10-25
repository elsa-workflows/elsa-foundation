using System.Text.Json;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Connectors.OAuth2.Tests;

public sealed class SecretIsolationTests
{
    [Fact]
    public async Task Tokens_are_resolved_through_ISecretValueResolver_and_masked()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.ClientCredentialsScheme(preview));

        var completion = await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);
        Assert.Equal(200, completion.Result.StatusCode);

        var grantName = OAuth2SecretKeys.Grant(OAuth2TestSupport.ConnectionName);
        var resolved = await harness.SecretValues.ResolveAsync(OAuth2TestSupport.TenantId, new SecretReference(grantName));
        Assert.True(resolved.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(resolved.Value));

        var mask = (RecordingMask)harness.Mask;
        Assert.True(mask.HasRegistrations(OAuth2TestSupport.ActivityExecutionId));
        var leaked = mask.Registrations(OAuth2TestSupport.ActivityExecutionId)
            .Select(entry => entry.Value)
            .First(value => value.Contains(MockOAuthServer.CanaryAccessToken, StringComparison.Ordinal));
        var masked = mask.Mask(OAuth2TestSupport.ActivityExecutionId, $"token={leaked}");
        Assert.DoesNotContain(leaked, masked, StringComparison.Ordinal);
        Assert.Contains("[secret:", masked, StringComparison.Ordinal);
        Assert.Contains(mask.Registrations(OAuth2TestSupport.ActivityExecutionId), entry =>
            entry.Name.EndsWith(":access_token", StringComparison.Ordinal));

        var state = JsonSerializer.Serialize(completion.Result);
        Assert.DoesNotContain(leaked, state, StringComparison.Ordinal);
        Assert.DoesNotContain(MockOAuthServer.CanaryClientSecret, state, StringComparison.Ordinal);
        foreach (var line in harness.Logs.Lines)
        {
            Assert.DoesNotContain(leaked, line, StringComparison.Ordinal);
            Assert.DoesNotContain(MockOAuthServer.CanaryClientSecret, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Authorization_code_refresh_token_is_masked()
    {
        var preview = new MockOAuthServer();
        await using var harness = await OAuth2TestSupport.StartAsync(OAuth2TestSupport.AuthorizationCodeScheme(preview));
        await OAuth2TestSupport.AuthorizeAsync(harness, harness.Server);
        await OAuth2TestSupport.SendAsync(harness, harness.Server.ResourceEndpoint);

        var mask = (RecordingMask)harness.Mask;
        Assert.Contains(mask.Registrations(OAuth2TestSupport.ActivityExecutionId), entry =>
            entry.Name.EndsWith(":refresh_token", StringComparison.Ordinal) &&
            entry.Value.StartsWith(MockOAuthServer.CanaryRefreshToken, StringComparison.Ordinal));
        var refresh = mask.Registrations(OAuth2TestSupport.ActivityExecutionId)
            .First(entry => entry.Name.EndsWith(":refresh_token", StringComparison.Ordinal)).Value;
        var masked = mask.Mask(OAuth2TestSupport.ActivityExecutionId, $"refresh={refresh}");
        Assert.DoesNotContain(refresh, masked, StringComparison.Ordinal);
    }

    [Fact]
    public void Signing_and_scan_are_stubbed_until_the_pipeline_lands()
    {
        Assert.Equal("stub", ConnectorPackageAttestation.Status);
        Assert.Equal("valence-works/elsa-control#802", ConnectorPackageAttestation.PipelineIssue);
        Assert.False(ConnectorPackageAttestation.IsSigned);
        Assert.False(ConnectorPackageAttestation.IsScanned);
    }

    [Fact(Skip = "Pending lab-shard live-provider run until valence-works/elsa-control#802 and a lab shard are available.")]
    public void Live_provider_lab_test_is_pending()
    {
    }
}
