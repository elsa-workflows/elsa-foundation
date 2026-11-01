using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Activities.Testing;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Editorial;
using Elsa.Agent.Specialists.Editorial.Activities;
using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Agent.Specialists.Tests.Publication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Locking.FileSystem;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Secrets.Features;
using Elsa.Serialization.Core;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Agent.Specialists.Tests.Composition;

/// <summary>
/// One durable native host for the editorial example: SQLite runtime state and an SQLite specialist journal
/// survive <see cref="RestartAsync"/>, which disposes the whole service provider and builds a new one.
/// </summary>
internal sealed class EditorialHost : IAsyncDisposable
{
    internal const string Tenant = "partition-test";
    internal const string GateNodeId = "gate";
    internal const string CheckNodeId = "check";
    internal const string ReviewNodeId = "review";
    private const string RecoveryKey = "editorial-native-test-recovery-key-32-bytes";
    private const string HierarchyKey = "editorial-native-test-hierarchy-key-32-bytes";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "elsa-editorial-" + Guid.NewGuid().ToString("N"));
    private readonly string encryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private WorkflowExecutionHarness? harness;
    private int generation;

    private EditorialHost(string workflowExecutionId, EditorialModel model, TimeSpan decisionLifetime)
    {
        DecisionLifetime = decisionLifetime;
        WorkflowExecutionId = workflowExecutionId;
        Model = model;
        Directory.CreateDirectory(Path.Combine(directory, "locks"));
    }

    public string WorkflowExecutionId { get; }
    public TimeSpan DecisionLifetime { get; }
    public EditorialModel Model { get; }
    public EditorialSubjects Subjects { get; } = new();
    public WorkflowExecutionHarness Harness => harness ?? throw new InvalidOperationException("The editorial host is stopped.");

    public EditorialCrashSwitch Crash { get; } = new();

    public static async Task<EditorialHost> StartAsync(string workflowExecutionId, EditorialModel model, TimeSpan? decisionLifetime = null)
    {
        var host = new EditorialHost(workflowExecutionId, model, decisionLifetime ?? TimeSpan.FromMinutes(15));
        await host.BuildAsync();
        return host;
    }

    public async Task RestartAsync()
    {
        await Harness.DisposeAsync();
        harness = null;
        await BuildAsync();
    }

    private async Task BuildAsync()
    {
        var runtime = $"Data Source={Path.Combine(directory, "runtime.db")};Pooling=False";
        var journal = $"Data Source={Path.Combine(directory, "journal.db")};Pooling=False";
        var ids = Enumerable.Range(1, 96).Select(index => $"{WorkflowExecutionId}-g{generation}-activity-{index}").ToArray();
        generation++;
        harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new BackgroundActivitiesFeature { RunWorkers = false, LeaseDurationSeconds = 60 }.ConfigureServices(services))
            .WithFeature(services => new SecretsFeature { EncryptionKey = encryptionKey }.ConfigureServices(services))
            .WithFeature(services => new SpecialistsRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new SpecialistsEditorialFeature { DecisionLifetime = DecisionLifetime }.ConfigureServices(services))
            .WithFeature(services => new FileSystemLockingFeature { LocksFolderPath = Path.Combine(directory, "locks") }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<Elsa.Persistence.Schema.SchemaFinalization.IEfSchemaFleet, SingleNodeFleet>();
                Elsa.Cluster.Readability.EfSchemaReadabilityServiceCollectionExtensions.AddEfSchemaDormancy(services);
                services.AddSingleton<ISpecialistModelClientFactory>(Model);
                services.AddScoped<ICurrentSubjectPermissionAuthorizer>(_ => Subjects);
                services.AddSingleton(Crash);
                services.AddScoped<IRuntimeCheckpointCommitEnricher, EditorialCrashEnricher>();
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = runtime,
                    CacheWorkflowExecutables = false,
                    RecoveryContinuationSigningKey = RecoveryKey,
                    HierarchyCursorSigningKey = HierarchyKey
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                services.Configure<Elsa.Persistence.EntityFramework.SchemaFinalization.EfSchemaFinalizationOptions>(options =>
                {
                    options.EvaluationInterval = TimeSpan.FromMilliseconds(50);
                    options.RefreshInterval = TimeSpan.FromMilliseconds(50);
                });
                Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection.SpecialistsEntityFrameworkCoreRegistration.AddSpecialistsEntityFrameworkCore(services,
                    new() { Provider = "Sqlite", ConnectionString = journal });
            })
            .WithPersistenceMode(PersistenceMode.Durable)
            .Build(EditorialExecutable.Identity, WorkflowExecutionId, ids);
        foreach (var initializer in Harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        // A normal host observes the finalized physical-source outbox schema before any durable settlement.
        var dormancy = Harness.Services.GetRequiredService<Elsa.Cluster.Core.Contracts.ISchemaDormancyCheck>();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await dormancy.EnsureAvailableAsync([new Elsa.Cluster.Core.Models.SchemaVersionRequirement("RuntimePostCommitOutbox", "2.0.0")], "EditorialCompositionTests");
                break;
            }
            catch (Elsa.Primitives.Exceptions.SchemaDormancyRefusedException) when (attempt < 200) { await Task.Delay(50); }
        }
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(
            Harness.Services.GetRequiredService<Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.SpecialistsDbContext>().Database);
        Harness.InitializeActivityTypes();
        var types = Harness.Services.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), "String");
        types.RegisterType(typeof(JsonElement), "System.Text.Json.JsonElement");
        await new Elsa.Agent.Specialists.SpecialistTypesStartupTask(types).ExecuteAsync(CancellationToken.None);
        await new Elsa.Agent.Specialists.Editorial.EditorialTypesStartupTask(types).ExecuteAsync(CancellationToken.None);
    }

    public async ValueTask<PersistenceOperationScope> ScopeAsync() =>
        await Harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new PersistenceScope(Tenant));

    public static readonly ConcurrentQueue<string> FirstChance = new();

    public async Task StartRunAsync(WorkflowExecutable executable)
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, args) =>
        {
            if (FirstChance.Count < 12)
                FirstChance.Enqueue(args.Exception.GetType().Name + ": " + args.Exception.Message);
        };
        var identity = executable.Identity;
        var reference = new WorkflowExecutableSourceReference(
            "editorial-source", identity.ArtifactId, "WorkflowDefinitionVersion", identity.DefinitionVersionId,
            identity.ArtifactVersion, identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion,
            executable.CreatedAt, executable.CreatedAt, WorkflowExecutableReferenceScope.Published,
            ActivationId: "publication-editorial", SlotId: "slot-editorial", TenantId: Tenant);
        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
        }
        await using var start = await ScopeAsync();
        var authority = new WorkflowExecutionAuthoritySnapshot(systemIdentity: "editorial-test", rootInitiator: "editorial-test");
        var started = await start.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: reference.ArtifactId, requestedBy: authority.SystemIdentity,
                workflowExecutionId: WorkflowExecutionId, idempotencyKey: $"start:{WorkflowExecutionId}",
                metadata: null, variables: null, inputs: null, stimulusInput: null, triggerNodeId: null,
                runKind: WorkflowRunKind.PublishedRun,
                sourceSelection: new WorkflowExecutableSourceSelection(reference.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null, correlationId: null,
                tenantId: Tenant, partition: new WorkflowExecutionPartition(Tenant), authority: authority));
        Assert.True(started.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted, started.CommandDispatch.Reason + await DiagnoseAsync());
    }

    public async Task<string> DiagnoseAsync()
    {
        await using var scope = await ScopeAsync();
        var states = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowExecutionId);
        var incidents = await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionId);
        return JsonSerializer.Serialize(new
        {
            States = states.Select(x => new { x.Execution.ExecutableNodeId, x.Status, x.SubStatus, Fault = x.Fault?.ToString(), Outcome = x.Completion?.OutcomeKey }),
            Incidents = incidents, FirstChance = FirstChance.ToArray()
        });
    }

    /// <summary>Runs background model turns and native intents until nothing more can progress.</summary>
    public async Task PumpAsync(int maximumSteps = 64)
    {
        for (var step = 0; step < maximumSteps; step++)
        {
            var progressed = false;
            await using (var scope = await ScopeAsync())
            {
                var claim = await scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>()
                    .ClaimNextAsync("editorial-test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
                if (claim is not null)
                {
                    await Harness.Services.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new PersistenceScope(Tenant), claim);
                    progressed = true;
                }
            }
            await using (var scope = await ScopeAsync())
            {
                var sweep = await scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest());
                progressed |= sweep.OutboxAttemptedCount > 0;
            }
            await using (var scope = await ScopeAsync())
            {
                var pending = await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId));
                if (pending.Items.Count > 0)
                {
                    var actor = await Harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(WorkflowExecutionId,
                        WorkflowExecutionActorActivationReason.SchedulerWork, DateTimeOffset.UtcNow, "editorial-test", WorkflowExecutionActorCapabilities.InProcessMailbox));
                    var now = DateTimeOffset.UtcNow;
                    var id = $"drain-{generation}-{step}-{Guid.NewGuid():N}";
                    var command = new WorkflowExecutionCommand(id, WorkflowExecutionId, WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>());
                    await actor.EnqueueAsync(new(id, WorkflowExecutionId, command, id, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
                    progressed = true;
                }
            }
            if (!progressed) return;
        }
        throw new InvalidOperationException("The editorial run did not settle.");
    }

    public Task CancelAsync() => Harness.CancelAsync(WorkflowExecutionId, new WorkflowExecutionPartition(Tenant));

    public async Task<IReadOnlyCollection<IncidentState>> IncidentsAsync()
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionId);
    }

    public async Task<IReadOnlyCollection<ActivityExecutionState>> ActivitiesAsync()
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowExecutionId);
    }

    public async Task<WorkflowExecutionState> WorkflowAsync()
    {
        await using var scope = await ScopeAsync();
        return (await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowExecutionId))!;
    }

    public async Task<ActivityExecutionState> PendingReviewAsync() =>
        Assert.Single(await ActivitiesAsync(), activity => activity.Execution.ExecutableNodeId == ReviewNodeId && activity.Completion is null);

    public async Task<EditorialDecisionReceipt> DecideAsync(EditorialDecisionCommand command, string actor, string tenant = Tenant)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IEditorialReviews>().DecideAsync(command, new(tenant, actor));
    }

    public async Task<EditorialReviewView?> ReadReviewAsync(string activityExecutionId, string tenant = Tenant)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IEditorialReviews>().GetAsync(new(WorkflowExecutionId, activityExecutionId), new(tenant, "reader"));
    }

    public async ValueTask DisposeAsync()
    {
        if (harness is not null) await harness.DisposeAsync();
        try { Directory.Delete(directory, true); } catch (IOException) { }
    }
}

/// <summary>Builds the native Sequence → Flowchart editorial example from two really published specialists.</summary>
internal static class EditorialExecutable
{
    public static readonly WorkflowExecutableIdentity Identity = new("editorial-artifact", "editorial", "editorial-v1", "1.0.0", "sha256:editorial");
    private const string Scope = "workflow";
    private static readonly ValueTypeDescriptor Text = new("String");

    public static readonly SpecialistDefinitionSpec Copywriter = new("copywriter", "acme.copywriter", "Copywriter", "COPYWRITER: write the brief as an article.",
        [
            new("brief-key", "Brief", new("String"), true, false, null, "elsa.json"),
            new("previous-key", "PreviousArticle", new("String"), false, true, null, "elsa.json"),
            new("feedback-key", "ReviewComments", new("String"), false, true, null, "elsa.json")
        ],
        [new("article-key", "Article", new("String"), true, false, "elsa.json")]);

    public static readonly SpecialistDefinitionSpec Proofreader = new("proofreader", "acme.proofreader", "Proofreader", "PROOFREADER: return approved or changes_requested with comments.",
        [new("article-key", "Article", new("String"), true, false, null, "elsa.json")],
        [
            new("verdict-key", "Verdict", new("String"), true, false, "elsa.json"),
            new("comments-key", "Comments", new("String"), false, true, "elsa.json")
        ]);

    public static async Task<(WorkflowExecutable Executable, string CopywriterVersion, string ProofreaderVersion)> BuildAsync(DateTimeOffset deadline, int maxRounds = 3)
    {
        var copywriter = await SpecialistPublicationTests.PublishAndPlaceSpecialistAsync(Copywriter, CopywriterNodeOrigin,
            new Dictionary<string, RuntimeInputBinding>
            {
                ["brief-key"] = new("brief-key", Text, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(Text, JsonSerializer.SerializeToElement("moss gardens"), ValueProtectionPolicy.InstanceInline)),
                ["previous-key"] = Read("previous-key", Text, "previous-article"),
                ["feedback-key"] = Read("feedback-key", Text, "feedback")
            },
            new Dictionary<string, RuntimeOutputCapture> { ["article-key"] = Capture("article-key", "String", "article", "Article") });
        var proofreader = await SpecialistPublicationTests.PublishAndPlaceSpecialistAsync(Proofreader, ProofreaderNodeOrigin,
            new Dictionary<string, RuntimeInputBinding> { ["article-key"] = Read("article-key", Text, "article") },
            new Dictionary<string, RuntimeOutputCapture>
            {
                ["verdict-key"] = Capture("verdict-key", "String", "verdict", "Verdict"),
                ["comments-key"] = Capture("comments-key", "String", "review-comments", "ReviewComments")
            });
        var gate = Gate(EditorialHost.GateNodeId, deadline, maxRounds);
        var check = Gate(EditorialHost.CheckNodeId, deadline, maxRounds);
        var reviewContract = ClrActivityContractTestBuilder.BuildContract(typeof(ReviewEditorialContent));
        var review = Clr<ReviewEditorialContent>(EditorialHost.ReviewNodeId, reviewContract, new Dictionary<string, RuntimeInputBinding>
        {
            ["Article"] = Read("Article", reviewContract.Inputs["Article"].Type, "article"),
            ["Round"] = Read("Round", reviewContract.Inputs["Round"].Type, "round"),
            ["ReviewComments"] = Read("ReviewComments", reviewContract.Inputs["ReviewComments"].Type, "review-comments")
        });
        // Placement owns the exact specialist node identities; the example routes by those identities.
        var writer = copywriter.Placement.Root;
        var reader = proofreader.Placement.Root;
        var flowchartContract = ClrActivityContractTestBuilder.BuildContract(typeof(FlowchartActivity));
        var flowchart = new ExecutableNode("editorial-flowchart", "authored-editorial-flowchart", typeof(FlowchartActivity).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, flowchartContract.DescriptorPayload, new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [gate, writer, check, reader, review])],
            structure: new ExecutableActivityStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(
                [
                    Connect(EditorialHost.GateNodeId, writer.ExecutableNodeId, "Write"),
                    Connect(writer.ExecutableNodeId, EditorialHost.CheckNodeId),
                    Connect(EditorialHost.CheckNodeId, reader.ExecutableNodeId, "Review"),
                    Connect(reader.ExecutableNodeId, EditorialHost.GateNodeId),
                    Connect(EditorialHost.GateNodeId, EditorialHost.ReviewNodeId, "Approved")
                ], EditorialHost.GateNodeId))),
            activityContract: flowchartContract);
        var sequenceContract = ClrActivityContractTestBuilder.BuildContract(typeof(SequenceActivity));
        var sequence = new ExecutableNode("editorial-sequence", "authored-editorial-sequence", typeof(SequenceActivity).FullName!, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, sequenceContract.DescriptorPayload, new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [flowchart])],
            structure: new ExecutableActivityStructure(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceExecutableStructure([flowchart.ExecutableNodeId]))),
            activityContract: sequenceContract);
        var target = new WorkflowExecutableResumeTarget(WorkflowExecutableResumeTarget.ComposeScopedId(EditorialHost.ReviewNodeId, EditorialReviewIds.ResumeTargetKey),
            EditorialHost.ReviewNodeId, "ResumeAsync", new Dictionary<string, string>(StringComparer.Ordinal), EditorialReviewIds.ResumeTargetKey);
        var resumeTargets = copywriter.Placement.ResumeTargets.Concat(proofreader.Placement.ResumeTargets)
            .Append(new KeyValuePair<string, WorkflowExecutableResumeTarget>(target.ResumeTargetId, target))
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var round = gate.ActivityContract!.Inputs["Round"].Type;
        var executable = new WorkflowExecutable(Identity, sequence, resumeTargets, WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(),
            inputContract: null, dependencies: null,
            copywriter.Placement.RuntimeRequirements.Concat(proofreader.Placement.RuntimeRequirements).Distinct().ToArray(),
            copywriter.Storage.Concat(proofreader.Storage).Distinct().ToArray(), IncidentStrategyBuiltIns.FaultReference,
            workflowVariables:
            [
                Variable("round", "Round", round, JsonSerializer.SerializeToElement(0)),
                Variable("awaiting", "Awaiting", Text, JsonSerializer.SerializeToElement(string.Empty)),
                Variable("article", "Article", Text), Variable("previous-article", "PreviousArticle", Text), Variable("feedback", "Feedback", Text),
                Variable("verdict", "Verdict", Text), Variable("review-comments", "ReviewComments", Text)
            ]);
        return (executable, copywriter.DefinitionVersionId, proofreader.DefinitionVersionId);
    }

    private const string CopywriterNodeOrigin = "editorial-copywriter";
    private const string ProofreaderNodeOrigin = "editorial-proofreader";

    private static ExecutableNode Gate(string nodeId, DateTimeOffset deadline, int maxRounds)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(EditorialRevisionGate));
        var inputs = contract.Inputs;
        var bindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["Awaiting"] = Read("Awaiting", inputs["Awaiting"].Type, "awaiting"),
            ["Round"] = Read("Round", inputs["Round"].Type, "round"),
            ["Article"] = Read("Article", inputs["Article"].Type, "article"),
            ["Verdict"] = Read("Verdict", inputs["Verdict"].Type, "verdict"),
            ["ReviewComments"] = Read("ReviewComments", inputs["ReviewComments"].Type, "review-comments"),
            ["MaxRounds"] = Literal("MaxRounds", inputs["MaxRounds"].Type, maxRounds),
            ["Deadline"] = Literal("Deadline", inputs["Deadline"].Type, deadline)
        };
        var projections = contract.Result.Projections;
        var captures = new Dictionary<string, RuntimeOutputCapture>
        {
            ["Round"] = Capture("Round", projections["Round"].Type.Alias, "round", "Round"),
            ["Awaiting"] = Capture("Awaiting", projections["Awaiting"].Type.Alias, "awaiting", "Awaiting"),
            ["PreviousArticle"] = Capture("PreviousArticle", projections["PreviousArticle"].Type.Alias, "previous-article", "PreviousArticle"),
            ["Feedback"] = Capture("Feedback", projections["Feedback"].Type.Alias, "feedback", "Feedback")
        };
        return Clr<EditorialRevisionGate>(nodeId, contract, bindings, captures);
    }

    private static ExecutableNode Clr<TActivity>(string nodeId, ActivityContract contract, IReadOnlyDictionary<string, RuntimeInputBinding> bindings,
        IReadOnlyDictionary<string, RuntimeOutputCapture>? captures = null) where TActivity : IActivity =>
        new(nodeId, $"authored-{nodeId}", typeof(TActivity).FullName!, "1.0.0", WellKnownRuntimeActivityConsumers.ClrActivity, contract.DescriptorPayload,
            ClrActivityContractTestBuilder.CompleteInputBindings(contract, bindings), new Dictionary<string, string>(),
            outputCaptures: captures ?? new Dictionary<string, RuntimeOutputCapture>(), activityContract: contract);

    private static RuntimeInputBinding Read(string key, ValueTypeDescriptor type, string variable) =>
        new(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead, variable: new(variable, Scope));

    private static RuntimeInputBinding Literal<T>(string key, ValueTypeDescriptor type, T value) =>
        new(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));

    private static RuntimeOutputCapture Capture(string outputKey, string alias, string variable, string name) =>
        new(outputKey, $"{RuntimeWorkflowStateSeed.VariableValueIdPrefix}{name}", new(alias, WellKnownRuntimeDurableValueStorageDrivers.Json, null),
            DurableValueLifecycle.Instance, DurableValueStorage.Custom, true,
            new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.TargetVariableReferenceKey] = variable,
                [RuntimeMetadataKeys.VariableName] = name,
                [RuntimeMetadataKeys.StorageDriverKey] = WellKnownRuntimeDurableValueStorageDrivers.Json
            });

    private static RuntimeVariableDeclaration Variable(string key, string name, ValueTypeDescriptor type, JsonElement? initial = null) =>
        new(key, name, type, ValueProtectionPolicy.InstanceInline, initial is { } value
            ? new RuntimeInputBinding(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, value, ValueProtectionPolicy.InstanceInline))
            : null);

    private static FlowchartConnection Connect(string source, string target, string? port = null) =>
        new(new FlowchartEndpoint(source, port), new FlowchartEndpoint(target));
}

/// <summary>A scripted deterministic model. Each specialist's responses are consumed in order.</summary>
internal sealed class EditorialModel(IReadOnlyList<string> copywriter, IReadOnlyList<string> proofreader) : ISpecialistModelClientFactory
{
    private int writes;
    private int reviews;
    public string ProviderKey => "fixture";
    public ConcurrentQueue<(string Role, string User)> Calls { get; } = new();

    public ValueTask<SpecialistModelClientLease> OpenAsync(SpecialistModelOpenRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new SpecialistModelClientLease(new Client(this, request), new ChatOptions { MaxOutputTokens = 4096 }, "fixture/1", 1)
            { EnforcesDispatchAuthority = request.DispatchAuthority is not null });

    private string Next(string? instructions)
    {
        if (instructions?.Contains("COPYWRITER", StringComparison.Ordinal) == true)
            return copywriter[Math.Min(Interlocked.Increment(ref writes) - 1, copywriter.Count - 1)];
        if (instructions?.Contains("PROOFREADER", StringComparison.Ordinal) == true)
            return proofreader[Math.Min(Interlocked.Increment(ref reviews) - 1, proofreader.Count - 1)];
        throw new InvalidOperationException("The fixture model received an unknown specialist.");
    }

    private sealed class Client(EditorialModel owner, SpecialistModelOpenRequest request) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (request.DispatchAuthority is { } authority)
                await authority.BeforeSendAsync(new("fixture", request.Descriptor.Model.ModelId, "fixture-incarnation", 1, "fixture/1", "external-deterministic-one-response/1"), cancellationToken);
            var response = owner.Next(options?.Instructions);
            owner.Calls.Enqueue((options?.Instructions?.Contains("COPYWRITER", StringComparison.Ordinal) == true ? "copywriter" : "proofreader",
                messages.Last(x => x.Role == ChatRole.User).Text));
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, response))
                { ModelId = "fixture-model", Usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 3 } };
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new(ChatRole.Assistant, response.Text);
            yield return new ChatResponseUpdate { FinishReason = ChatFinishReason.Stop, ModelId = response.ModelId };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

/// <summary>Current-subject authority whose grants a test can revoke between steps.</summary>
internal sealed class EditorialSubjects : ICurrentSubjectPermissionAuthorizer
{
    private readonly ConcurrentDictionary<(string Tenant, string Subject), bool> grants = new();
    public void Grant(string subject, string tenant = EditorialHost.Tenant) => grants[(tenant, subject)] = true;
    public void Revoke(string subject, string tenant = EditorialHost.Tenant) => grants.TryRemove((tenant, subject), out _);
    public bool Unavailable { get; set; }
    public int Checks => checks;
    private int checks;
    private (string Tenant, string Subject)? revokeAfterNextCheck;

    /// <summary>Grants the next check for this subject, then revokes it: models revocation between issue and admission.</summary>
    public void RevokeAfterNextCheck(string subject, string tenant = EditorialHost.Tenant) => revokeAfterNextCheck = (tenant, subject);

    public ValueTask<CurrentSubjectPermissionResult> AuthorizeAsync(CurrentSubjectPermissionRequest request, CancellationToken cancellationToken = default)
    {
        if (Unavailable) throw new TimeoutException("The identity store is temporarily unavailable.");
        Interlocked.Increment(ref checks);
        if (revokeAfterNextCheck == (request.TenantId, request.SubjectId) && grants.ContainsKey((request.TenantId, request.SubjectId)))
        {
            revokeAfterNextCheck = null;
            grants.TryRemove((request.TenantId, request.SubjectId), out _);
            return ValueTask.FromResult(CurrentSubjectPermissionResult.Granted);
        }
        return ValueTask.FromResult(request.Permission == EditorialReviewIds.DecidePermission && grants.ContainsKey((request.TenantId, request.SubjectId))
            ? CurrentSubjectPermissionResult.Granted
            : CurrentSubjectPermissionResult.Denied("not granted"));
    }
}

/// <summary>A single current host: finalization needs no other member's acknowledgement.</summary>
internal sealed class SingleNodeFleet : Elsa.Persistence.Schema.SchemaFinalization.IEfSchemaFleet
{
    public Elsa.Persistence.Schema.SchemaFinalization.EfSchemaFleetStanding GetLocalStanding() => new(new("editorial-host", "incarnation"), false);
    public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask<Elsa.Persistence.Schema.SchemaFinalization.EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new Elsa.Persistence.Schema.SchemaFinalization.EfSchemaFleetAnswer(true, []));
    public ValueTask<Elsa.Persistence.Schema.SchemaFinalization.EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new Elsa.Persistence.Schema.SchemaFinalization.EfSchemaFleetAnswer(true, []));
    public TimeSpan SettleMargin => TimeSpan.Zero;
    public Microsoft.Extensions.Primitives.IChangeToken GetChangeToken() => new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
}

/// <summary>Armed once, it loses the process before the review's completion checkpoint commits.</summary>
internal sealed class EditorialCrashSwitch
{
    private int armed;
    public void Arm() => Interlocked.Exchange(ref armed, 1);
    public bool Fired { get; private set; }
    public bool TryFire()
    {
        if (Interlocked.Exchange(ref armed, 0) != 1) return false;
        Fired = true;
        return true;
    }
}

internal sealed class EditorialCrashEnricher(EditorialCrashSwitch crash) : IRuntimeCheckpointCommitEnricher
{
    public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
    {
        // A stale fence is how the drainer sees a lost owner: nothing is saved, acknowledged or poisoned.
        if (commit.StateChanges.ActivityExecutions.Any(change => change.State.Execution.ExecutableNodeId == EditorialHost.ReviewNodeId &&
                change.State.Status == ActivityExecutionStatus.Completed) && crash.TryFire())
            throw new Elsa.Workflows.Runtime.Core.Exceptions.RuntimeStaleFencingTokenException(commit.WorkflowExecutionId, 1, 2);
        return ValueTask.FromResult(commit);
    }
}
