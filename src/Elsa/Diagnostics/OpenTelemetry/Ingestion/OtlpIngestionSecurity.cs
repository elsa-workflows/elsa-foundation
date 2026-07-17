using System.Net;
using System.Security.Cryptography;
using System.Text;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Microsoft.AspNetCore.Http;

namespace Elsa.Diagnostics.OpenTelemetry.Ingestion;

public static class OtlpIngestionSecurity
{
    public static bool IsAuthorized(HttpContext httpContext, OpenTelemetryDiagnosticsOptions options)
        => Authenticate(httpContext, options).Accepted;

    internal static OtlpRequestAuthenticationResult Authenticate(HttpContext httpContext, OpenTelemetryDiagnosticsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return options.AllowUnauthenticatedLoopback && IsLoopback(httpContext)
                ? OtlpRequestAuthenticationResult.AcceptedUntrusted
                : OtlpRequestAuthenticationResult.Rejected;

        if (!httpContext.Request.Headers.TryGetValue(options.ApiKeyHeaderName, out var value) ||
            !ApiKeysMatch(value.ToString(), options.ApiKey))
            return OtlpRequestAuthenticationResult.Rejected;

        var context = OpenTelemetryIngestionContext.Authenticated("elsa:otlp:configured-api-key");
        return OtlpRequestAuthenticationResult.Accept(context);
    }

    private static bool IsLoopback(HttpContext httpContext)
    {
        var remoteAddress = httpContext.Connection.RemoteIpAddress;
        return remoteAddress != null && IPAddress.IsLoopback(remoteAddress);
    }

    private static bool ApiKeysMatch(string providedApiKey, string expectedApiKey)
    {
        var providedBytes = Encoding.UTF8.GetBytes(providedApiKey);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedApiKey);
        var providedHash = SHA256.HashData(providedBytes);
        var expectedHash = SHA256.HashData(expectedBytes);

        try
        {
            return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(providedBytes);
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(providedHash);
            CryptographicOperations.ZeroMemory(expectedHash);
        }
    }
}
