using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Collectibility;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Secrets.Api.Features;
using Elsa.Secrets.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NativeEndpoints;

namespace Elsa.Secrets.Tests.Support;

/// <summary>Value-only record of what one live Secrets API composition exercised before unload.</summary>
public sealed record SecretsObservation(
    int RouteCount,
    IReadOnlyList<string> PolicyNames,
    bool JsonExercised,
    bool DocumentationGenerated);

/// <summary>
/// The production Secrets API feature as a collectible module: its host composition, and the route, JSON
/// binding, and OpenAPI generation it exercises while mapped.
/// </summary>
public static class SecretsCollectibleModule
{
    private static readonly CollectibleModule Module = new()
    {
        FeatureType = typeof(SecretsApiFeature),
        AssemblyNamePrefix = "Elsa.Collectible.Secrets.Api",
        ConfigureHost = ConfigureHost
    };

    public static CollectibleModuleCycle<SecretsObservation> Create(
        RetentionStage retentionStage = RetentionStage.Clean,
        bool generateDocumentation = false) =>
        CollectibleModuleFixture.Create(
            Module,
            retentionStage,
            host => Observe(host, generateDocumentation || retentionStage == RetentionStage.Serializer));

    private static void ConfigureHost(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Secrets:EncryptionKey"] = "collectibility-test-encryption-key"
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddAuthentication();
        services.AddAuthorization();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(StringComparer.Ordinal) { "test" });
        services.AddSecrets(configuration);
    }

    private static SecretsObservation Observe(CollectibleModuleHost host, bool generateDocumentation)
    {
        var routeCount = host.Endpoints.Count(endpoint =>
            (endpoint as RouteEndpoint)?.RoutePattern.RawText?.StartsWith("/secrets", StringComparison.Ordinal) == true);
        if (routeCount == 0)
            throw new InvalidOperationException("The production Secrets API mapper published no endpoints.");

        var policyNames = host.Endpoints
            .Select(endpoint => endpoint.Metadata.GetMetadata<IAuthorizeData>()?.Policy)
            .OfType<string>()
            .ToArray();
        return new(routeCount, policyNames, ExerciseJson(host), generateDocumentation && GenerateOpenApiDocument(host));
    }

    private static bool ExerciseJson(CollectibleModuleHost host)
    {
        var endpoint = host.FindEndpoint("/secrets/picker", HttpMethods.Post);
        if (endpoint?.RequestDelegate is null)
            return false;

        using var requestScope = host.Services.CreateScope();
        var context = new DefaultHttpContext
        {
            RequestServices = requestScope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(IdentityClaimTypes.TenantId, "collectibility-tenant")],
                "test"))
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/secrets/picker";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream("{}"u8.ToArray());
        try
        {
            endpoint.RequestDelegate(context).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Generates the document from a second provider that owns the mapped data sources. The runtime keeps
    /// schema metadata for the collectible types beyond that provider's disposal; the suite asserts that
    /// retention is reported honestly rather than claimed released.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool GenerateOpenApiDocument(CollectibleModuleHost host)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.SuppressEndpointLifetimeEnforcement();
        services.AddElsaEndpoints();
        services.AddSingleton(host.Services.GetRequiredService<IHostEnvironment>());
        ConfigureHost(services);
        services.AddOpenApi();
        foreach (var dataSource in host.DataSources)
            services.AddSingleton(dataSource);

        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var document = serviceProvider.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return document.Paths?.Count(path => path.Key.StartsWith("/secrets", StringComparison.Ordinal)) == 7;
    }
}
