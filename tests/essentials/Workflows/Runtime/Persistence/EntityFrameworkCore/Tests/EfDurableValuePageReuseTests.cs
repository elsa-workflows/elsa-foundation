using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CShells;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime;
using Elsa.Activities.Testing;
using Elsa.Activities.Primitives.Activation;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Activities;
using Elsa.Api.Capabilities;
using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.Core.Models;
using Elsa.Locking.Core;
using Elsa.Mediator;
using Elsa.Modularity.EntityFramework.Extensions;
using Elsa.Primitives.Hosting;
using Elsa.Primitives.Models;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfDurableValuePageReuseTests
{
    private readonly ITestOutputHelper _output;

    private const string ShellName = "durable-page-reuse";
    private const string ResourceName = "DurablePageReuse";
    private const string RecoverySigningKey = "durable-page-reuse-recovery-signing-key-32-bytes";
    private const string HierarchySigningKey = "durable-page-reuse-hierarchy-signing-key-32-bytes";
    private const string Payload = "the persisted workflow input";
    private const string Greeting = "visible workflow variable";

    public EfDurableValuePageReuseTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task Coalesced_external_sequence_reuses_nonempty_ef_pages_without_changing_workflow_values()
    {
        var uncached = await RunScenarioAsync(coalesceDurableValueReads: false);
        var cached = await RunScenarioAsync(coalesceDurableValueReads: true);

        _output.WriteLine($"Uncached durable-value page reads: {uncached.PageReadCount}; nonempty: {uncached.NonemptyPageReadCount}.");
        _output.WriteLine($"Enabled durable-value page reads: {cached.PageReadCount}; nonempty: {cached.NonemptyPageReadCount}.");
        Assert.True(uncached.PageReadCount >= 4, $"Expected the uncached start/invoke page reads, observed {uncached.PageReadCount}.");
        Assert.True(uncached.NonemptyPageReadCount >= 2, $"Expected nonempty backing pages after the first external claim, observed {uncached.NonemptyPageReadCount} of {uncached.PageReadCount} page reads.");
        Assert.True(cached.NonemptyPageReadCount > 0, "The enabled run must still read a nonempty backing page.");
        Assert.True(
            cached.PageReadCount < uncached.PageReadCount,
            $"Expected the eligible EF memo to reduce backing page requests; uncached={uncached.PageReadCount}, enabled={cached.PageReadCount}.");
        Assert.Equal(uncached.SemanticSnapshot, cached.SemanticSnapshot);
        Assert.Equal(typeof(ArgumentException).FullName, uncached.InvalidIdentityFailureType);
        Assert.Equal(uncached.InvalidIdentityFailureType, cached.InvalidIdentityFailureType);
        Assert.Equal(typeof(ArgumentException).FullName, uncached.InvalidCursorFailureType);
        Assert.Equal(uncached.InvalidCursorFailureType, cached.InvalidCursorFailureType);
        Assert.Equal(typeof(ArgumentException).FullName, uncached.InvalidInnerCursorFailureType);
        Assert.Equal(uncached.InvalidInnerCursorFailureType, cached.InvalidInnerCursorFailureType);
    }

    private static async Task<ScenarioCapture> RunScenarioAsync(bool coalesceDurableValueReads)
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-durable-page-reuse-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        var pageReads = new DurableValuePageReadProbe();

        try
        {
            await using var host = CreateHost(connectionString, pageReads, coalesceDurableValueReads);
            var shell = await host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            var enabledFeatures = shell.ServiceProvider.GetRequiredService<ShellSettings>().EnabledFeatures;
            Assert.Contains("ActivitiesPrimitives", enabledFeatures);
            Assert.Contains("ActivitiesRuntime", enabledFeatures);
            await using var scope = shell.ServiceProvider.CreateAsyncScope();
            Assert.True(scope.ServiceProvider.GetRequiredService<RuntimeCoalescingDurableValuePageReuseRegistration>().IsEligible);
            Assert.Contains(
                scope.ServiceProvider.GetServices<IActivityActivationStrategy>(),
                strategy => strategy is ClrActivityActivator);

            var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
            Assert.Equal(databasePath, context.Database.GetDbConnection().DataSource);
            Assert.NotEmpty(await context.Database.GetAppliedMigrationsAsync());
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            var executable = NewExecutable();
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
            await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(
                new WorkflowExecutableSourceReference(
                    "durable-page-reuse-published-reference",
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

            pageReads.Reset();
            var started = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStartService>()
                .ExecuteAsync(
                    new ExecuteWorkflow(
                        executable.Identity.ArtifactId,
                        new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                        {
                            ["payload"] = JsonSerializer.SerializeToElement(Payload)
                        }),
                    CancellationToken.None);
            var observedPages = pageReads.Snapshot();

            Assert.Equal("Accepted", started.CommandDispatchStatus);
            var executionStore = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
            var workflow = await executionStore.FindAsync(started.WorkflowExecutionId);
            Assert.NotNull(workflow);
            var schedulerState = await scope.ServiceProvider.GetRequiredService<ISchedulerStateStore>()
                .FindAsync(started.WorkflowExecutionId);
            var activityStates = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
                .ListAllAsync(started.WorkflowExecutionId);
            var incidents = await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>()
                .ListAsync(started.WorkflowExecutionId);
            Assert.True(
                workflow.Status == WorkflowExecutionStatus.Completed,
                $"Expected Completed; found {workflow.Status}. Scheduler pending=[{string.Join(",", schedulerState?.PendingWork.Select(item => $"{item.ExecutableNodeId}:{item.Reason}") ?? [])}], " +
                $"continuations={schedulerState?.PendingContinuations.Count}; activities=[{string.Join(",", activityStates.Select(state => $"{state.Execution.ExecutableNodeId}:{state.Status}/{state.SubStatus}"))}]; " +
                $"incidents=[{string.Join(";", incidents.Select(incident => $"{incident.FailureType}:{incident.Message}"))}].");
            Assert.NotNull(workflow.CompletedAt);
            Assert.Equal(
                WorkflowExecutableCheckpointCadence.CoalescedMode,
                workflow.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence]);

            Assert.Equal(3, activityStates.Count);
            Assert.All(activityStates, state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));

            var first = Assert.Single(activityStates, state => state.Execution.ExecutableNodeId == "external-first");
            var second = Assert.Single(activityStates, state => state.Execution.ExecutableNodeId == "external-second");
            Assert.Empty(incidents);
            foreach (var activity in new[] { first, second })
            {
                Assert.Equal(started.WorkflowExecutionId, activity.Execution.WorkflowExecutionId);
                Assert.Equal(typeof(DurableValuePageReuseActivity).FullName, activity.Execution.ActivityType);
                Assert.Equal(activity.InvocationId, activity.InputSnapshot!.InvocationId);
                Assert.Equal(typeof(DurableValuePageReuseActivity).FullName, activity.ContractIdentity!.ActivityTypeKey);
                Assert.Equal(JsonSerializer.Serialize(Payload), activity.InputSnapshot.Values["Payload"].InlineValue!.Value.GetRawText());
                Assert.Equal(JsonSerializer.Serialize(Greeting), activity.InputSnapshot.Values["VisibleGreeting"].InlineValue!.Value.GetRawText());
                Assert.Equal($"{Greeting}:{Payload}", activity.Completion!.Result.InlineValue!.Value.GetString());
            }

            var semanticSnapshot = JsonSerializer.Serialize(new
            {
                Status = workflow.Status.ToString(),
                Cadence = workflow.SystemMetadata[RuntimeMetadataKeys.CheckpointCadence],
                Activities = new[] { first, second }.Select(activity => new
                {
                    activity.Execution.ExecutableNodeId,
                    activity.Execution.ActivityType,
                    WorkflowIdentityMatches = activity.Execution.WorkflowExecutionId == started.WorkflowExecutionId,
                    InvocationIdentityMatches = activity.InputSnapshot!.InvocationId == activity.InvocationId,
                    ContractActivityType = activity.ContractIdentity!.ActivityTypeKey,
                    Inputs = activity.InputSnapshot.Values.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
                    {
                        pair.Key,
                        Value = pair.Value.InlineValue!.Value.GetRawText()
                    }),
                    Result = activity.Completion!.Result.InlineValue!.Value.GetRawText()
                })
            });

            var durableValueStore = scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>();
            var sessionAccessor = scope.ServiceProvider.GetRequiredService<IRuntimeCoalescingSessionAccessor>();
            var coalescingOptions = scope.ServiceProvider.GetRequiredService<CoalescingRuntimeCheckpointPersistenceOptions>();
            Assert.Equal(coalesceDurableValueReads, coalescingOptions.CoalesceDurableValueReads);
            var invalidExecutionId = new string('x', RuntimeOperationalStateEfModule.IdentityMaximumLength + 1);
            var invalidIdentitySession = new RuntimeCoalescingSession(
                invalidExecutionId,
                new InMemoryWorkflowSchedulerWorkQueue(),
                coalescingOptions);
            string invalidIdentityFailureType;
            using (sessionAccessor.Push(invalidIdentitySession))
            {
                invalidIdentityFailureType = await CaptureFailureTypeAsync(
                    async () => await durableValueStore.ListPageAsync(new DurableValueStatePageQuery(invalidExecutionId, limit: 10)));
            }

            // The completed workflow persists one value. Add a second only for the cursor rejection control;
            // the measured workflow page counts and semantic snapshot were captured before this setup.
            var persistedValue = Assert.Single((await durableValueStore.ListPageAsync(
                new DurableValueStatePageQuery(started.WorkflowExecutionId, limit: 10))).Items);
            await durableValueStore.SaveAsync(new DurableValueState(
                "cursor-rejection-probe",
                started.WorkflowExecutionId,
                "cursor-rejection-probe",
                persistedValue.Type,
                persistedValue.Lifecycle,
                persistedValue.Storage,
                persistedValue.InlineValue,
                persistedValue.ExternalReference,
                persistedValue.SourceActivityExecutionId,
                persistedValue.CapturedAt,
                persistedValue.Metadata));
            var cursorSession = new RuntimeCoalescingSession(
                started.WorkflowExecutionId,
                new InMemoryWorkflowSchedulerWorkQueue(),
                coalescingOptions);
            string invalidCursorFailureType;
            string invalidInnerCursorFailureType;
            using (sessionAccessor.Push(cursorSession))
            {
                // Warm the valid first-page key before asking the public coalesced store to reject a malformed token.
                await durableValueStore.ListPageAsync(new DurableValueStatePageQuery(started.WorkflowExecutionId, limit: 10));
                await durableValueStore.ListPageAsync(new DurableValueStatePageQuery(started.WorkflowExecutionId, limit: 10));
                invalidCursorFailureType = await CaptureFailureTypeAsync(
                    async () => await durableValueStore.ListPageAsync(new DurableValueStatePageQuery(
                        started.WorkflowExecutionId,
                        limit: 10,
                        continuationToken: "not-a-valid-coalescing-token")));

                // Use a real provider-produced cursor, warm its valid key, then corrupt only its inner signature.
                // Recompute the public envelope checksum so rejection must reach the EF/HMAC boundary.
                var firstPage = await durableValueStore.ListPageAsync(
                    new DurableValueStatePageQuery(started.WorkflowExecutionId, limit: 1));
                Assert.NotNull(firstPage.NextContinuationToken);
                await durableValueStore.ListPageAsync(new DurableValueStatePageQuery(
                    started.WorkflowExecutionId, limit: 1, continuationToken: firstPage.NextContinuationToken));
                var invalidInnerCursor = CorruptInnerSignature(firstPage.NextContinuationToken);
                async Task ReadInvalidInnerCursorAsync() => await durableValueStore.ListPageAsync(
                    new DurableValueStatePageQuery(started.WorkflowExecutionId, limit: 1, continuationToken: invalidInnerCursor));
                invalidInnerCursorFailureType = await CaptureFailureTypeAsync(ReadInvalidInnerCursorAsync);
                Assert.Equal(invalidInnerCursorFailureType, await CaptureFailureTypeAsync(ReadInvalidInnerCursorAsync));
            }

            return new ScenarioCapture(
                observedPages.Count,
                observedPages.Count(read => read),
                semanticSnapshot,
                invalidIdentityFailureType,
                invalidCursorFailureType,
                invalidInnerCursorFailureType);
        }
        finally
        {
            foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
                File.Delete(path);
        }
    }

    private static ServiceProvider CreateHost(
        string connectionString,
        DurableValuePageReadProbe pageReads,
        bool coalesceDurableValueReads)
    {
        var values = new Dictionary<string, string?>
        {
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["Elsa:Persistence:Resources:primary:Provider"] = "Sqlite",
            [$"Elsa:Persistence:Resources:primary:ConnectionName"] = ResourceName,
            [$"ConnectionStrings:{ResourceName}"] = connectionString,
            [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeEntityFrameworkCore:RecoveryContinuationSigningKey"] = RecoverySigningKey,
            [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeEntityFrameworkCore:HierarchyCursorSigningKey"] = HierarchySigningKey,
            [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeCheckpointPersistence:Mode"] = "Coalesced",
            [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeCheckpointPersistence:MaxSegmentCheckpoints"] = "50",
            [$"CShells:Shells:{ShellName}:Features:WorkflowsRuntimeCheckpointPersistence:CoalesceDurableValueReads"] = coalesceDurableValueReads.ToString()
        };
        foreach (var id in SelectedFeatureIds)
            values[$"CShells:Shells:{ShellName}:Features:{id}"] = null;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IDistributedLockProvider, RuntimeEntityFrameworkCoreFeatureTests.ProcessLockProvider>();
        services.AddEfPersistenceResources(configuration, typeof(EfDurableValuePageReuseTests).Assembly);
        services.ConfigureDbContext<RuntimeSqliteDbContext>(options => options.AddInterceptors(pageReads));
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

    private static WorkflowExecutable NewExecutable()
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(DurableValuePageReuseActivity));
        Assert.Equal(SideEffectProfile.External, contract.SideEffectProfile);
        var type = new ValueTypeDescriptor("String");
        var sequenceContract = ClrActivityContractTestBuilder.BuildContract(typeof(SequenceActivity));
        var literal = new RuntimeInputBinding(
            "greeting",
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(Greeting), ValueProtectionPolicy.InstanceInline));
        var variable = new RuntimeVariableDeclaration("greeting", "Greeting", type, ValueProtectionPolicy.InstanceInline, literal);
        var first = NewLeaf("external-first", contract);
        var second = NewLeaf("external-second", contract);
        var root = new ExecutableNode(
            executableNodeId: "sequence-root",
            authoredActivityId: "authored-sequence-root",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: sequenceContract.DescriptorPayload,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, [first, second])],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = new[] { first.ExecutableNodeId, second.ExecutableNodeId } })),
            activityContract: sequenceContract);
        var identity = new WorkflowExecutableIdentity(
            "durable-page-reuse-artifact",
            "durable-page-reuse-definition",
            "durable-page-reuse-version",
            "1.0.0",
            "sha256:durable-page-reuse");

        return new WorkflowExecutable(
            identity,
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            inputContract: new WorkflowExecutableInputContract(
                WorkflowExecutableInputContract.CurrentVersion,
                [new WorkflowDeclaredInput(
                    "payload",
                    TypeReferenceFactory.FromClrType(typeof(string), TypeAliasConvention.CanonicalAlias),
                    isRequired: true)]),
            dependencies: [],
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            checkpointCadence: new WorkflowExecutableCheckpointCadence(WorkflowExecutableCheckpointCadence.CoalescedMode, 50),
            workflowVariables: [variable]);
    }

    private static ExecutableNode NewLeaf(string nodeId, ActivityContract contract)
    {
        var stringType = contract.Inputs[nameof(DurableValuePageReuseActivity.Payload)].Type;
        var variableType = contract.Inputs[nameof(DurableValuePageReuseActivity.VisibleGreeting)].Type;
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(DurableValuePageReuseActivity.Payload)] = new(
                nameof(DurableValuePageReuseActivity.Payload),
                stringType,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.WorkflowRequest,
                workflowRequest: new RuntimeWorkflowRequestReference("payload")),
            [nameof(DurableValuePageReuseActivity.VisibleGreeting)] = new(
                nameof(DurableValuePageReuseActivity.VisibleGreeting),
                variableType,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.VariableRead,
                variable: new RuntimeVariableReference("greeting", VariableReference.WorkflowScopeId))
        };

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(DurableValuePageReuseActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: WellKnownRuntimeActivityConsumers.ClrActivity,
            descriptorPayload: contract.DescriptorPayload,
            inputBindings: bindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static async Task<string> CaptureFailureTypeAsync(Func<Task> action)
    {
        var exception = await Record.ExceptionAsync(action);
        Assert.NotNull(exception);
        return exception.GetType().FullName!;
    }

    private static string CorruptInnerSignature(string coalescingToken)
    {
        var parts = coalescingToken.Split('.');
        Assert.Equal("crsp1", parts[0]);
        var base64Payload = parts[1].Replace('-', '+').Replace('_', '/');
        base64Payload = base64Payload.PadRight((base64Payload.Length + 3) / 4 * 4, '=');
        var payload = JsonNode.Parse(Convert.FromBase64String(base64Payload))!;
        var cursor = payload["Cursor"]!;
        var innerToken = cursor["InnerContinuation"]!.GetValue<string>().Split('.');
        Assert.Equal(3, innerToken.Length);
        Assert.NotEmpty(innerToken[2]);
        innerToken[2] = (innerToken[2][0] == 'A' ? "B" : "A") + innerToken[2][1..];
        cursor["InnerContinuation"] = string.Join('.', innerToken);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        return $"crsp1.{Encode(bytes)}.{Encode(SHA256.HashData(bytes))}";

        static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

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
        "WorkflowsRuntimeTriggers",
        "WorkflowsRuntimeCheckpointPersistence"
    ];

    private sealed record ScenarioCapture(
        int PageReadCount,
        int NonemptyPageReadCount,
        string SemanticSnapshot,
        string InvalidIdentityFailureType,
        string InvalidCursorFailureType,
        string InvalidInnerCursorFailureType);

    private sealed class DurableValuePageReadProbe : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<bool> _reads = new();

        public void Reset()
        {
            while (_reads.TryDequeue(out _))
            {
            }
        }

        public IReadOnlyCollection<bool> Snapshot() => _reads.ToArray();

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            if (sql.Contains(RuntimeOperationalStateEfModule.DurableValueTableName, StringComparison.OrdinalIgnoreCase) &&
                sql.Contains(nameof(DurableValueStateEntity.DurableValueIdOrderKey), StringComparison.OrdinalIgnoreCase) &&
                sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
            {
                // HasRows observes the provider result without advancing or consuming its reader.
                _reads.Enqueue(result.HasRows);
            }

            return ValueTask.FromResult(result);
        }
    }
}

public sealed class DurableValuePageReuseActivity : Activity<string>
{
    [ActivityInput(Key = nameof(Payload))]
    public string Payload { get; set; } = null!;

    [ActivityInput(Key = nameof(VisibleGreeting))]
    public string VisibleGreeting { get; set; } = null!;

    protected override ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete($"{VisibleGreeting}:{Payload}"));
}
