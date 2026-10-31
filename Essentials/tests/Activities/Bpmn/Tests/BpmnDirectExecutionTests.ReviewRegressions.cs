using System.IO.Compression;
using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Activities;
using RunBpmnState = Elsa.Activities.Bpmn.Models.RunBpmnState;
using Elsa.Primitives.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Events;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Owned_embedding_wrapper_errors_keep_the_public_checkpoint_and_retained_invocation_context(bool cancellation, bool wrongFormat)
    {
        var at = DateTimeOffset.UtcNow;
        var payload = wrongFormat ? JsonSerializer.SerializeToElement(new { FormatVersion = "bad", Invocation = new { } })
            : JsonSerializer.SerializeToElement("malformed-wrapper");
        var retained = new ActivityExecutionState(new("wrapper-invocation", "wrapper-execution", "wrapper-node", "wrapper-authored", RunBpmn.ActivityType, "1.0.0"),
            ActivityExecutionStatus.Suspended, null, at, at, null, null, null, null, null, null, [], [], 0, 0, new Dictionary<string, string>())
        {
            PrivateState = new("wrapper-invocation", 1, ValueEnvelope.Inline(new(typeof(RunBpmnState).FullName!, schemaVersion: 1),
                payload, ValueProtectionPolicy.InstanceInline), "wrapper-attempt", at)
        };
        var reads = 0;
        var persisted = 0;
        var activities = DeliveryFake<IActivityExecutionStateStore>((method, _) => method.Name == "FindAsync"
            ? ValueTask.FromResult<ActivityExecutionState?>(ReadRetained()) : throw new InvalidOperationException(method.Name));
        ActivityExecutionState ReadRetained() { reads++; return retained; }
        var native = new BpmnNativeWorkBoundary(null!, null!, null!, activities, null!, null!, null!, TimeProvider.System, null!);
        var enricher = new BpmnEmbeddingCheckpointEnricher(activities, native);
        var proposed = cancellation ? retained with { Status = ActivityExecutionStatus.Cancelled, PrivateState = null } : retained;
        var bookmark = new BookmarkState("wrapper-bookmark", "wrapper-execution", "wrapper-invocation", "wrapper-node", "wrapper-resume",
            "wrapper-stimulus", "wrapper-hash", null, new Dictionary<string, string>(), at, null);
        var changes = new RuntimeCheckpointStateChangeSet(null, null,
            [new(proposed.InvocationId, RuntimeStateChangeOperation.Upsert, proposed, new Dictionary<string, string>())],
            cancellation ? [] : [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())], [], [], []);
        var commit = new RuntimeCheckpointCommit("wrapper-commit", new("wrapper-checkpoint", cancellation ? "ActivityCancelled" : RuntimeCheckpointNames.BookmarkCreated,
            "wrapper-execution", at, [], new Dictionary<string, string>()), changes, [], new Dictionary<string, string>());
        var store = DeliveryFake<IRuntimeCheckpointCommitStore>((method, _) => { persisted++; throw new InvalidOperationException(method.Name); });
        var committer = new RuntimeCheckpointCommitter(DeliveryFake<IRuntimeCheckpointPersistencePolicy>(), store,
            DeliveryFake<IRuntimeExecutionOwnershipContextAccessor>(), [enricher], []);
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => committer.CommitAsync(commit).AsTask());
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        foreach (var identity in new[] { "wrapper-execution", "wrapper-invocation", "wrapper-node", "wrapper-commit" })
            Assert.Contains(identity, error.Message, StringComparison.Ordinal);
        Assert.Equal(cancellation ? 1 : 0, reads);
        Assert.Equal(0, persisted);
    }

    [Fact]
    public async Task Accepted_start_replays_after_publication_retirement_without_admitting_new_work()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var runtime = scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("operator", null);
        var definition = Material().Definition.Deserialize<BpmnModelDefinition>(Web)!;
        var published = await runtime.PublishAsync(definition, "retained-publication", caller);
        var started = await runtime.StartAsync(published.SourceReferenceId, "retained-start", null, caller);
        Assert.True(await scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .RetireAsync(published.SourceReferenceId, DateTimeOffset.UtcNow));
        var replay = await runtime.StartAsync(published.SourceReferenceId, "retained-start", new Dictionary<string, JsonElement>(), caller);
        Assert.True(replay.Replay);
        Assert.False(replay.Pending);
        Assert.Equal(started.ExecutionId, replay.ExecutionId);
        Assert.Equal(started.Result!.Value.GetRawText(), replay.Result!.Value.GetRawText());
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() =>
            runtime.StartAsync(published.SourceReferenceId, "fresh-start", null, caller).AsTask());
        await Assert.ThrowsAsync<BpmnModelResourceMissingException>(() =>
            runtime.StartAsync(published.SourceReferenceId, "retained-start", null, new("another-operator", null)).AsTask());
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
            runtime.StartAsync("different-source", "retained-start", null, caller).AsTask());
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() => runtime.StartAsync(published.SourceReferenceId,
            "retained-start", new Dictionary<string, JsonElement> { ["changed"] = JsonSerializer.SerializeToElement(true) }, caller).AsTask());
        Assert.Equal(1, (await runtime.FindExecutionAsync(started.ExecutionId, caller))!.Revision);
    }

    [Fact]
    public async Task Human_work_inspection_wraps_owned_persisted_JSON_with_execution_context()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new RuntimeModelExecutionState(1, new("tenant-a"), "broken-work-execution", "tenant-a", new("elsa.bpmn", 1),
            Material().Identity, "incarnation", 1, RuntimeModelLifecycle.Running, new("bpmn-runtime", "operator"), now, now,
            JsonSerializer.SerializeToElement(new { formatVersion = 1, work = "invalid-shape" }), null);
        var access = new InspectionAccess();
        var runtime = new BpmnModelRuntime(null!, null!, null!, null!, null!, access, null!, TimeProvider.System, null!, states: new InspectionReader(state));
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() =>
            runtime.ListWorkAsync(state.ExecutionId, new(), new("operator", "tenant-a")).AsTask());
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains(state.ExecutionId, error.Message);
    }

    [Fact]
    public async Task A_refused_construct_names_its_process_element_and_type()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var process = new BpmnProcessDefinition("approval-process", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("unsupported-call-42", BpmnElementTypes.CallActivity), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("call", "start", "unsupported-call-42"), new("end", "unsupported-call-42", "end")]);
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>()
            .PublishAsync(new(1, JsonSerializer.SerializeToElement(process, Web), []), "refused-profile", new("operator", null)).AsTask());
        Assert.Contains("approval-process", error.Message);
        Assert.Contains("unsupported-call-42", error.Message);
        Assert.Contains(BpmnElementTypes.CallActivity, error.Message);
    }

    private sealed class InspectionAccess : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("tenant-a")); }

    private sealed class InspectionReader(RuntimeModelExecutionState state) : IRuntimeModelExecutionReader
    {
        public ValueTask<RuntimeModelExecutionState?> FindAsync(string executionId, CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeModelExecutionState?>(state);
        public ValueTask<RuntimeModelInputReceipt?> FindInputAsync(string executionId, string inputId, CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeModelInputReceipt?>(null);
        public ValueTask<RuntimeStorePage<RuntimeModelExecutionState>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new RuntimeStorePage<RuntimeModelExecutionState>(request, [state], null));
    }

    [Fact]
    public async Task An_external_observer_can_subscribe_to_the_public_Core_protocol_startup_event()
    {
        var observed = new ProtocolStartupObservation();
        await using var host = await OpenAsync(configure: services =>
        {
            services.AddSingleton(observed);
            services.AddEventHandler<RuntimeExecutionProtocolsInitializing, ObserveProtocolStartup>();
        });
        Assert.Same(typeof(IRuntimeExecutionProtocolSource).Assembly, typeof(RuntimeExecutionProtocolsInitializing).Assembly);
        Assert.True(typeof(RuntimeExecutionProtocolsInitializing).IsPublic);
        Assert.Equal(1, observed.Calls);
    }

    private sealed class ProtocolStartupObservation { public int Calls { get; set; } }

    private sealed class ObserveProtocolStartup(ProtocolStartupObservation observation) : IEventHandler<RuntimeExecutionProtocolsInitializing>
    {
        public Task Handle(RuntimeExecutionProtocolsInitializing domainEvent, CancellationToken cancellationToken)
        {
            observation.Calls++;
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_invalid_service_retry_of_human_work_is_rejected_without_blocking_following_commands(bool cancel)
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var definition = TimedHumanMaterial("PT1H", emptyServiceInventory: true);
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveModelAsync(definition);
        var material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
        await SendAsync(host, material, "start", "start", new { });
        var reader = scope.ServiceProvider.GetRequiredService<IRuntimeModelExecutionReader>();
        var waiting = (await reader.FindAsync("direct-execution"))!;
        var human = Assert.Single(BpmnDirectExecutionDriver.Read(waiting).Work, work => work.Timer is null);
        await SendAsync(host, material, "invalid-retry", "retry-service", new
        {
            incidentId = "nonexistent-incident", human.InvocationId, failedAttemptId = human.AttemptId
        });
        var rejected = (await reader.FindInputAsync("direct-execution", "invalid-retry"))!;
        Assert.Equal("Rejected", rejected.Result.GetProperty("status").GetString());
        Assert.Equal("incident-not-current", rejected.Result.GetProperty("code").GetString());
        Assert.Equal(waiting.Revision, rejected.AcceptedRevision);
        if (cancel) await SendAsync(host, material, "following", "cancel", new { reason = "ordinary cancellation" });
        else await SendAsync(host, material, "following", "complete-human", new { human.InvocationId, human.AttemptId });
        var terminal = (await reader.FindAsync("direct-execution"))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, terminal.Lifecycle);
        Assert.Equal(cancel ? "Cancelled" : "Completed", terminal.TerminalResult!.Value.GetProperty("status").GetString());
        Assert.Equal("Accepted", (await reader.FindInputAsync("direct-execution", "following"))!.Result.GetProperty("status").GetString());
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new("direct-execution"))).Items);
    }

    [Fact]
    public async Task An_independently_cancelled_native_worker_cannot_settle_while_its_actual_downstream_cancel_is_pending()
    {
        await using (var raw = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualIndependentNativeCancellation.sqlite.gz")))
        await using (var zipped = new GZipStream(raw, CompressionMode.Decompress))
        await using (var target = File.Create(database)) await zipped.CopyToAsync(target);
        await using var host = await OpenAsync(admitNewExecutions: false);
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var reader = services.GetRequiredService<IRuntimeModelExecutionReader>();
        var before = Assert.Single((await reader.ListPageAsync(new())).Items);
        Assert.Empty(BpmnDirectExecutionDriver.Read(before).Cancellations ?? []);
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(before).Work).NativeWork!;
        var pending = await services.GetRequiredService<IRuntimePostCommitOutboxStore>()
            .GetDeliverableAsync(new(DateTimeOffset.UtcNow, 20, origin.WorkerExecutionId));
        var resultSource = Assert.Single(pending, item => item.Intent.Kind == BpmnNativeWorkBoundary.ResultKind);
        var cancelSource = Assert.Single(pending, item => item.Intent.Kind == "Elsa.Etl.Cancel.v1");
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, cancelSource.Status);
        var result = resultSource.Intent.Payload!.Value.Deserialize<BpmnNativeWorkResult>(Web)!;
        var material = (await services.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(before.Executable.ArtifactId))!;
        var command = new RuntimeModelExecutionCommand(before.Executable, before.MaterialIncarnationId,
            origin.ResultInputId, "native-result", resultSource.Intent.Payload.Value, before.Authority, before.TenantId);
        var now = DateTimeOffset.UtcNow;
        var item = new RuntimeSchedulerWorkItem("test-result", before.ExecutionId, command.InputId,
            WorkflowExecutionCommandKind.ExecuteModel, "test-result", "test-result", now, now);
        var context = new RuntimeModelExecutionContext(before.Scope, material, before, command, item);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => services.GetRequiredService<BpmnNativeWorkBoundary>()
            .ValidateResultAsync(context, result, CancellationToken.None).AsTask());
        Assert.Null(await reader.FindInputAsync(before.ExecutionId, origin.ResultInputId));
        Assert.Equal(JsonSerializer.Serialize(before, Web), JsonSerializer.Serialize(await reader.FindAsync(before.ExecutionId), Web));
    }

    [Fact]
    public async Task Malformed_owned_process_JSON_has_a_domain_error_with_material_context_and_original_cause()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var source = Material();
        var definition = new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(new { elements = 42 }), []);
        var material = new RuntimeModelMaterial(1, source.Identity, source.Protocol, source.Profile,
            JsonSerializer.SerializeToElement(definition, Web), source.InputContract, [], [], []);
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<BpmnDirectExecutionDriver>()
            .ValidateMaterialAsync(material).AsTask());
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains(material.Identity.ArtifactId, error.Message);
        var runtimeError = await Assert.ThrowsAsync<BpmnModelValidationException>(() => scope.ServiceProvider.GetRequiredService<IBpmnModelRuntime>()
            .PublishAsync(definition, "malformed-process", new("operator", null)).AsTask());
        Assert.IsAssignableFrom<JsonException>(runtimeError.InnerException);
    }

    [Fact]
    public async Task Malformed_owned_command_JSON_has_a_domain_error_with_execution_and_input_context()
    {
        await using var host = await OpenAsync();
        await using var scope = await ScopeAsync(host);
        var definition = Material();
        var store = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        await store.SaveModelAsync(definition);
        var material = (await store.FindMaterialAsync(definition.Identity.ArtifactId))!;
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() => SendAsync(host, material,
            "malformed-start", "start", new { inputs = 42 }));
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains("direct-execution", error.Message);
        Assert.Contains("malformed-start", error.Message);
    }
}
