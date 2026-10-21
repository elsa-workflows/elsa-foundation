using System.Text.Json;
using System.Text;
using CShells.Lifecycle;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation.Clr;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime;
using Elsa.Etl.Workflows.Design;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Locking.FileSystem;
using Elsa.Mediator;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Hosting;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Validations;
using Elsa.Workflows.Publishing;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Api.Requests;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Tasks.Core;
using WorkflowSequence = Elsa.Activities.Sequence.Activities.Sequence;
using WorkflowFlowchart = Elsa.Activities.Flowchart.Activities.Flowchart;
using WriteLineActivity = Elsa.Activities.Primitives.Activities.WriteLine;

namespace Elsa.Etl.Tests;

/// <summary>
/// Authors and publishes one parent workflow through the persisted design and production publication paths.
/// The authoring service provider is disposed before the fixture is returned; only durable files and immutable
/// publication snapshots cross into the separate runtime-only provider.
/// </summary>
internal sealed class EtlPublishedWorkflowFixture : IAsyncDisposable
{
    private const string DefinitionId = "etl-published-parent";
    private const string PipelineDefinitionId = "orders";
    private const string ActivityPipelineInput = nameof(RunEtl.Pipeline);
    private const string WorkflowRecoverySigningKey = "etl-published-workflow-recovery-key-32";
    private const string WorkflowHierarchySigningKey = "etl-published-workflow-hierarchy-key-32";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private bool _disposed;

    private EtlPublishedWorkflowFixture(
        string rootDirectory,
        EtlScope scope,
        IReadOnlyList<EtlHostFileBindingOptions> bindings,
        EtlArtifact originalArtifact,
        EtlArtifact updatedArtifact,
        WorkflowExecutable executable,
        WorkflowExecutableSourceReference reference)
    {
        RootDirectory = rootDirectory;
        WorkflowDatabase = Path.Combine(rootDirectory, "workflow.db");
        EtlRuntimeDatabase = Path.Combine(rootDirectory, "etl-runtime.db");
        EtlDesignDatabase = Path.Combine(rootDirectory, "etl-design.db");
        SnapshotRoot = Path.Combine(rootDirectory, "snapshots");
        OutputRoot = Path.Combine(rootDirectory, "output");
        Scope = scope;
        Bindings = bindings;
        OriginalArtifact = originalArtifact;
        UpdatedArtifact = updatedArtifact;
        Executable = executable;
        Reference = reference;
    }

    public string RootDirectory { get; }
    public string WorkflowDatabase { get; }
    public string EtlRuntimeDatabase { get; }
    public string EtlDesignDatabase { get; }
    public string SnapshotRoot { get; }
    public string OutputRoot { get; }
    public EtlScope Scope { get; }
    public IReadOnlyList<EtlHostFileBindingOptions> Bindings { get; }
    public EtlArtifact OriginalArtifact { get; }
    public EtlArtifact UpdatedArtifact { get; }
    public WorkflowExecutable Executable { get; }
    public WorkflowExecutableSourceReference Reference { get; }

    public static async Task<EtlPublishedWorkflowFixture> CreateAsync(bool flowchart)
    {
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.PublishedWorkflow", Guid.NewGuid().ToString("N"));
        WorkflowExecutionHarness? authoring = null;
        try
        {
            Directory.CreateDirectory(directory);
            var scope = new EtlScope(null, "partition-test");
            var sourcePath = Path.Combine(directory, "source.csv");
            var lookupPath = Path.Combine(directory, "lookup.csv");
            var workflowDatabase = Path.Combine(directory, "workflow.db");
            var etlDesignDatabase = Path.Combine(directory, "etl-design.db");
            var snapshotRoot = Path.Combine(directory, "snapshots");

            await File.WriteAllTextAsync(sourcePath, "id,name\n1,Ada\n", new UTF8Encoding(false));
            await File.WriteAllTextAsync(lookupPath, "id,category\n1,one\n", new UTF8Encoding(false));
            Directory.CreateDirectory(snapshotRoot);
            var bindings = EtlNativeTestFileBindings.Create(scope, directory);

            authoring = BuildAuthoringProvider(directory, workflowDatabase, etlDesignDatabase);
            foreach (var initializer in authoring.Services.GetServices<IShellInitializer>())
                await initializer.InitializeAsync();
            authoring.InitializeActivityTypes();

            IReadOnlyCollection<(ActivityDefinition Definition, ActivityDefinitionVersion Version)> sourceCatalog;
            WorkflowExecutable executable;
            WorkflowExecutableSourceReference reference;
            string parentVersionId;
            EtlArtifact originalArtifact;
            EtlArtifact updatedArtifact;

            await using (var operation = await OpenPartitionScopeAsync(authoring.Services, scope))
            {
                var services = operation.ServiceProvider;
                sourceCatalog = NativeEtlAuthoredComposite.CreateSourceCatalog(services, ActivityTypeKeys(flowchart));
                var compiler = services.GetRequiredService<IEtlArtifactCompiler>();
                var definitions = services.GetRequiredService<IEtlDefinitionStore>();
                var initialPipeline = EtlTestPipelines.Create();
                originalArtifact = Assert.IsType<EtlArtifact>(compiler.Compile(PipelineDefinitionId, 1, initialPipeline).Artifact);
                Assert.Equal(EtlDefinitionWriteStatus.Created,
                    (await definitions.CreateDraftAsync(scope, PipelineDefinitionId, initialPipeline)).Status);
                Assert.Equal(EtlPublicationStatus.Published,
                    (await definitions.PublishAsync(scope, PipelineDefinitionId, 1, originalArtifact)).Status);

                var sourcePublisher = services.GetRequiredService<IActivitySourceVersionPublisher>();
                foreach (var entry in sourceCatalog)
                    await sourcePublisher.PublishAsync(entry.Definition, entry.Version);

                var state = CreateWorkflowState(sourceCatalog, originalArtifact, flowchart);
                var definition = new WorkflowDefinition { Id = DefinitionId, Name = "Published ETL parent" };
                var draft = new WorkflowDefinitionDraft
                {
                    Id = DefinitionId + "-draft",
                    WorkflowDefinitionId = DefinitionId,
                    State = state
                };
                var created = await services.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                    OperationKey("definition-create"), definition, draft);
                var savedDraft = await services.GetRequiredService<IWorkflowDefinitionDraftStore>().FindByIdAsync(created.DraftId)
                    ?? throw new InvalidOperationException("The workflow design store did not return its created draft.");
                parentVersionId = await services.GetRequiredService<IPromoteDraftToVersionCommand>().Execute(
                    OperationKey("parent-promote-v1"),
                    savedDraft.Id,
                    "1.0.0",
                    WorkflowDraftStateHash.Compute(savedDraft.StateSource));
            }

            await using (var operation = await OpenPartitionScopeAsync(authoring.Services, scope))
            {
                var services = operation.ServiceProvider;
                var catalog = await services.GetRequiredService<IActivityAuthoringCatalogReader>()
                    .ListAsync(new ListActivityAuthoringCatalog(ActivityCatalogAvailability.All), CancellationToken.None);
                var etlDescriptor = Assert.Single(catalog.Activities, item => item.ActivityTypeKey == RunEtl.ActivityType);
                var pipelineInput = Assert.Single(etlDescriptor.Inputs, input => input.ReferenceKey == ActivityPipelineInput);
                Assert.Equal(nameof(RunEtl.Pipeline), pipelineInput.Name);
                Assert.Equal("etl-published-selection", pipelineInput.UiHint);
                Assert.True(pipelineInput.IsRequired);

                var published = await services.GetRequiredService<IRequestSender>()
                    .Send(new PublishWorkflow(parentVersionId, TenantId: scope.TenantId), CancellationToken.None);
                Assert.Equal(PublicationStatusView.Active, published.Status);
                reference = await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
                    .FindAsync(published.SourceReferenceId)
                    ?? throw new InvalidOperationException("Workflow publication did not persist its source reference.");
                executable = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(published.ArtifactId)
                    ?? throw new InvalidOperationException("Workflow publication did not persist its executable.");
                Assert.Equal(parentVersionId, executable.Identity.DefinitionVersionId);
                Assert.Equal(published.ArtifactHash, executable.Identity.ArtifactHash);
                Assert.Equal(published.SourceReferenceId, reference.SourceReferenceId);
                Assert.Equal(WorkflowExecutableReferenceScope.Published, reference.Scope);
                Assert.Equal(parentVersionId, reference.DefinitionVersionId);
                Assert.NotNull(reference.PublishedAt);
                Assert.NotNull(reference.ActivationId);
                Assert.NotNull(reference.SlotId);
                Assert.Null(reference.DeletedAt);
            }

            await using (var operation = await OpenPartitionScopeAsync(authoring.Services, scope))
            {
                var services = operation.ServiceProvider;
                var definitions = services.GetRequiredService<IEtlDefinitionStore>();
                var currentDraft = await definitions.FindDraftAsync(scope, PipelineDefinitionId)
                    ?? throw new InvalidOperationException("The ETL design store did not return its published draft.");
                var renamedPipeline = EtlTestPipelines.Create("renamed");
                var updatedDraft = await definitions.UpdateDraftAsync(scope, PipelineDefinitionId, currentDraft.Revision, renamedPipeline);
                Assert.Equal(EtlDefinitionWriteStatus.Updated, updatedDraft.Status);
                var compiler = services.GetRequiredService<IEtlArtifactCompiler>();
                var updatedCandidate = Assert.IsType<EtlArtifact>(compiler.Compile(PipelineDefinitionId, 2, renamedPipeline).Artifact);
                var publication = await definitions.PublishAsync(scope, PipelineDefinitionId, updatedDraft.Draft!.Revision, updatedCandidate);
                Assert.Equal(EtlPublicationStatus.Published, publication.Status);
                updatedArtifact = Assert.IsType<EtlArtifact>(publication.Artifact);
                Assert.Equal(1, originalArtifact.DefinitionVersion);
                Assert.Equal(2, updatedArtifact.DefinitionVersion);
                Assert.NotEqual(originalArtifact.ContentHash, updatedArtifact.ContentHash);
            }

            await using (var operation = await OpenPartitionScopeAsync(authoring.Services, scope))
            {
                var version = await operation.ServiceProvider.GetRequiredService<IWorkflowDefinitionVersionStore>()
                    .FindByIdAsync(parentVersionId)
                    ?? throw new InvalidOperationException("The workflow design store did not retain the published parent version.");
                var pinnedToken = ReadPipelineToken(version.State.RootActivity!, flowchart);
                var pinned = EtlPublishedSelection.Parse(pinnedToken);
                Assert.Equal(originalArtifact.DefinitionVersion, pinned.DefinitionVersion);
                Assert.Equal(originalArtifact.ContentHash, pinned.ContentHash);
                Assert.NotEqual(updatedArtifact.ContentHash, pinned.ContentHash);
            }

            await authoring.DisposeAsync();
            authoring = null;
            return new EtlPublishedWorkflowFixture(directory, scope, bindings, originalArtifact, updatedArtifact, executable, reference);
        }
        catch (Exception exception)
        {
            var cleanupFailed = false;
            if (authoring is not null)
            {
                try
                {
                    await authoring.DisposeAsync();
                }
                catch (Exception cleanupException)
                {
                    cleanupFailed = true;
                    exception.Data["EtlPublishedWorkflowFixture.ProviderCleanupFailure"] = cleanupException.ToString();
                }
            }

            if (!cleanupFailed && Directory.Exists(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception cleanupException)
                {
                    cleanupFailed = true;
                    exception.Data["EtlPublishedWorkflowFixture.DirectoryCleanupFailure"] = cleanupException.ToString();
                }
            }

            if (cleanupFailed && Directory.Exists(directory))
                exception.Data["EtlPublishedWorkflowFixture.RetainedEvidence"] = directory;
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        if (Directory.Exists(RootDirectory))
            Directory.Delete(RootDirectory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private static WorkflowExecutionHarness BuildAuthoringProvider(string directory, string workflowDatabase, string etlDesignDatabase) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            // Runtime EF selects the recurring projection store too. Its production indexer must prepare even
            // this workflow's empty schedule projection before the atomic publication activation can serve it.
            .WithFeature(services => new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new PrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ExpressionsFeature().ConfigureServices(services))
            .WithFeature(services => new EventsFeature().ConfigureServices(services))
            .WithFeature(services => new MediatorFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsPublishingFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesDesignApiFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsPublishingApiFeature().ConfigureServices(services))
            .WithFeature(services => new ClrActivityReconciliationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesDesignEntityFrameworkCoreFeature
            {
                ConnectionString = $"Data Source={workflowDatabase};Pooling=False"
            }.ConfigureServices(services))
            .WithFeature(services => new WorkflowsDesignEntityFrameworkCoreFeature
            {
                ConnectionString = $"Data Source={workflowDatabase};Pooling=False"
            }.ConfigureServices(services))
            .WithFeature(services => new WorkflowDesignValidationsFeature().ConfigureServices(services))
            .WithFeature(services => new PublishingEntityFrameworkCoreFeature
            {
                ConnectionString = $"Data Source={workflowDatabase};Pooling=False"
            }.ConfigureServices(services))
            .WithFeature(services => new EtlDesignFeature().ConfigureServices(services))
            .WithFeature(services => new EtlDesignEntityFrameworkCoreFeature
            {
                ConnectionString = $"Data Source={etlDesignDatabase};Pooling=False"
            }.ConfigureServices(services))
            .WithFeature(services => new EtlWorkflowsDesignFeature().ConfigureServices(services))
            .WithFeature(services => new FileSystemLockingFeature
            {
                LocksFolderPath = Path.Combine(directory, "locks")
            }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = $"Data Source={workflowDatabase};Pooling=False",
                    RecoveryContinuationSigningKey = WorkflowRecoverySigningKey,
                    HierarchyCursorSigningKey = WorkflowHierarchySigningKey
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                services.Configure<EfMigrateOptions>(options => options.Policy = EfMigratePolicy.AutoMigrate);
            })
            .WithPersistenceMode(PersistenceMode.Durable)
            .Build(
                WorkflowExecutionHarness.Identity,
                "etl-published-authoring-" + Guid.NewGuid().ToString("N"),
                Array.Empty<string>());

    private static IReadOnlyCollection<string> ActivityTypeKeys(bool flowchart) =>
    [
        RunEtl.ActivityType,
        typeof(WriteLineActivity).FullName!,
        flowchart ? typeof(WorkflowFlowchart).FullName! : typeof(WorkflowSequence).FullName!
    ];

    private static WorkflowDefinitionState CreateWorkflowState(
        IReadOnlyCollection<(ActivityDefinition Definition, ActivityDefinitionVersion Version)> sourceCatalog,
        EtlArtifact artifact,
        bool flowchart)
    {
        var versionsByType = sourceCatalog.ToDictionary(
            entry => entry.Definition.ActivityTypeKey,
            entry => entry.Version.Id,
            StringComparer.Ordinal);
        var selection = new EtlPublishedSelection(artifact.DefinitionId, artifact.DefinitionVersion, artifact.ContentHash).ToToken();
        var runEtl = new ActivityNode(
            "run-etl",
            versionsByType[RunEtl.ActivityType],
            [new ArgumentState(ActivityPipelineInput, new(JsonSerializer.SerializeToElement(selection), "Literal"), null, null, null, null)],
            []);
        var afterEtl = new ActivityNode(
            "after-etl",
            versionsByType[typeof(WriteLineActivity).FullName!],
            [new ArgumentState("text", new(JsonSerializer.SerializeToElement("after-etl"), "Literal"), null, null, null, null)],
            []);

        ActivityNode root;
        if (flowchart)
        {
            var authored = new FlowchartAuthoredStructure(
                [runEtl, afterEtl],
                [new FlowchartConnection(new FlowchartEndpoint(runEtl.NodeId, "Done"), new FlowchartEndpoint(afterEtl.NodeId))],
                runEtl.NodeId);
            root = new ActivityNode("flowchart-root", versionsByType[typeof(WorkflowFlowchart).FullName!], [], [],
                new ActivityNodeStructure(WorkflowFlowchart.StructureKind, WorkflowFlowchart.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(authored, WebJson)));
        }
        else
        {
            var authored = new SequenceAuthoredStructure([runEtl, afterEtl]);
            root = new ActivityNode("sequence-root", versionsByType[typeof(WorkflowSequence).FullName!], [], [],
                new ActivityNodeStructure(WorkflowSequence.StructureKind, WorkflowSequence.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(authored, WebJson)));
        }

        return new WorkflowDefinitionState([], root, [], [], null);
    }

    private static string ReadPipelineToken(ActivityNode root, bool flowchart)
    {
        IEnumerable<ActivityNode> children = flowchart
            ? root.Structure!.Payload.Deserialize<FlowchartAuthoredStructure>(WebJson)!.Activities
            : root.Structure!.Payload.Deserialize<SequenceAuthoredStructure>(WebJson)!.Activities;
        var runEtl = Assert.Single(children, node => node.NodeId == "run-etl");
        var argument = Assert.Single(runEtl.Inputs, input => input.ReferenceKey == ActivityPipelineInput);
        return Assert.IsType<JsonElement>(argument.Value.Value).GetString()!;
    }

    private static DesignOperationKey OperationKey(string action) =>
        new($"etl-published-fixture-{action}-{Guid.NewGuid():N}");

    private static async ValueTask<PersistenceOperationScope> OpenPartitionScopeAsync(IServiceProvider services, EtlScope scope) =>
        await services.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(scope.PersistencePartition));
}
