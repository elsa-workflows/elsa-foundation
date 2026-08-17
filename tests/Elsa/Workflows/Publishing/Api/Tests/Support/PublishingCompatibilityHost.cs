using CShells.FastEndpoints.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// A real historical FastEndpoints host.  Endpoint discovery, route constraints, authorization
/// metadata, API Explorer and OpenAPI are production paths; only mediator/store responses are
/// replaced with deterministic capture seams.
/// </summary>
public sealed class PublishingCompatibilityHost(IHost host) : IAsyncDisposable
{
    public HttpClient Client { get; } = host.GetTestClient();
    public IHost Host { get; } = host;

    public static async Task<PublishingCompatibilityHost> StartAsync()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.UseSetting(WebHostDefaults.ApplicationKey, "elsa-workflows-publishing-before-capture");
                webHost.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddWorkflowRuntime();
                    services.AddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
                    services.AddAuthentication(CaptureAuthenticationHandler.SchemeName)
                        .AddScheme<AuthenticationSchemeOptions, CaptureAuthenticationHandler>(CaptureAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddFoundationIdentityAbstractions(options =>
                        options.NormalizedAuthenticationTypes = new HashSet<string>([CaptureAuthenticationHandler.SchemeName], StringComparer.Ordinal));
                    services.AddOpenApi();

                    // Compose the actual endpoint-free engine and the historical FE API feature.
                    services.AddSingleton<IWorkflowTriggerBindingExtractor>(new WorkflowTriggerBindingExtractor([]));
                    services.AddSingleton<IWorkflowExecutableCompiler, CaptureWorkflowExecutableCompiler>();
                    new WorkflowsPublishingFeature().ConfigureServices(services);
                    new WorkflowsPublishingApiFeature().ConfigureServices(services);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider, CaptureTimeProvider>();
                    services.AddSingleton<IRequestSender, CaptureRequestSender>();
                    services.AddFastEndpoints(options => options.Assemblies = [typeof(WorkflowsPublishingApiFeature).Assembly]);
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapFastEndpoints(config =>
                        {
                            using var scope = endpoints.ServiceProvider.CreateScope();
                            foreach (var configurator in scope.ServiceProvider.GetServices<IFastEndpointsConfigurator>())
                                configurator.Configure(config);
                            config.Security.PermissionsClaimType = IdentityClaimTypes.Permission;
                        });
                        endpoints.MapOpenApi();
                    });
                });
            })
            .Build();

        await host.StartAsync();
        var slotStore = host.Services.GetRequiredService<IPublicationSlotStore>();
        await slotStore.TryActivateAsync(
            "definition-route",
            "default",
            "publication-capture",
            expectedRevision: 0,
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
        return new PublishingCompatibilityHost(host);
    }

    public async Task<string> GetOpenApiAsync()
    {
        using var response = await Client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        Host.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class CaptureTimeProvider : TimeProvider
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => FixedTime;
}

internal sealed class CaptureAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "PublishingBeforeCapture";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var identity = Request.Headers[PublishingCompatibilityCases.IdentityHeader].ToString();
        if (string.IsNullOrWhiteSpace(identity))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new ClaimsIdentity(Scheme.Name);
        claims.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));
        claims.AddClaim(new Claim(IdentityClaimTypes.Permission, PermissionKey.Wildcard));
        claims.AddClaim(new Claim(IdentityClaimTypes.TenantId, "capture-tenant"));
        claims.AddClaim(new Claim(ClaimTypes.NameIdentifier, "capture-actor"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(claims), Scheme.Name)));
    }
}

internal sealed class CaptureRequestSender(IHttpContextAccessor contextAccessor) : IRequestSender
{
    public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
    {
        var scenario = contextAccessor.HttpContext?.Request.Headers[PublishingCompatibilityCases.IdentityHeader].ToString();
        cancellationToken.ThrowIfCancellationRequested();
        if (scenario == "trusted-cancellation")
            throw new OperationCanceledException("The deterministic capture request was canceled.", cancellationToken);
        if (scenario == "trusted-domain-not-found")
            throw new EntityNotFoundException("The deterministic capture entity was not found.");
        if (scenario == "trusted-domain-conflict")
        {
            if (contextAccessor.HttpContext?.Request.Path.StartsWithSegments("/design/activities") == true)
                throw new ActivityPublicationRejectedException(
                    "activity.draft.stale-revision",
                    "The deterministic activity publication conflict was requested.",
                    [],
                    isConflict: true);
            throw new PublicationPolicyResolutionException(
                "expected_publication_mismatch",
                "The deterministic publication conflict was requested.");
        }
        if (scenario == "trusted-domain-validation")
            throw new PublicationPolicyResolutionException("invalid_host_policy", "The deterministic publication validation failure was requested.");
        if (scenario == "trusted-domain-unavailable")
            throw new RuntimeRequirementPreflightRequestException("The deterministic runtime requirement provider is unavailable.");

        if (typeof(T) == typeof(PublishedWorkflowView))
        {
            var response = new PublishedWorkflowView(
                "publication-capture",
                "definition-capture",
                "version-route",
                "definition-version-capture",
                "artifact-capture",
                "default",
                PublicationStatusView.Active,
                "source-reference-capture",
                new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
                null,
                "1.0",
                "artifact-hash-capture",
                "root-capture",
                1,
                WasCreated: true);
            return Task.FromResult((T)(object)response);
        }

        return Task.FromResult((T)CaptureResponseFactory.Create(typeof(T)));
    }
}

/// <summary>Deterministic compiler seam used only by the historical host; it still returns a real executable.</summary>
internal sealed class CaptureWorkflowExecutableCompiler : IWorkflowExecutableCompiler
{
    public ValueTask<WorkflowExecutable> CompileAsync(
        WorkflowExecutableCompileRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse("{}");
        var node = new ExecutableNode(
            "capture-node",
            "capture-activity",
            "Capture.Activity",
            "1.0",
            new RuntimeActivityDescriptor("capture.consumer", "1", document.RootElement),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
        var executable = new WorkflowExecutable(
            new WorkflowExecutableIdentity(
                "capture-artifact",
                "capture-definition",
                request.VersionId,
                "1.0",
                "capture-hash"),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            new IncidentStrategyReference("default", "1"));
        return ValueTask.FromResult(executable);
    }
}

internal static class CaptureResponseFactory
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    public static object Create(Type type) => Create(type, 0) ?? throw new InvalidOperationException($"Cannot create capture response '{type.FullName}'.");

    private static object? Create(Type type, int depth)
    {
        if (depth > 10)
            return null;
        if (type == typeof(string))
            return "capture";
        if (type == typeof(DateTimeOffset))
            return FixedTime;
        if (type == typeof(DateTime))
            return FixedTime.UtcDateTime;
        if (type == typeof(Guid))
            return Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (type == typeof(JsonElement))
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement.Clone();
        }
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0);
        if (Nullable.GetUnderlyingType(type) is not null)
            return null;
        if (type.IsArray)
            return Array.CreateInstance(type.GetElementType()!, 0);
        if (type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type))
        {
            var elementType = type.GetGenericArguments().SingleOrDefault();
            if (elementType is not null)
                return Array.CreateInstance(elementType, 0);
        }
        if (type == typeof(object))
            return new { value = "capture" };
        if (type.IsInterface || type.IsAbstract)
            return null;
        if (type.IsValueType)
            return Activator.CreateInstance(type);

        var constructor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();
        if (constructor is null)
            return Activator.CreateInstance(type);
        var arguments = constructor.GetParameters()
            .Select(parameter => CreateParameter(parameter.ParameterType, depth + 1))
            .ToArray();
        return constructor.Invoke(arguments);
    }

    private static object? CreateParameter(Type type, int depth)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        {
            var arguments = type.GetGenericArguments();
            return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
        }
        return Create(type, depth);
    }
}
