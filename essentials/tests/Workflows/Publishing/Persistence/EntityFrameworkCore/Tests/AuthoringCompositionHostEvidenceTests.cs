using CShells;
using CShells.AspNetCore.Configuration;
using CShells.AspNetCore.Extensions;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Locking.Core;
using Elsa.Mediator;
using Elsa.Mediator.Core.Contracts;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Primitives.Hosting;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Design.Api;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Proves the proposed Authoring selection in a real CShells HTTP host. The fixture's explicit
/// IDs remain the catalog contract; CShells supplies only the required runtime closure.
/// </summary>
public sealed class AuthoringCompositionHostEvidenceTests : IDisposable
{
    private const string ShellName = "default";
    private const string ResourceName = "Authoring";
    private const string RuntimePublicationFeature = "WorkflowsRuntimePublicationEntityFrameworkCorePersistence";
    private const string RuntimeArtifactsFeature = "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence";
    private const string TriggerType = "HostedPublicationTrigger";
    private const string StimulusHash = "hosted-publication-event";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-authoring-fixture-{Guid.NewGuid():N}.db");
    private readonly ITestOutputHelper output;

    public AuthoringCompositionHostEvidenceTests(ITestOutputHelper output) => this.output = output;

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    private static readonly string[] ExplicitFeatureIds =
    [
        "Primitives",
        "Serialization",
        "Mediator",
        "Events",
        "Expressions",
        "ApiCapabilities",
        "ActivitiesDesignApi",
        "ActivitiesDesignEntityFrameworkCore",
        "ActivitiesDesignReconciliation",
        "ClrActivityReconciliation",
        "WorkflowDesignValidations",
        "WorkflowsDesignApi",
        "WorkflowsDesignEntityFrameworkCore",
        "WorkflowsPublishing",
        "WorkflowsPublishingApi",
        "WorkflowsPublishingEntityFrameworkCore"
    ];

    [Fact]
    public async Task Authoring_routes_mount_with_the_current_standalone_selection()
    {
        await using var host = await StartHostAsync();
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
        var enabledFeatures = settings.EnabledFeatures.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        output.WriteLine($"Requested feature IDs ({ExplicitFeatureIds.Length}): {string.Join(", ", ExplicitFeatureIds)}");
        output.WriteLine($"Resolved enabled features ({enabledFeatures.Length}): {string.Join(", ", enabledFeatures)}");

        Assert.Equal(
            ExplicitFeatureIds.OrderBy(name => name, StringComparer.Ordinal),
            enabledFeatures);

        var endpoints = host.Services.GetServices<EndpointDataSource>()
            .Concat(shell.ServiceProvider.GetServices<EndpointDataSource>())
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(pattern => pattern is not null)
            .Select(pattern => $"/{pattern!.TrimStart('/')}")
            .ToHashSet(StringComparer.Ordinal);

        foreach (var prefix in new[] { "/design/activities", "/design/workflows", "/publishing", "/runtime" })
        {
            var routes = endpoints
                .Where(route => route.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(route => route, StringComparer.Ordinal)
                .ToArray();
            output.WriteLine($"Mounted routes {prefix} ({routes.Length}):");
            foreach (var route in routes)
                output.WriteLine($"  {route}");
        }

        Assert.Contains("/design/activities/catalog", endpoints);
        Assert.Contains("/design/workflows/definitions", endpoints);
        Assert.Contains("/publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish", endpoints);
        Assert.DoesNotContain(endpoints, route => route.StartsWith("/runtime/", StringComparison.Ordinal));
        Assert.Contains("/capabilities", endpoints);

        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var declared = Assert.Single((await provider.GetRequiredService<IApiCapabilityCatalog>().GetAsync()).Capabilities,
            declaration => declaration.Id == "elsa.api.publishing");
        var testRunRoutes = endpoints.Where(route => route.Contains("test-run", StringComparison.Ordinal)).ToArray();
        var testRunLinks = declared.Links.Where(link => link.Rel.Contains("test-run", StringComparison.Ordinal)).ToArray();
        Assert.Empty(testRunRoutes);
        Assert.Empty(testRunLinks);
        Assert.Null(provider.GetService<IWorkflowTestRunStarter>());
        Assert.Null(provider.GetService<ITestRunWaitOperations>());
        Assert.Null(provider.GetService<IActivityDraftTestRunService>());
        Assert.Contains("/publishing/incident-strategies", endpoints);
        Assert.Contains(declared.Links, link => link.Rel == "incident-strategies");
        var incidentCatalog = provider.GetRequiredService<IIncidentStrategyCatalog>();
        Assert.Equal(IncidentStrategyBuiltIns.FaultReference, incidentCatalog.DefaultStrategy);
        Assert.Equal(2, incidentCatalog.List().Count);

        var publishingOptions = provider.GetRequiredService<PublishingEntityFrameworkCoreOptions>();
        Assert.Equal("Sqlite", publishingOptions.Provider);
        Assert.Equal(ResourceName, publishingOptions.ConnectionName);
        Assert.Null(publishingOptions.ConnectionString);

        DbContext[] contexts =
        [
            scope.ServiceProvider.GetRequiredService<ActivitiesDesignDbContext>(),
            scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>(),
            scope.ServiceProvider.GetRequiredService<PublishingSnapshotReviewDbContext>()
        ];
        foreach (var context in contexts)
        {
            Assert.Equal(databasePath, context.Database.GetDbConnection().DataSource);
            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
            Assert.NotEmpty(appliedMigrations);
            Assert.Empty(pendingMigrations);
            output.WriteLine($"Persistence context {context.GetType().Name}: provider=Sqlite, resource={ResourceName}, applied={appliedMigrations.Count()}, pending={pendingMigrations.Count()}");
        }
    }

    [Fact]
    public async Task Authored_publication_compiles_activates_and_reopens_on_sqlite_without_the_legacy_runner()
    {
        const string definitionId = "runner-neutral-publish";
        const string versionId = "runner-neutral-publish-1";
        const string tenantId = PersistenceScope.DefaultValue;
        PublishedWorkflowView publication;
        await using (var host = await StartHostAsync(durableRuntime: true))
        {
            var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            Assert.Null(provider.GetService<IRuntimeResumptionService>());
            Assert.Null(provider.GetService<IWorkflowSchedulerDrainer>());
            Assert.Null(provider.GetService<IWorkflowStartDispatcher>());
            Assert.Null(provider.GetService<IStimulusRouter>());
            Assert.Null(provider.GetService<IWorkflowExecutionStateStore>());
            var settings = provider.GetRequiredService<ShellSettings>();
            Assert.DoesNotContain("WorkflowsRuntimeApi", settings.EnabledFeatures);
            Assert.DoesNotContain("WorkflowsRuntimeTriggers", settings.EnabledFeatures);

            await SeedTriggerWorkflowsAsync(provider, IncidentStrategyBuiltIns.ContinueWithIncidentsReference, (definitionId, versionId, "start"));
            var handler = provider.GetRequiredService<IRequestHandler<PublishWorkflow, PublishedWorkflowView>>();
            publication = await handler.Handle(new PublishWorkflow(versionId, TenantId: tenantId), CancellationToken.None);
        }

        await using (var host = await StartHostAsync(durableRuntime: true))
        {
            var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var slot = await provider.GetRequiredService<IWorkflowActivationAuthority>().FindAsync(definitionId, "default");
            Assert.Equal(publication.PublicationId, slot?.ActiveActivationId);
            var bindings = provider.GetRequiredService<IWorkflowTriggerBindingStore>();
            var active = Assert.Single((await bindings.ListByStimulusAsync(new("Event", StimulusHash))).Items);
            Assert.Equal(publication.ArtifactId, active.ArtifactId);
            Assert.Equal("start", active.ExecutableNodeId);
            Assert.Equal(publication.PublicationId, active.ActivationId);
            Assert.Equal(WorkflowActivationProjectionState.Active, await provider.GetRequiredService<IRecurringTriggerScheduleStore>()
                .FindActivationStateAsync(publication.PublicationId));
            var artifact = await provider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(publication.ArtifactId);
            Assert.Equal(IncidentStrategyBuiltIns.ContinueWithIncidentsReference, artifact!.IncidentStrategy);
            Assert.NotNull(await provider.GetRequiredService<IPublicationRecordStore>().FindAsync(publication.PublicationId));
        }
    }

    [Fact]
    public async Task Authoring_activation_reserves_an_exclusive_trigger_across_definitions_without_a_runner()
    {
        const string tenant = PersistenceScope.DefaultValue;
        await using var host = await StartHostAsync(exclusiveIngress: true);
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        Assert.Null(provider.GetService<IWorkflowStartDispatcher>());
        var catalog = provider.GetRequiredService<IIncidentStrategyCatalog>();
        Assert.True(catalog.TryGet(HostedIncidentStrategy.Reference, out _));
        Assert.IsType<HostedIncidentStrategy>(provider.GetRequiredService<IIncidentStrategyResolver>().Resolve(HostedIncidentStrategy.Reference));
        await SeedTriggerWorkflowsAsync(provider, HostedIncidentStrategy.Reference, ("first", "first-v1", "first-start"),
            ("second", "second-v1", "second-start"));

        var handler = provider.GetRequiredService<IRequestHandler<PublishWorkflow, PublishedWorkflowView>>();
        var first = await handler.Handle(new PublishWorkflow("first-v1", TenantId: tenant), CancellationToken.None);
        var failure = await Assert.ThrowsAsync<PublicationActivationException>(() =>
            handler.Handle(new PublishWorkflow("second-v1", TenantId: tenant), CancellationToken.None));
        Assert.Contains("Another source owns this endpoint reservation", failure.Message);

        var authority = provider.GetRequiredService<IWorkflowActivationAuthority>();
        Assert.Equal(first.PublicationId, (await authority.FindAsync("first", "default"))?.ActiveActivationId);
        Assert.Null((await authority.FindAsync("second", "default"))?.ActiveActivationId);
        var bindings = provider.GetRequiredService<IWorkflowTriggerBindingStore>();
        Assert.Equal(first.PublicationId, Assert.Single((await bindings.ListByStimulusAsync(new("Event", StimulusHash))).Items).ActivationId);
    }

    private static async Task SeedTriggerWorkflowsAsync(IServiceProvider provider, IncidentStrategyReference strategy,
        params (string Definition, string Version, string Node)[] workflowsToSeed)
    {
        const string tenantId = PersistenceScope.DefaultValue;
        const string activityVersionId = "trigger-1";
        var activities = provider.GetRequiredService<ActivitiesDesignDbContext>();
        activities.ActivityDefinitions.Add(new()
        {
            Id = TriggerType,
            TenantId = tenantId,
            ActivityTypeKey = TriggerType,
            Category = "Testing"
        });
        activities.ActivityDefinitionVersions.Add(new ActivityDefinitionVersion("1.0.0", TriggerType, executionType: ActivityExecutionType.Trigger)
        {
            Id = activityVersionId,
            TenantId = tenantId,
            ProviderKey = "test",
            ProviderSchemaVersion = "1",
            ConsumerKey = "test",
            ConsumerSchemaVersion = "1",
            DescriptorPayloadSource = "{}",
            SourceKind = "test",
            SourceId = activityVersionId
        });
        await activities.SaveChangesAsync();

        var serializer = provider.GetRequiredService<IPayloadSerializer>();
        var workflows = provider.GetRequiredService<WorkflowsDesignDbContext>();
        foreach (var (definitionId, versionId, nodeId) in workflowsToSeed)
        {
            var state = WorkflowDefinitionState.Empty with
            {
                RootActivity = new(nodeId, activityVersionId, [], []),
                StrategyOptions = new() { IncidentStrategy = strategy }
            };
            workflows.Versions.Add(new WorkflowDefinitionVersion(definitionId, "1.0.0", serializer.Serialize(state))
            {
                Id = versionId,
                TenantId = tenantId,
                Definition = new() { Id = definitionId, TenantId = tenantId, Name = definitionId }
            });
        }
        await workflows.SaveChangesAsync();
    }

    private sealed class HostedTriggerProvider(TriggerCardinality cardinality) : IActivityTriggerStimulusProvider
    {
        public TriggerCardinality Cardinality { get; } = cardinality;

        public ActivityTriggerStimulusResult Describe(ExecutableNode node) => node.ActivityType == TriggerType
            ? ActivityTriggerStimulusResult.Recognized([new("Event", StimulusHash)])
            : ActivityTriggerStimulusResult.NotRecognized;
    }

    private sealed class HostedIncidentStrategy : IIncidentStrategy
    {
        internal static readonly IncidentStrategyReference Reference = new("Fixture.Incident", "1");

        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(IncidentResolutionActions.FaultWorkflow());
    }

    public void Dispose()
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private async Task<WebApplication> StartHostAsync(bool durableRuntime = false, bool exclusiveIngress = false)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        const string features = $"CShells:Shells:{ShellName}:Features";
        var settings = ExplicitFeatureIds.ToDictionary(id => $"{features}:{id}", _ => (string?)null, StringComparer.Ordinal);
        settings["Elsa:Persistence:DefaultResource"] = "primary";
        settings["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite";
        settings["Elsa:Persistence:Resources:primary:ConnectionName"] = ResourceName;
        settings[$"ConnectionStrings:{ResourceName}"] = ConnectionString;
        if (durableRuntime)
        {
            settings[$"{features}:{RuntimePublicationFeature}"] = null;
            settings[$"{features}:{RuntimeArtifactsFeature}:RecoveryContinuationSigningKey"] = "authoring-runtime-recovery-test-signing-key";
        }
        if (durableRuntime || exclusiveIngress)
        {
            builder.Services.AddSingleton<IDistributedLockProvider, ImmediateLockProvider>();
            builder.Services.AddSingleton<IActivityTriggerStimulusProvider>(new HostedTriggerProvider(
                exclusiveIngress ? TriggerCardinality.Exclusive : TriggerCardinality.FanOut));
        }
        if (exclusiveIngress)
            builder.Services.AddIncidentStrategy<HostedIncidentStrategy>(new(HostedIncidentStrategy.Reference, "Fixture", "Hosted test strategy."));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        builder.Configuration.AddConfiguration(configuration);
        builder.Services.AddEfPersistenceResources(configuration, typeof(AuthoringCompositionHostEvidenceTests).Assembly);
        builder.Services.AddCShellsAspNetCore(shells => shells
            .WithAssemblies(
                typeof(PrimitivesFeature).Assembly,
                typeof(SerializationFeature).Assembly,
                typeof(MediatorFeature).Assembly,
                typeof(EventsFeature).Assembly,
                typeof(ExpressionsFeature).Assembly,
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(ActivitiesDesignApiFeature).Assembly,
                typeof(ActivitiesDesignEntityFrameworkCoreFeature).Assembly,
                typeof(ActivitiesDesignReconciliationFeature).Assembly,
                typeof(ClrActivityReconciliationFeature).Assembly,
                typeof(WorkflowDesignValidationsFeature).Assembly,
                typeof(WorkflowsDesignApiFeature).Assembly,
                typeof(WorkflowsDesignEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsPublishingFeature).Assembly,
                typeof(WorkflowsPublishingApiFeature).Assembly,
                typeof(PublishingEntityFrameworkCoreFeature).Assembly,
                typeof(RuntimePublicationEntityFrameworkCoreFeature).Assembly,
                typeof(RuntimeArtifactsEntityFrameworkCoreFeature).Assembly,
                typeof(TasksFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration)
            .WithWebRouting(options => options.EnablePathRouting = true));

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }
}
