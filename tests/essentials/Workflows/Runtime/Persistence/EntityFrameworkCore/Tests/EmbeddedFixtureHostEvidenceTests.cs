using System.Text.Json;
using CShells;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Locking.Core;
using Elsa.Mediator;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Primitives.Hosting;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Exercises the proposed Embedded selection in a generic CShells host. The authored 12 IDs are
/// kept separate from CShells' dependency closure.
/// No ASP.NET host or HTTP request is used.
/// </summary>
public sealed class EmbeddedFixtureHostEvidenceTests : IDisposable
{
    private const string ShellName = "embedded-runtime";
    private const string ResourceName = "Embedded";
    private const string RecoverySigningKey = "embedded-runtime-recovery-signing-key-32-bytes";
    private const string HierarchySigningKey = "embedded-runtime-hierarchy-signing-key-32-bytes";
    private readonly string databasePath = Path.Join(Path.GetTempPath(), $"elsa-embedded-fixture-{Guid.NewGuid():N}.db");

    private string ConnectionString => $"Data Source={databasePath};Pooling=False";

    private static readonly string[] SelectedFeatureIds =
    [
        "Primitives",
        "Serialization",
        "Mediator",
        "Events",
        "Expressions",
        "ActivitiesRuntime",
        "ActivitiesPrimitives",
        "ActivitiesControlFlow",
        "ActivitiesSequence",
        "WorkflowsRuntimeEntityFrameworkCore",
        "WorkflowsRuntimeResumption",
        "WorkflowsRuntimeTriggers"
    ];

    private static readonly string[] DependencyFeatureIds =
    [
        "ApiCapabilities",
        "Tasks",
        "WorkflowsRuntimeApi"
    ];

    [Fact]
    public async Task Proposed_embedded_selection_activates_in_non_http_host_binds_runtime_store_and_executes()
    {
        await using var host = CreateHost();
        var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var settings = shell.ServiceProvider.GetRequiredService<ShellSettings>();
        var effectiveIds = settings.EnabledFeatures.OrderBy(id => id, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            SelectedFeatureIds.Concat(DependencyFeatureIds).OrderBy(id => id, StringComparer.Ordinal),
            effectiveIds);

        await using var scope = shell.ServiceProvider.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<RuntimeWorkflowExecutionEntityFrameworkCoreOptions>();
        Assert.Equal("Sqlite", options.Provider);
        Assert.Equal(ResourceName, options.ConnectionName);
        Assert.Null(options.ConnectionString);

        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        Assert.Equal(databasePath, context.Database.GetDbConnection().DataSource);
        Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        var executable = RuntimeEventExecutableTestFixture.Create("embedded");
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
            new WorkflowExecutableSourceReference(
                "embedded-published-reference",
                executable.Identity.ArtifactId,
                "WorkflowDefinitionVersion",
                executable.Identity.DefinitionId,
                executable.Identity.ArtifactVersion,
                executable.Identity.DefinitionId,
                executable.Identity.DefinitionVersionId,
                executable.Identity.ArtifactVersion,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                WorkflowExecutableReferenceScope.Published));

        var started = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStartService>()
            .ExecuteAsync(new ExecuteWorkflow(executable.Identity.ArtifactId), CancellationToken.None);
        var executionStore = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
        Assert.IsType<EfWorkflowExecutionStateStore>(executionStore);
        var initialExecution = await executionStore.FindAsync(started.WorkflowExecutionId);
        Assert.True(
            started.CommandDispatchStatus == "Accepted",
            $"Expected Accepted, got {started.CommandDispatchStatus}; reason={started.Reason}; execution={initialExecution?.Status}; substatus={initialExecution?.SubStatus}");

        var bookmarks = scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>();
        Assert.IsType<EfBookmarkStateStore>(bookmarks);
        var bookmarkStates = await bookmarks.ListAllBookmarkStatesAsync(started.WorkflowExecutionId);
        var activityStates = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(started.WorkflowExecutionId);
        var incidents = await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>()
            .ListAsync(started.WorkflowExecutionId);
        var scheduler = await scope.ServiceProvider.GetRequiredService<ISchedulerStateStore>()
            .FindAsync(started.WorkflowExecutionId);
        Assert.True(
            bookmarkStates.Count == 1,
            $"Expected one Event bookmark; execution={initialExecution?.Status}/{initialExecution?.SubStatus}; " +
            $"activities=[{string.Join(", ", activityStates.Select(state => $"{state.Status}/{state.SubStatus}"))}]; " +
            $"incidents=[{string.Join("; ", incidents.Select(incident => $"{incident.FailureType}: {incident.Message}"))}]; " +
            $"scheduler pending={scheduler?.PendingWork.Count}, continuations={scheduler?.PendingContinuations.Count}, " +
            $"completion={scheduler?.PendingCompletionWork.Count}, generated={scheduler?.PendingGeneratedEvents.Count}.");
        var bookmark = Assert.Single(bookmarkStates);
        Assert.Equal(ActivityExecutionStatus.Suspended, Assert.Single(activityStates).Status);

        var resumed = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(
            new BookmarkResumeDispatchRequest(
                workflowExecutionId: started.WorkflowExecutionId,
                stimulusType: bookmark.StimulusType,
                stimulusHash: bookmark.StimulusHash,
                input: JsonSerializer.SerializeToElement(new EventReceived("embedded-ready"))));
        Assert.Equal(BookmarkResumeDispatchStatus.Dispatched, resumed.Status);

        Assert.Empty(await bookmarks.ListAllBookmarkStatesAsync(started.WorkflowExecutionId));
        Assert.Equal(ActivityExecutionStatus.Completed, Assert.Single(await scope.ServiceProvider
            .GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(started.WorkflowExecutionId)).Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await executionStore.FindAsync(started.WorkflowExecutionId))?.Status);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            File.Delete(path);
    }

    private ServiceProvider CreateHost()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Elsa:Persistence:DefaultResource"] = "primary",
                ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
                [$"Elsa:Persistence:Resources:primary:ConnectionName"] = ResourceName,
                [$"ConnectionStrings:{ResourceName}"] = ConnectionString,
                [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey"] = RecoverySigningKey,
                [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey"] = HierarchySigningKey
            }.Concat(SelectedFeatureIds.Select(id => KeyValuePair.Create(
                $"CShells:Shells:{ShellName}:Features:{id}",
                (string?)null))))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDistributedLockProvider, RuntimeEntityFrameworkCoreFeatureTests.ProcessLockProvider>();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddEfPersistenceResources(configuration, typeof(EmbeddedFixtureHostEvidenceTests).Assembly);
        services.AddCShells(shells => shells
            .WithAssemblies(
                typeof(PrimitivesFeature).Assembly,
                typeof(SerializationFeature).Assembly,
                typeof(MediatorFeature).Assembly,
                typeof(EventsFeature).Assembly,
                typeof(ExpressionsFeature).Assembly,
                typeof(ActivitiesRuntimeFeature).Assembly,
                typeof(ActivitiesPrimitivesFeature).Assembly,
                typeof(ActivitiesControlFlowFeature).Assembly,
                typeof(ActivitiesSequenceFeature).Assembly,
                typeof(ApiCapabilitiesFeature).Assembly,
                typeof(WorkflowsRuntimeApiFeature).Assembly,
                typeof(RuntimeEntityFrameworkCoreFeature).Assembly,
                typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                typeof(TasksFeature).Assembly)
            .WithConfigurationProvider(configuration));

        return services.BuildServiceProvider(validateScopes: true);
    }

}
