using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Authorization;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.OpenApi;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Executes the Publishing mapper, authorization policy, owner serializer, native OpenAPI provider,
/// and representative real delegates in three collectible host generations.
/// </summary>
[Collection(Wave8PublishingCollectibilityCollection.Name)]
public sealed class Wave8PublishingMinimalApiCollectibilityTests
{
    private const string OwnerId = "Elsa.Workflows.Publishing.Api";
    private const string AuthenticationScheme = "Wave8PublishingCollectibility";

    [Fact]
    public async Task Publishing_owner_is_collectible_after_three_alternating_openapi_and_serialization_cycles()
    {
        var failures = new List<string>();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var evidence = await CreateAndReleaseAsync(cycle);
            var collected = WaitForCollection(evidence.References);
            if (!collected)
            {
                failures.Add($"cycle {cycle}: owner retained; alive=" +
                             string.Join(",", evidence.References.Where(reference => reference.Value.IsAlive).Select(reference => reference.Key)));
            }
        }

        if (failures.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Publishing_feature_remains_public_nonsealed_and_virtual_for_shell_composition()
    {
        var featureType = typeof(WorkflowsPublishingApiFeature);
        Assert.True(featureType.IsPublic);
        Assert.False(featureType.IsSealed);
        var configure = featureType.GetMethod(nameof(WorkflowsPublishingApiFeature.ConfigureServices), BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(configure);
        Assert.True(configure!.IsVirtual);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Evidence> CreateAndReleaseAsync(int cycle)
    {
        var sourcePath = typeof(WorkflowsPublishingApiFeature).Assembly.Location;
        var loadContext = new PublishingLoadContext($"Elsa.Wave8.Publishing.{cycle}.{Guid.NewGuid():N}");
        Assembly assembly;
        using (var stream = new MemoryStream(File.ReadAllBytes(sourcePath), writable: false))
            assembly = loadContext.LoadFromStream(stream);

        var featureType = assembly.GetType(typeof(WorkflowsPublishingApiFeature).FullName!, throwOnError: true)!;
        var mapperType = assembly.GetType(typeof(WorkflowsPublishingApi).FullName!, throwOnError: true)!;
        var feature = Activator.CreateInstance(featureType)!;
        var mapper = mapperType.GetMethod(nameof(WorkflowsPublishingApi.MapWorkflowsPublishingApi), BindingFlags.Public | BindingFlags.Static)!;
        var observations = new CollectibilityProbeState();

        var publishedDataSource = new CollectibilityEndpointDataSource();
        var serviceDescriptors = new ServiceCollection();
        serviceDescriptors.AddLogging();
        serviceDescriptors.AddRouting();
        serviceDescriptors.AddHttpContextAccessor();
        serviceDescriptors.AddSingleton<IHostEnvironment>(new CollectibilityHostEnvironment());
        serviceDescriptors.AddSingleton<EndpointDataSource>(publishedDataSource);
        serviceDescriptors.AddSingleton(observations);
        serviceDescriptors.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>([AuthenticationScheme], StringComparer.Ordinal));
        serviceDescriptors.AddAuthentication(AuthenticationScheme)
            .AddScheme<AuthenticationSchemeOptions, CollectibilityAuthenticationHandler>(AuthenticationScheme, _ => { });
        serviceDescriptors.AddAuthorization();

        // Invoke the real owner feature so the test observes its actual resolver, permission,
        // API Explorer, handler, and service-registration seams. Replace only runtime state with
        // deterministic disposable probes; no production mapper or feature code is substituted.
        featureType.GetMethod(nameof(WorkflowsPublishingApiFeature.ConfigureServices), BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(feature, [serviceDescriptors]);
        serviceDescriptors.AddOpenApi();
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IRequestSender, CollectibilityRequestSender>());
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IWorkflowExecutableCompiler, CollectibilityCompiler>());
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IPublicationSlotStore, CollectibilitySlotStore>());
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IPublicationRecordStore, CollectibilityPublicationStore>());
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IPublicationPolicyStore, CollectibilityPolicyStore>());
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IActivityDefinitionPublisher, CollectibilityActivityPublisher>());
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IWorkflowTestRunStore, CollectibilityWorkflowTestRunStore>());
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IActivityDraftTestRunStore, CollectibilityActivityTestRunStore>());
        serviceDescriptors.Replace(ServiceDescriptor.Singleton<IPermissionResourceHandler, CollectibilityResourceHandler>());

        var services = serviceDescriptors.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = false
        });
        var routeBuilder = new CollectibilityRouteBuilder(services);
        mapper.Invoke(null, [routeBuilder]);
        var endpoints = routeBuilder.DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()?.OwnerId == OwnerId)
            .ToArray();
        publishedDataSource.SetEndpoints(endpoints);
        Assert.Equal(23, endpoints.Length);

        var jsonOptions = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var resolver = jsonOptions.TypeInfoResolverChain.Single(candidate => candidate.GetType().Name == "JsonTypeInfoResolver");
        var serialized = JsonSerializer.Serialize(
            new RuntimeRequirementPreflightView(0, true, [], []),
            jsonOptions);
        Assert.Contains("checkedArtifactCount", serialized, StringComparison.Ordinal);

        var openApiProvider = services.GetRequiredKeyedService<IOpenApiDocumentProvider>("v1");
        OpenApiDocument? openApi = null;
        var openApiOperationCount = 0;
        if (cycle % 2 == 0)
        {
            openApi = await openApiProvider.GetOpenApiDocumentAsync(CancellationToken.None).ConfigureAwait(false);
            openApiOperationCount = openApi.Paths?.Sum(path => path.Value.Operations?.Count ?? 0) ?? 0;
        }

        await ExerciseRepresentativeDelegatesAsync(services, endpoints).ConfigureAwait(false);

        if (cycle % 2 == 1)
        {
            openApi = await openApiProvider.GetOpenApiDocumentAsync(CancellationToken.None).ConfigureAwait(false);
            openApiOperationCount = openApi.Paths?.Sum(path => path.Value.Operations?.Count ?? 0) ?? 0;
        }

        Assert.Equal(23, openApiOperationCount);
        Assert.NotNull(openApi?.Paths?.GetValueOrDefault("/publishing/activities"));
        Assert.True(observations.AuthorizationCalls >= 6);
        Assert.True(observations.ResourceCalls >= 6);
        Assert.Equal(1, observations.CatalogCalls);
        Assert.Equal(1, observations.PreflightCalls);
        Assert.Equal(1, observations.PublicationCalls);
        Assert.Equal(1, observations.TestRunCalls);
        Assert.Equal(1, observations.PolicyCalls);
        Assert.Equal(1, observations.SlotCalls);

        // Resolve and exercise configured owner state so disposal is observable rather than inferred
        // from process memory. The actual mapped delegates above already use sender, policy and slot state.
        await services.GetRequiredService<IWorkflowExecutableCompiler>().CompileAsync(
            new WorkflowExecutableCompileRequest(
                "collectibility-version",
                WorkflowExecutableReferenceScope.Published,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                "collectibility-artifact",
                new Dictionary<string, string>()),
            CancellationToken.None);
        _ = services.GetRequiredService<IActivityDefinitionPublisher>();
        _ = services.GetRequiredService<IWorkflowTestRunStore>();
        _ = services.GetRequiredService<IActivityDraftTestRunStore>();
        _ = services.GetRequiredService<IPermissionResourceHandler>();

        var references = new Dictionary<string, WeakReference>(StringComparer.Ordinal)
        {
            ["load-context"] = new(loadContext),
            ["assembly"] = new(assembly),
            ["mapper-type"] = new(mapperType),
            ["feature-type"] = new(featureType),
            ["feature"] = new(feature),
            ["services"] = new(services),
            ["route-builder"] = new(routeBuilder),
            ["endpoint-data-source"] = new(publishedDataSource),
            ["json-resolver"] = new(resolver),
            ["openapi"] = new(openApi!),
            ["observations"] = new(observations)
        };
        foreach (var endpoint in endpoints.Select((endpoint, index) => (endpoint, index)))
            references[$"endpoint-{endpoint.index}"] = new(endpoint.endpoint);

        Assert.True(observations.Disposals == 0);
        publishedDataSource.SetEndpoints([]);
        routeBuilder.DataSources.Clear();
        Assert.Empty(publishedDataSource.Endpoints);
        await services.DisposeAsync().ConfigureAwait(false);
        Assert.True(observations.Disposals >= 8, $"Expected configured owner state to be disposed; observed {observations.Disposals}.");

        endpoints = null!;
        openApiProvider = null!;
        openApi = null;
        resolver = null!;
        services = null!;
        mapper = null!;
        mapperType = null!;
        feature = null!;
        featureType = null!;
        routeBuilder = null!;
        publishedDataSource = null!;
        serviceDescriptors = null!;
        loadContext.Unload();
        loadContext = null!;
        assembly = null!;

        return new(Guid.NewGuid(), references, references["load-context"], references["assembly"], references["mapper-type"]);
    }

    private static async Task ExerciseRepresentativeDelegatesAsync(
        ServiceProvider services,
        IReadOnlyCollection<RouteEndpoint> endpoints)
    {
        await InvokeAsync(services, endpoints, "List", null, null, StatusCodes.Status200OK);
        await InvokeAsync(services, endpoints, "RuntimeRequirementPreflightEndpoint", null,
            "{\"scope\":\"collectibility\",\"artifactIds\":[]}", StatusCodes.Status200OK);
        await InvokeAsync(services, endpoints, "ListPublicationSlotsEndpoint",
            new Dictionary<string, object?> { ["definitionId"] = "definition" }, null, StatusCodes.Status200OK);
        await InvokeAsync(services, endpoints, "GetWorkflowPublicationPolicyEndpoint",
            new Dictionary<string, object?> { ["definitionId"] = "definition" }, null, StatusCodes.Status200OK);
        await InvokeAsync(services, endpoints, "PublishWorkflowEndpoint",
            new Dictionary<string, object?> { ["versionId"] = "version" },
            "{\"versionId\":\"body-version\"}", StatusCodes.Status200OK);
        await InvokeAsync(services, endpoints, "TestRunsStart",
            new Dictionary<string, object?> { ["versionId"] = "version" },
            "{\"versionId\":\"body-version\"}", StatusCodes.Status200OK);
    }

    private static async Task InvokeAsync(
        ServiceProvider services,
        IEnumerable<RouteEndpoint> endpoints,
        string operation,
        IReadOnlyDictionary<string, object?>? routeValues,
        string? body,
        int expectedStatus)
    {
        var endpoint = Assert.Single(endpoints, endpoint =>
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ==
            $"ElsaWorkflowsPublishingApiEndpoints{operation}");
        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            Response = { Body = new MemoryStream() }
        };
        context.SetEndpoint(endpoint);
        foreach (var routeValue in routeValues ?? new Dictionary<string, object?>())
            context.Request.RouteValues[routeValue.Key] = routeValue.Value;
        if (body is not null)
        {
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        }

        var authentication = await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync(context, AuthenticationScheme);
        Assert.True(authentication.Succeeded);
        context.User = authentication.Principal!;
        var authorizationData = endpoint.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>();
        var policy = await Microsoft.AspNetCore.Authorization.AuthorizationPolicy.CombineAsync(
            scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>(),
            authorizationData);
        Assert.NotNull(policy);
        var authorization = await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationService>()
            .AuthorizeAsync(context.User, endpoint, policy!.Requirements);
        Assert.True(authorization.Succeeded);

        await endpoint.RequestDelegate!(context);
        Assert.Equal(expectedStatus, context.Response.StatusCode);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitForCollection(IReadOnlyDictionary<string, WeakReference> references)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (references.All(reference => !reference.Value.IsAlive))
                return true;
        }

        return false;
    }

    private sealed record Evidence(
        Guid CycleId,
        IReadOnlyDictionary<string, WeakReference> References,
        WeakReference LoadContext,
        WeakReference Assembly,
        WeakReference MapperType);

    private sealed class PublishingLoadContext(string name) : AssemblyLoadContext(name, isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            Default.Assemblies.FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CollectibilityRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }

    private sealed class CollectibilityEndpointDataSource : EndpointDataSource
    {
        private IReadOnlyList<Endpoint> _endpoints = [];
        private CancellationTokenSource _changeSource = new();

        public override IReadOnlyList<Endpoint> Endpoints => Volatile.Read(ref _endpoints);
        public override IChangeToken GetChangeToken() => new CancellationChangeToken(Volatile.Read(ref _changeSource).Token);

        public void SetEndpoints(IReadOnlyList<Endpoint> endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            Volatile.Write(ref _endpoints, endpoints.ToArray());
            var previous = Interlocked.Exchange(ref _changeSource, new CancellationTokenSource());
            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }
    }

    private sealed class CollectibilityHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = OwnerId;
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CollectibilityProbeState
    {
        public int AuthorizationCalls { get; set; }
        public int CatalogCalls { get; set; }
        public int PreflightCalls { get; set; }
        public int PublicationCalls { get; set; }
        public int TestRunCalls { get; set; }
        public int PolicyCalls { get; set; }
        public int SlotCalls { get; set; }
        public int CompilerCalls { get; set; }
        public int ResourceCalls { get; set; }
        public int Disposals { get; set; }
    }

    private sealed class CollectibilityAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        CollectibilityProbeState observations)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            observations.AuthorizationCalls++;
            var identity = new ClaimsIdentity(
                [
                    new Claim(IdentityClaimTypes.Permission, WorkflowPublishingPermissions.Read),
                    new Claim(IdentityClaimTypes.Permission, WorkflowPublishingPermissions.Manage),
                    new Claim(IdentityClaimTypes.TenantId, "collectibility-tenant"),
                    new Claim(IdentityClaimTypes.Normalized, "v1"),
                    new Claim(IdentityClaimTypes.Provider, "collectibility-provider"),
                    new Claim(ClaimTypes.NameIdentifier, "collectibility-actor")
                ],
                Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed class CollectibilityRequestSender(CollectibilityProbeState observations) : IRequestSender, IDisposable
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull
        {
            object value;
            switch (request)
            {
                case ListConstructableActivities:
                    observations.CatalogCalls++;
                    value = Array.Empty<ConstructableActivityView>();
                    break;
                case RunRuntimeRequirementPreflight:
                    observations.PreflightCalls++;
                    value = new RuntimeRequirementPreflightView(0, true, [], []);
                    break;
                case PublishWorkflow:
                    observations.PublicationCalls++;
                    value = new PublishedWorkflowView(
                        "collectibility-publication", "definition", "version", "version", "artifact", "default",
                        PublicationStatusView.Active, "source", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null,
                        "1.0.0", "hash", "root", 0, false);
                    break;
                case StartWorkflowTestRun:
                    observations.TestRunCalls++;
                    value = new WorkflowTestRunView(
                        "collectibility-test-run", "definition", "version", "artifact", null, "Completed", null, null,
                        DateTimeOffset.UtcNow.AddMinutes(1));
                    break;
                default:
                    value = RuntimeHelpers.GetUninitializedObject(typeof(T));
                    break;
            }

            return Task.FromResult((T)value);
        }

        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityCompiler(CollectibilityProbeState observations) : IWorkflowExecutableCompiler, IDisposable
    {
        public ValueTask<WorkflowExecutable> CompileAsync(WorkflowExecutableCompileRequest request, CancellationToken cancellationToken = default)
        {
            observations.CompilerCalls++;
            return ValueTask.FromResult((WorkflowExecutable)RuntimeHelpers.GetUninitializedObject(typeof(WorkflowExecutable)));
        }

        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilitySlotStore(CollectibilityProbeState observations) : IPublicationSlotStore, IDisposable
    {
        private static PublicationSlot Slot => new("collectibility-slot", "definition", "default", null, 0, DateTimeOffset.UtcNow);

        public ValueTask<PublicationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default)
        {
            observations.SlotCalls++;
            return ValueTask.FromResult<PublicationSlot?>(Slot);
        }

        public ValueTask<IReadOnlyCollection<PublicationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            observations.SlotCalls++;
            return ValueTask.FromResult<IReadOnlyCollection<PublicationSlot>>([Slot]);
        }

        public ValueTask<PublicationSlotTransitionResult> TryActivateAsync(string workflowDefinitionId, string slotName, string publicationId, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PublicationSlotTransitionResult(true, Slot with { ActivePublicationId = publicationId, Revision = expectedRevision + 1 }));

        public ValueTask<PublicationSlotTransitionResult> TryUnpublishAsync(string workflowDefinitionId, string slotName, long expectedRevision, DateTimeOffset updatedAt, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PublicationSlotTransitionResult(true, Slot with { Revision = expectedRevision + 1 }));

        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityPublicationStore(CollectibilityProbeState observations) : IPublicationRecordStore, IDisposable
    {
        public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default) => ValueTask.FromResult<PublicationRecord?>(null);
        public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<PublicationRecord>>([]);
        public ValueTask<bool> TryTransitionAsync(PublicationRecord publication, PublicationStatus expectedStatus, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityPolicyStore(CollectibilityProbeState observations) : IPublicationPolicyStore, IDisposable
    {
        public ValueTask<PublicationPolicy?> FindAsync(string? workflowDefinitionId, CancellationToken cancellationToken = default)
        {
            observations.PolicyCalls++;
            return ValueTask.FromResult<PublicationPolicy?>(new PublicationPolicy(
                workflowDefinitionId, PublicationPolicyDefaultAction.ReplaceDefaultSlot, "default", 0, DateTimeOffset.UtcNow));
        }

        public ValueTask<PublicationPolicyWriteResult> TrySaveAsync(PublicationPolicy policy, long expectedRevision, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PublicationPolicyWriteResult(true, policy));

        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityActivityPublisher(CollectibilityProbeState observations) : IActivityDefinitionPublisher, IDisposable
    {
        public Task<ActivityPublicationPreflightView> PreflightAsync(PreflightActivityDefinitionPublicationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult((ActivityPublicationPreflightView)RuntimeHelpers.GetUninitializedObject(typeof(ActivityPublicationPreflightView)));

        public Task<ActivityPublicationReceipt> PublishReviewedAsync(PublishActivityDefinitionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult((ActivityPublicationReceipt)RuntimeHelpers.GetUninitializedObject(typeof(ActivityPublicationReceipt)));

        public ValueTask<ActivityPublicationReceipt> GetReceiptAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult((ActivityPublicationReceipt)RuntimeHelpers.GetUninitializedObject(typeof(ActivityPublicationReceipt)));

        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityWorkflowTestRunStore(CollectibilityProbeState observations) : IWorkflowTestRunStore, IDisposable
    {
        public ValueTask SaveAsync(WorkflowTestRun testRun, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<WorkflowTestRun?> FindAsync(string testRunId, CancellationToken cancellationToken = default) => ValueTask.FromResult<WorkflowTestRun?>(null);
        public ValueTask SaveDraftSnapshotAsync(WorkflowTestRunDraftSnapshot snapshot, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<WorkflowTestRunDraftSnapshot?> FindDraftSnapshotAsync(string definitionVersionId, CancellationToken cancellationToken = default) => ValueTask.FromResult<WorkflowTestRunDraftSnapshot?>(null);
        public ValueTask<int> CleanupExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityActivityTestRunStore(CollectibilityProbeState observations) : IActivityDraftTestRunStore, IDisposable
    {
        public ValueTask<ActivityDraftTestRunCreateResult> TryCreateAsync(ActivityDraftTestRunReceipt receipt, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult((ActivityDraftTestRunCreateResult)RuntimeHelpers.GetUninitializedObject(typeof(ActivityDraftTestRunCreateResult)));
        public ValueTask<ActivityDraftTestRunReceipt?> FindAsync(string testRunId, CancellationToken cancellationToken = default) => ValueTask.FromResult<ActivityDraftTestRunReceipt?>(null);
        public ValueTask<ActivityDraftTestRunReceipt?> FindByIdempotencyKeyAsync(string operationScope, string draftId, string idempotencyKey, CancellationToken cancellationToken = default) => ValueTask.FromResult<ActivityDraftTestRunReceipt?>(null);
        public ValueTask<bool> TryUpdateAsync(ActivityDraftTestRunReceipt receipt, long expectedRevision, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<int> DeleteExpiredAsync(DateTimeOffset asOf, int limit, CancellationToken cancellationToken = default) => ValueTask.FromResult(0);
        public void Dispose() => observations.Disposals++;
    }

    private sealed class CollectibilityResourceHandler(CollectibilityProbeState observations) : IPermissionResourceHandler, IDisposable
    {
        public ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
        {
            observations.ResourceCalls++;
            return ValueTask.FromResult<PermissionEvaluationResult?>(PermissionEvaluationResult.Success);
        }

        public void Dispose() => observations.Disposals++;
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class Wave8PublishingCollectibilityCollection
{
    public const string Name = "Wave 8 Publishing Minimal API collectibility";
}
