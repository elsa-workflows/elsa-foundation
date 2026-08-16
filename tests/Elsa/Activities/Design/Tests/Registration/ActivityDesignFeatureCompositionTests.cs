using System.Security.Claims;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Runtime;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Activities.Design.Tests.Registration;

/// <summary>
/// Provider-neutral conversion (T070) of the §2.23.1 feature-composition rows that lived in the
/// EF-referencing <c>FeatureRegistrationTests</c>. Every feature exercised here — the runtime,
/// reconciliation, API, and CLR-source features — is provider-agnostic, so its registration contract is
/// unaffected by the EF store removal (T072/T073). Only the SQLite EF shell-feature row from the
/// original file is genuinely EF-mechanism and is dispositioned separately. Each test composes the
/// feature into a real <see cref="ServiceCollection"/>, pre-registers the minimal cross-feature
/// prerequisites, builds the provider, and asserts the feature's claimed services resolve cleanly.
/// </summary>
public sealed class ActivityDesignFeatureCompositionTests
{
    [Fact]
    public void ActivitiesRuntimeFeature_RegistersCanonicalMaterializationAndTypeDiscovery()
    {
        var services = MinimalServices();
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.Contains(services, d => d.ServiceType == typeof(Elsa.Workflows.Runtime.Core.Contracts.IRuntimeActivityInputMaterializer)
            && d.ImplementationType == typeof(Elsa.Workflows.Runtime.Core.Services.RuntimeActivityInputMaterializer));
        Assert.Contains(services, d => d.ServiceType == typeof(Elsa.Activities.Runtime.Services.ActivityInputHydrator));
        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask)
            && d.ImplementationType == typeof(Elsa.Activities.Runtime.Tasks.RegisterActivityTypesStartupTask));
    }

    [Fact]
    public void ActivitiesDesignReconciliationFeature_RegistersReconcilerHasherStartupTaskAndHandler()
    {
        var services = MinimalServices();
        StubReconcilerDependencies(services);

        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask)
            && d.ImplementationType == typeof(Elsa.Activities.Design.Reconciliation.Services.ActivityVersionReconcilerStartupTask));

        Assert.Contains(services, d => d.ServiceType == typeof(IEventHandler)
            && d.ImplementationType == typeof(Elsa.Activities.Design.Reconciliation.Handlers.CollectActivityVersions));

        using var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService<IActivityVersionReconciler>());
    }

    [Fact]
    public void ActivitiesDesignApiFeature_RegistersActivityAvailabilityEvaluator()
    {
        var services = MinimalServices();

        new ActivitiesDesignApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IActivityAvailabilityEvaluator>());
    }

    [Fact]
    public async Task ActivitiesDesignApiFeature_Registers_Canonical_Contract_Proposal_Capability_Relations()
    {
        var services = MinimalServices();
        services.AddApiCapabilities();
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var document = await provider.GetRequiredService<IApiCapabilityCatalog>().GetAsync();
        var capability = Assert.Single(document.Capabilities, x => x.Id == "elsa.api.activity-design");

        Assert.Contains(capability.Links, x =>
            x.Rel == "activity-draft-contract-proposals" &&
            x.Href == "design/activities/drafts/{draftId}/contract-proposals" &&
            x.Templated);
        Assert.Contains(capability.Links, x =>
            x.Rel == "activity-draft-contract-proposals-apply" &&
            x.Href == "design/activities/drafts/{draftId}/contract-proposals/apply" &&
            x.Templated);
    }

    [Fact]
    public async Task ActivitiesDesignApiFeature_Registers_Request_Scoped_Tenant_And_Permission_Authorization()
    {
        var services = MinimalServices();
        services.AddFoundationIdentityAbstractions(options =>
            options.NormalizedAuthenticationTypes = new HashSet<string>(["test"], StringComparer.Ordinal));
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(IdentityClaimTypes.Normalized, "v1"),
                new Claim(IdentityClaimTypes.TenantId, "tenant-a"),
                new Claim(ClaimTypes.NameIdentifier, "actor-a"),
                new Claim(IdentityClaimTypes.Permission, HttpContextActivityDesignAuthorizationContext.AuthorPermission),
                new Claim(IdentityClaimTypes.Permission, HttpContextActivityDesignAuthorizationContext.ProviderPayloadReadPermission)
            ], "test"))
        };

        using var scope = provider.CreateScope();
        var authoring = scope.ServiceProvider.GetRequiredService<IActivityAuthoringContextAsync>();
        var dependencies = scope.ServiceProvider.GetRequiredService<IActivityDependencyContextAsync>();
        var legacyDependencies = scope.ServiceProvider.GetRequiredService<IActivityDependencyAuthorizationContext>();

        Assert.Same(authoring, dependencies);
        Assert.Same(dependencies, legacyDependencies);
        Assert.True(await authoring.CanAuthorProviderAsync("elsa.activity-graph"));
        Assert.True(await authoring.CanReadProviderPayloadAsync("elsa.activity-graph"));
        Assert.Equal("actor-a", authoring.ActorId);
        Assert.True(await dependencies.CanReadAsync(new("ActivityVersion", "definition", TenantId: "tenant-a")));
        Assert.False(await dependencies.CanReadAsync(new("ActivityVersion", "definition", TenantId: "tenant-b")));
        Assert.NotEmpty(await dependencies.GetAuthorizationProfileAsync());
    }

    [Fact]
    public async Task ActivitiesDesignApiFeature_adapts_legacy_host_contexts_when_async_replacements_are_omitted()
    {
        var services = MinimalServices();
        services.AddScoped<IActivityAuthoringContext, LegacyAuthoringContext>();
        services.AddScoped<IActivityDependencyAuthorizationContext, LegacyDependencyContext>();
        new ActivitiesDesignApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var authoring = scope.ServiceProvider.GetRequiredService<IActivityAuthoringContextAsync>();
        var dependencies = scope.ServiceProvider.GetRequiredService<IActivityDependencyContextAsync>();

        Assert.IsType<LegacyActivityAuthoringContextAdapter>(authoring);
        Assert.IsType<LegacyActivityDependencyContextAdapter>(dependencies);
        Assert.Equal("legacy-tenant", authoring.TenantId);
        Assert.Equal("legacy-actor", authoring.ActorId);
        Assert.Equal("legacy-profile", await authoring.GetAuthorizationProfileAsync());
        Assert.True(await authoring.CanAuthorProviderAsync("legacy"));
        Assert.True(await authoring.CanReadProviderPayloadAsync("legacy"));
        Assert.True(await authoring.CanManageActivityDefinitionsAsync());
        Assert.Equal("legacy-tenant", dependencies.TenantId);
        Assert.Equal("legacy-profile", await dependencies.GetAuthorizationProfileAsync());
        Assert.True(await dependencies.CanReadAsync(new("ActivityVersion", "definition")));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authoring.GetAuthorizationProfileAsync(canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authoring.CanAuthorProviderAsync("legacy", canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authoring.CanReadProviderPayloadAsync("legacy", canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authoring.CanManageActivityDefinitionsAsync(canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dependencies.GetAuthorizationProfileAsync(canceled.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dependencies.CanReadAsync(new("ActivityVersion", "definition"), canceled.Token).AsTask());
    }

    [Fact]
    public async Task ActivitiesDesignApiFeature_adapts_a_legacy_replacement_registered_after_feature_configuration()
    {
        var services = MinimalServices();
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Scoped<IActivityDependencyAuthorizationContext, LegacyDependencyContext>());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var dependencies = scope.ServiceProvider.GetRequiredService<IActivityDependencyContextAsync>();

        Assert.IsType<LegacyActivityDependencyContextAdapter>(dependencies);
        Assert.Equal("legacy-tenant", dependencies.TenantId);
        Assert.Equal("legacy-profile", await dependencies.GetAuthorizationProfileAsync());
        Assert.True(await dependencies.CanReadAsync(new("ActivityVersion", "definition")));
    }

    [Fact]
    public void Dependency_replacement_contract_requires_marker_and_rejects_duplicate_descriptors()
    {
        var unmarked = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            unmarked.EnsureReplacementContract<UnmarkedDependencyContract, UnmarkedDependencyContract>());

        var duplicate = new ServiceCollection();
        duplicate.AddScoped<IActivityDependencyContextAsync, CustomDependencyContext>();
        duplicate.AddScoped<IActivityDependencyContextAsync, SecondDependencyContext>();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            duplicate.EnsureReplacementContract<IActivityDependencyContextAsync, HttpContextActivityDesignAuthorizationContext>());
        Assert.Contains(nameof(CustomDependencyContext), exception.Message);
        Assert.Contains(nameof(SecondDependencyContext), exception.Message);
    }

    [Fact]
    public void Dependency_replacement_contract_resolves_default_custom_and_idempotent_markers()
    {
        var defaults = new ServiceCollection();
        defaults.AddFoundationIdentityAbstractions();
        defaults.EnsureReplacementContract<IActivityDependencyContextAsync, HttpContextActivityDesignAuthorizationContext>();
        defaults.EnsureReplacementContract<IActivityDependencyContextAsync, HttpContextActivityDesignAuthorizationContext>();
        using (var provider = defaults.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
        using (var defaultScope = provider.CreateScope())
            Assert.IsType<HttpContextActivityDesignAuthorizationContext>(defaultScope.ServiceProvider.GetRequiredService<IActivityDependencyContextAsync>());

        var custom = new ServiceCollection();
        custom.AddFoundationIdentityAbstractions();
        custom.AddScoped<IActivityDependencyContextAsync, CustomDependencyContext>();
        new ActivitiesDesignApiFeature().ConfigureServices(custom);
        using var customProvider = custom.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = customProvider.CreateScope();
        Assert.IsType<CustomDependencyContext>(scope.ServiceProvider.GetRequiredService<IActivityDependencyContextAsync>());
    }

    [Fact]
    public void Dependency_late_conflict_is_reported_by_startup_validation()
    {
        var services = MinimalServices();
        services.AddFoundationIdentityAbstractions();
        new ActivitiesDesignApiFeature().ConfigureServices(services);
        services.AddScoped<IActivityDependencyContextAsync, SecondDependencyContext>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);
        Assert.Contains("IActivityDependencyContextAsync", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(SecondDependencyContext), exception.Message, StringComparison.Ordinal);
        Assert.Contains("exactly one tagged implementation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivitiesDesignApiFeature_Provides_A_Process_Stable_Development_Cursor_Key_When_Host_Omits_One()
    {
        var services = MinimalServices();

        new ActivitiesDesignApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var codec = provider.GetRequiredService<IActivityDependencyCursorCodec>();
        var managementCodec = provider.GetRequiredService<IActivityManagementCursorCodec>();
        var forkCodec = provider.GetRequiredService<IActivityForkCandidateIdCodec>();
        var state = new ActivityDependencyCursorState("tenant", "profile", "version", "Outbound", false, ["Versions"], "watermark", 1);
        var managementState = new ActivityManagementCursorState("scope", 25, 42);

        var decoded = codec.Decode(codec.Encode(state));
        var decodedManagement = managementCodec.Decode(managementCodec.Encode(managementState));
        var forkState = new ActivityForkCandidateIdState(
            "reservation-1",
            $"sha256:{new string('a', 64)}",
            new DateTimeOffset(2026, 7, 18, 12, 15, 0, TimeSpan.Zero));
        var decodedFork = forkCodec.Decode(forkCodec.Encode(forkState));
        Assert.Equal(state.TenantScope, decoded.TenantScope);
        Assert.Equal(state.RootVersionId, decoded.RootVersionId);
        Assert.Equal(state.Include, decoded.Include);
        Assert.Equal(state.Position, decoded.Position);
        Assert.Equal(managementState, decodedManagement);
        Assert.Equal(forkState, decodedFork);
        Assert.Throws<ActivityManagementCursorInvalidException>(() =>
            managementCodec.Decode(managementCodec.Encode(new("scope", 25, -1))));
    }

    [Fact]
    public void ActivitiesDesignApiFeature_RegistersActivityFeatureAttributionResolver()
    {
        var services = MinimalServices();

        new ActivitiesDesignApiFeature().ConfigureServices(services);

        // The resolver's own dependencies (type registry, runtime feature catalog) are optional: a shell
        // composing neither must still resolve the service and degrade to null attribution.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var resolver = scope.ServiceProvider.GetService<IActivityFeatureAttributionResolver>();
        Assert.IsType<ActivityFeatureAttributionResolver>(resolver);
    }

    [Fact]
    public void ActivitiesDesignApiFeature_RegistersActivityAvailabilitySettingsStore()
    {
        var services = MinimalServices();

        new ActivitiesDesignApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IActivityAvailabilitySettingsStore>());
    }

    [Fact]
    public void ActivitiesDesignApiFeature_RegistersActivityAvailabilityDiagnosticsProjector()
    {
        var services = MinimalServices();

        new ActivitiesDesignApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IActivityAvailabilityDiagnosticsProjector>());
    }

    [Fact]
    public void ActivitiesDesignApiFeature_UsesHostConfiguredActivityAvailabilityOptions()
    {
        const string writeLineTypeKey = "Elsa.Activities.Primitives.Activities.WriteLine";
        const string runJavaScriptTypeKey = "Elsa.Activities.Scripting.Activities.RunJavaScript";
        var services = MinimalServices();
        services.AddOptions();
        services.Configure<ActivityAvailabilityOptions>(options =>
            options.Exclude.ActivityTypes = [runJavaScriptTypeKey]);

        new ActivitiesDesignApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var evaluator = provider.GetRequiredService<IActivityAvailabilityEvaluator>();

        var result = evaluator.FilterAddable(
        [
            new ActivityDefinitionModel("write-line", writeLineTypeKey, "Test", "WriteLine", null),
            new ActivityDefinitionModel("run-js", runJavaScriptTypeKey, "Test", "RunJavaScript", null)
        ]);

        var definition = Assert.Single(result);
        Assert.Equal(writeLineTypeKey, definition.ActivityTypeKey);
    }

    [Fact]
    public void ClrActivityReconciliationFeature_RegistersClrReconciliationSource()
    {
        var services = MinimalServices();

        new ClrActivityReconciliationFeature
        {
            Options = { FolderPath = Path.Combine(Path.GetTempPath(), "activities") }
        }.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();

        var source = provider.GetService<IActivityReconciliationSource>();
        Assert.IsType<ClrActivityReconciliationSource>(source);
        Assert.Equal("CLR", source.SourceKind);
    }

    /// <summary>
    /// Foundational services the host normally registers before any feature runs.
    /// </summary>
    private static ServiceCollection MinimalServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IInlineEventPublisher, StubEventPublisher>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddLogging();
        return services;
    }

    private static void StubReconcilerDependencies(IServiceCollection services)
    {
        services.AddSingleton<IIdentityGenerator, StubIdentityGenerator>();
        services.AddSingleton<IActivityDefinitionHasher, StubActivityDefinitionHasher>();
        services.AddSingleton<IActivityDefinitionStore, ThrowingActivityDefinitionStore>();
        services.AddSingleton<IActivityDefinitionVersionStore, ThrowingActivityDefinitionVersionStore>();
        services.AddSingleton<IAddActivityDefinitionCommand, StubAddActivityDefinitionCommand>();
        services.AddSingleton<IAddActivityDefinitionVersionCommand, StubAddActivityDefinitionVersionCommand>();
    }

    private sealed class StubEventPublisher : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubIdentityGenerator : IIdentityGenerator
    {
        public string Generate() => Guid.NewGuid().ToString("N");
    }

    private sealed class StubActivityDefinitionHasher : IActivityDefinitionHasher
    {
        public string Hash(IActivityDefinition definition, IActivityDefinitionVersion version) => "stub";
    }

    private sealed class StubAddActivityDefinitionCommand : IAddActivityDefinitionCommand
    {
        public Task<ActivityDefinitionCreated> Execute(
            Elsa.Persistence.Core.Design.DesignOperationKey operationKey,
            ActivityDefinition definition,
            ActivityDefinitionVersion version,
            CancellationToken cancellation) =>
            Task.FromResult(new ActivityDefinitionCreated(definition.Id, version.Id, version.Version, version.Hash));
    }

    private sealed class StubAddActivityDefinitionVersionCommand : IAddActivityDefinitionVersionCommand
    {
        public Task<ActivityDefinitionVersionAdded> Execute(
            Elsa.Persistence.Core.Design.DesignOperationKey operationKey,
            ActivityDefinitionVersion version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ActivityDefinitionVersionAdded(version.DefinitionId, version.Id, version.Version, version.Hash));
    }

    private sealed class ThrowingActivityDefinitionStore : IActivityDefinitionStore
    {
        private const string Message = "Reconciler registration test must not reach the definition store.";
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    }

    private sealed class ThrowingActivityDefinitionVersionStore : IActivityDefinitionVersionStore
    {
        private const string Message = "Reconciler registration test must not reach the version store.";
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException(Message);
    }

    private sealed class LegacyAuthoringContext : IActivityAuthoringContext
    {
        public string? TenantId => "legacy-tenant";
        public string ActorId => "legacy-actor";
        public string AuthorizationProfile => "legacy-profile";
        public bool CanAuthorProvider(string providerKey) => providerKey == "legacy";
        public bool CanReadProviderPayload(string providerKey) => providerKey == "legacy";
        public bool CanManageActivityDefinitions => true;
    }

    private sealed class LegacyDependencyContext : IActivityDependencyAuthorizationContext
    {
        public string? TenantId => "legacy-tenant";
        public string AuthorizationProfile => "legacy-profile";
        public bool CanRead(ActivityDefinitionReference reference) => true;
    }

    private class UnmarkedDependencyContract : IActivityDependencyContextAsync
    {
        public string? TenantId => null;
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => new("unmarked");
        public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) => new(false);
    }

    private sealed class CustomDependencyContext : UnmarkedDependencyContract
    {
    }

    private sealed class SecondDependencyContext : UnmarkedDependencyContract
    {
    }
}
