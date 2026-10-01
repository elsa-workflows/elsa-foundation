using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Modularity.Api.Authorization;

/// <summary>
/// Authentication primitive for the host-control surfaces a host maps on its root route builder (module
/// management, CShells management, and the optional Extension Builder). Every such surface gates its endpoints
/// behind the same configured API key; this type is the single source of truth for the header name,
/// configuration key, key comparison and validation flow so the surfaces cannot drift apart. It lives in a
/// library rather than in a host so an optional extension that maps a host-control surface can validate the
/// same key.
/// </summary>
public static class ManagementApiKeyAuthentication
{
    /// <summary>Configuration key holding the expected management API key.</summary>
    public const string ConfigurationKey = "Elsa:ModuleManagement:ApiKey";

    /// <summary>Request header carrying the caller-supplied management API key.</summary>
    public const string HeaderName = "X-Elsa-Module-Management-Key";

    /// <summary>
    /// Validates the management API key on the incoming request.
    /// Returns <c>null</c> when the request is authorized. Returns <see cref="Results.NotFound()"/>
    /// when no key is configured (the management surface is effectively disabled), or
    /// <see cref="Results.Unauthorized()"/> when the supplied key is missing or does not match.
    /// </summary>
    public static IResult? Validate(HttpContext httpContext)
    {
        var configuration = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var configuredApiKey = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configuredApiKey))
            return Results.NotFound();

        if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var providedApiKeys) ||
            providedApiKeys.Count != 1 ||
            string.IsNullOrWhiteSpace(providedApiKeys[0]) ||
            !KeysEqual(configuredApiKey, providedApiKeys[0]!))
            return Results.Unauthorized();

        return null;
    }

    /// <summary>Endpoint-filter adapter for host routes that use this management key.</summary>
    public static ValueTask<object?> RequireAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var failure = Validate(context.HttpContext);
        return failure is not null
            ? new ValueTask<object?>(failure)
            : next(context);
    }

    /// <summary>
    /// Compares two API keys in constant time to avoid leaking the expected key through timing
    /// side channels.
    /// </summary>
    public static bool KeysEqual(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
