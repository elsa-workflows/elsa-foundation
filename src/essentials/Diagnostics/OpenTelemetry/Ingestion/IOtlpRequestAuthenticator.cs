using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Diagnostics.OpenTelemetry.Ingestion;

/// <summary>
/// Authenticates an OTLP/HTTP request and establishes server-authoritative source claims. Hosts can replace
/// the default scoped implementation to validate per-source credentials and map them to trusted scope claims.
/// </summary>
public interface IOtlpRequestAuthenticator
{
    ValueTask<OtlpRequestAuthenticationResult> AuthenticateAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of authenticating an OTLP request.</summary>
public sealed class OtlpRequestAuthenticationResult
{
    private OtlpRequestAuthenticationResult(bool accepted, OpenTelemetryIngestionContext context)
    {
        Accepted = accepted;
        Context = context;
    }

    public bool Accepted { get; }
    public OpenTelemetryIngestionContext Context { get; }

    public static OtlpRequestAuthenticationResult Rejected { get; } = new(false, OpenTelemetryIngestionContext.Untrusted);
    public static OtlpRequestAuthenticationResult AcceptedUntrusted { get; } = new(true, OpenTelemetryIngestionContext.Untrusted);

    public static OtlpRequestAuthenticationResult Accept(OpenTelemetryIngestionContext authenticatedContext)
    {
        ArgumentNullException.ThrowIfNull(authenticatedContext);
        if (!authenticatedContext.IsAuthenticated)
            throw new ArgumentException("Accepted authenticated context must have an authenticated source identity.", nameof(authenticatedContext));

        return new(true, authenticatedContext);
    }
}
