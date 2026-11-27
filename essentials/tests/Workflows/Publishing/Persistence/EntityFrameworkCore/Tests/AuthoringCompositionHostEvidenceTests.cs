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
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Locking.Core;
using Elsa.Mediator;
using Elsa.Mediator.Core.Contracts;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Primitives.Hosting;
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
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authoring_routes_mount_and_explicit_legacy_selection_retains_runtime_routes(bool legacyRuntime)
    {
        await using var host = await StartHostAsync(legacyRuntime: legacyRuntime);
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
        var enabledFeatures = settings.EnabledFeatures.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        output.WriteLine($"Requested feature IDs ({ExplicitFeatureIds.Length}): {string.Join(", ", ExplicitFeatureIds)}");
        output.WriteLine($"Resolved enabled features ({enabledFeatures.Length}): {string.Join(", ", enabledFeatures)}");

        var selected = legacyRuntime ? ExplicitFeatureIds.Concat(["WorkflowsRuntimeApi", "WorkflowsRuntimeTriggers"]) : ExplicitFeatureIds;
        Assert.Equal(
            selected.OrderBy(name => name, StringComparer.Ordinal),
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
        if (legacyRuntime)
        {
            Assert.Contains("/runtime/workflows/executables", endpoints);
            Assert.Contains("/runtime/workflows/stimuli", endpoints);
        }
        else
            Assert.DoesNotContain(endpoints, route => route.StartsWith("/runtime/", StringComparison.Ordinal));
        Assert.Contains("/capabilities", endpoints);

        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var publishingOptions = scope.ServiceProvider.GetRequiredService<PublishingEntityFrameworkCoreOptions>();
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
        const string nodeId = "start";
        const string activityVersionId = "trigger-1";
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

            var state = WorkflowDefinitionState.Empty with { RootActivity = new(nodeId, activityVersionId, [], []) };
            var serializer = provider.GetRequiredService<IPayloadSerializer>();
            var workflows = provider.GetRequiredService<WorkflowsDesignDbContext>();
            workflows.Versions.Add(new WorkflowDefinitionVersion(definitionId, "1.0.0", serializer.Serialize(state))
            {
                Id = versionId,
                TenantId = tenantId,
                Definition = new() { Id = definitionId, TenantId = tenantId, Name = definitionId }
            });
            await workflows.SaveChangesAsync();
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
            Assert.Equal(nodeId, active.ExecutableNodeId);
            Assert.Equal(publication.PublicationId, active.ActivationId);
            Assert.Equal(WorkflowActivationProjectionState.Active, await provider.GetRequiredService<IRecurringTriggerScheduleStore>()
                .FindActivationStateAsync(publication.PublicationId));
            Assert.NotNull(await provider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(publication.ArtifactId));
            Assert.NotNull(await provider.GetRequiredService<IPublicationRecordStore>().FindAsync(publication.PublicationId));
        }
    }

    private sealed class HostedTriggerProvider : IActivityTriggerStimulusProvider
    {
        public ActivityTriggerStimulusResult Describe(ExecutableNode node) => node.ActivityType == TriggerType
            ? ActivityTriggerStimulusResult.Recognized([new("Event", StimulusHash)])
            : ActivityTriggerStimulusResult.NotRecognized;
    }

    public void Dispose()
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private async Task<WebApplication> StartHostAsync(bool legacyRuntime = false, bool durableRuntime = false)
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
        if (legacyRuntime)
        {
            settings[$"{features}:WorkflowsRuntimeApi"] = null;
            settings[$"{features}:WorkflowsRuntimeTriggers"] = null;
        }
        if (durableRuntime)
        {
            settings[$"{features}:{RuntimePublicationFeature}"] = null;
            settings[$"{features}:{RuntimeArtifactsFeature}:RecoveryContinuationSigningKey"] = "authoring-runtime-recovery-test-signing-key";
            builder.Services.AddSingleton<IDistributedLockProvider, ImmediateLockProvider>();
            builder.Services.AddSingleton<IActivityTriggerStimulusProvider, HostedTriggerProvider>();
        }
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
                typeof(TasksFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly)
            .WithConfigurationProvider(builder.Configuration)
            .WithWebRouting(options => options.EnablePathRouting = true));

        var app = builder.Build();
        app.MapShells();
        await app.StartAsync();
        return app;
    }
}
