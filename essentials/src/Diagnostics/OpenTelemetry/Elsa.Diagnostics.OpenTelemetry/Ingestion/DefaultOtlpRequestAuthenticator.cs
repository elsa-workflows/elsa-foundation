using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Ingestion;

/// <summary>
/// Preserves the built-in configured API-key or loopback policy. The global API key authenticates only the
/// collector credential and intentionally supplies no tenant scope claims. Loopback acceptance is untrusted.
/// </summary>
public sealed class DefaultOtlpRequestAuthenticator(IOptions<OpenTelemetryDiagnosticsOptions> options) : IOtlpRequestAuthenticator
{
    public ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OtlpIngestionSecurity.Authenticate(httpContext, options.Value));
    }
}
