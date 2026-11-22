using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Tests.Ingestion;

public sealed class OtlpRequestAuthenticationTests
{
    [Fact]
    public async Task DefaultAuthenticator_ApiKeyAcceptsWithoutTenantClaims()
    {
        var httpContext = CreateContext();
        httpContext.Request.Headers["x-otlp-api-key"] = "secret";
        var authenticator = new DefaultOtlpRequestAuthenticator(Options.Create(new OpenTelemetryDiagnosticsOptions { ApiKey = "secret" }));

        var result = await authenticator.AuthenticateAsync(httpContext);

        Assert.True(result.Accepted);
        Assert.True(result.Context.IsAuthenticated);
        Assert.Empty(result.Context.Claims);
        Assert.Empty(result.Context.Metadata);
    }

    [Fact]
    public async Task DefaultAuthenticator_LoopbackAcceptanceIsExplicitlyUntrusted()
    {
        var authenticator = new DefaultOtlpRequestAuthenticator(Options.Create(new OpenTelemetryDiagnosticsOptions()));

        var result = await authenticator.AuthenticateAsync(CreateContext(System.Net.IPAddress.Loopback));

        Assert.True(result.Accepted);
        Assert.Same(OpenTelemetryIngestionContext.Untrusted, result.Context);
    }

    [Fact]
    public async Task DefaultAuthenticator_RejectsWrongApiKey()
    {
        var httpContext = CreateContext();
        httpContext.Request.Headers["x-otlp-api-key"] = "wrong";
        var authenticator = new DefaultOtlpRequestAuthenticator(Options.Create(new OpenTelemetryDiagnosticsOptions { ApiKey = "secret" }));

        var result = await authenticator.AuthenticateAsync(httpContext);

        Assert.False(result.Accepted);
        Assert.Same(OpenTelemetryIngestionContext.Untrusted, result.Context);
    }

    [Fact]
    public void AuthenticatedContextRejectsBlankSourceIdentity()
    {
        Assert.Throws<ArgumentException>(() => OpenTelemetryIngestionContext.Authenticated(" "));
    }

    [Fact]
    public void AuthenticationResultRequiresExplicitUntrustedAcceptance()
    {
        Assert.Throws<ArgumentException>(() => OtlpRequestAuthenticationResult.Accept(OpenTelemetryIngestionContext.Untrusted));
        Assert.True(OtlpRequestAuthenticationResult.AcceptedUntrusted.Accepted);
        Assert.False(OtlpRequestAuthenticationResult.AcceptedUntrusted.Context.IsAuthenticated);
    }

    private static DefaultHttpContext CreateContext(System.Net.IPAddress? remoteIpAddress = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIpAddress ?? System.Net.IPAddress.Parse("203.0.113.10");
        return context;
    }
}
