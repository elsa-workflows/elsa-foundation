using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class NativeMessageSendCheckpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Buffer_detaches_submission_and_drains_only_the_exact_scope_once()
    {
        var buffer = new NativeMessageSendStagingBuffer();
        using var document = JsonDocument.Parse("{\"a\":1}");
        var first = Request(payload: document.RootElement); var wait = buffer.Stage(first); document.Dispose();
        var same = buffer.Stage(Request(payload: Json(" { \"a\" : 1 } ")));
        Assert.Equal(wait.IntentId, same.IntentId); Assert.Null(buffer.Take(new("other"), "workflow", "invocation"));
        var drained = buffer.Take(first.Scope, "workflow", "invocation")!;
        Assert.Equal(1, drained.Submission.Payload.GetProperty("a").GetInt32());
        Assert.Null(buffer.Take(first.Scope, "workflow", "invocation"));
    }

    [Fact]
    public void Scope_framing_and_reset_preserve_opaque_ids_without_aliasing_or_cross_scope_removal()
    {
        var buffer = new NativeMessageSendStagingBuffer();
        var requests = new[] { Request(scope: "a:1:b"), Request(scope: "a", workflow: "b:1:workflow"),
            Request(scope: "scope\0\ud800"), Request(scope: "scope\0\ud801") };
        var waits = requests.Select(buffer.Stage).ToArray(); Assert.Equal(4, waits.Select(item => item.IntentId).Distinct().Count());
        buffer.Reset(requests[0].Scope, requests[0].WorkflowExecutionId, requests[0].InvocationId);
        Assert.Null(buffer.Take(requests[0].Scope, requests[0].WorkflowExecutionId, requests[0].InvocationId));
        foreach (var request in requests.Skip(1)) Assert.NotNull(buffer.Take(request.Scope, request.WorkflowExecutionId, request.InvocationId));
    }

    [Theory]
    [InlineData("producer")]
    [InlineData("key")]
    [InlineData("lifetime")]
    [InlineData("number")]
    [InlineData("operation")]
    public void Same_pending_invocation_refuses_changed_content_or_context(string changed)
    {
        var buffer = new NativeMessageSendStagingBuffer(); var original = Request(); var retained = buffer.Stage(original);
        var other = changed switch
        {
            "producer" => Request(producer: "changed"), "key" => Request(key: "changed"),
            "lifetime" => Request(lifetime: 0), "number" => Request(payload: Json("{\"a\":1.0}")),
            _ => Request(operation: "changed")
        };
        Assert.Throws<InvalidOperationException>(() => buffer.Stage(other));
        Assert.Same(original, buffer.Take(original.Scope, original.WorkflowExecutionId, original.InvocationId));
        Assert.DoesNotContain("order", retained.ToString());
    }

    [Theory]
    [InlineData("name")]
    [InlineData("key")]
    [InlineData("duplicate")]
    [InlineData("lifetime")]
    public void Staging_reuses_full_T004_rules_before_retaining_any_proposal(string invalid)
    {
        var request = Request(name: invalid == "name" ? new string('é', 129) : "Payment",
            key: invalid == "key" ? new string('x', 1025) : "order",
            payload: invalid == "duplicate" ? Json("{\"a\":1,\"a\":2}") : Json("null"),
            lifetime: invalid == "lifetime" ? 30 * 86400 + 1 : null);
        var buffer = new NativeMessageSendStagingBuffer();
        Assert.Throws<MessageSubmissionValidationException>(() => buffer.Stage(request));
        Assert.Null(buffer.Take(request.Scope, request.WorkflowExecutionId, request.InvocationId));
    }

    [Fact]
    public void Preparation_builds_one_real_source_intent_and_exact_wait_from_pinned_suspension()
    {
        var fixture = Fixture(); var plan = Prepare(fixture);
        Assert.Equal(NativeMessageSendCheckpointPlan.IntentKind, plan.Intent.Kind);
        Assert.Equal(Now, plan.Intent.RecordedAt); Assert.Equal(fixture.Wait.IntentId, plan.Intent.IntentId);
        Assert.Equal(fixture.Wait.BookmarkId, plan.Intent.DependsOnWaitRegistrationId);
        Assert.Equal(RuntimePostCommitOutboxItems.OutboxItemId("suspension-commit", plan.Intent), plan.OutboxItemId);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.ComputeIntent(plan.Intent), plan.IntentFingerprint);
        Assert.Equal(fixture.Target, plan.ResumeTargetId);
        Assert.Equal(plan.AuthoredFingerprint, plan.Intent.Payload!.Value.GetProperty("authoredFingerprint").GetString());
        Assert.False(plan.Intent.Payload.Value.TryGetProperty("receipt", out _));
        Assert.False(plan.Intent.Payload.Value.TryGetProperty("controlIncarnation", out _));
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("input")]
    [InlineData("private-wait")]
    [InlineData("registration")]
    [InlineData("attempt")]
    [InlineData("operation")]
    [InlineData("node")]
    public void Preparation_refuses_mismatched_actual_inputs_owner_attempt_or_private_wait(string changed)
    {
        var f = Fixture(); var state = f.State; var request = f.Request;
        if (changed == "scope") request = Request(scope: "other");
        if (changed == "input") request = Request(payload: Json("{\"a\":2}"));
        if (changed == "private-wait") state = state with { PrivateState = Private(new("foreign", "bookmark", "hash")) };
        if (changed == "registration") state = state with { TriggerRegistrations = [Registration(f.Target, "wrong")] };
        if (changed == "attempt") request = Request(attempt: "other-attempt");
        if (changed == "operation") request = Request(operation: "other-operation");
        if (changed == "node") request = Request(node: "other-node");
        Assert.Throws<InvalidOperationException>(() => new NativeMessageSendCheckpointPreparation().Prepare(request,
            f.Invoke, f.Executable, state, "suspension-commit", f.Request.Scope));
    }

    [Theory]
    [InlineData("producer")]
    [InlineData("number")]
    [InlineData("lifetime")]
    public void New_transient_replay_keeps_cause_identity_but_changed_authored_content_changes_source_equality(string changed)
    {
        var original = Fixture(); var next = Fixture(changed switch
        { "producer" => Request(producer: "producer-2"), "number" => Request(payload: Json("{\"a\":1.0}")), _ => Request(lifetime: 0) });
        var first = Prepare(original); var second = Prepare(next);
        Assert.Equal(first.Intent.IntentId, second.Intent.IntentId); Assert.Equal(first.BookmarkId, second.BookmarkId);
        Assert.NotEqual(first.AuthoredFingerprint, second.AuthoredFingerprint); Assert.NotEqual(first.IntentFingerprint, second.IntentFingerprint);
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(Commit(first)), RuntimeCheckpointCommitFingerprint.Compute(Commit(second)));
    }

    [Fact]
    public void Owned_plan_wire_preserves_opaque_source_ids_and_does_not_turn_receipts_into_authority()
    {
        var f = Fixture(Request(scope: "scope\ud800", workflow: "workflow\0\ud801")); var plan = Prepare(f);
        var encoded = JsonSerializer.Serialize(plan); var decoded = JsonSerializer.Deserialize<NativeMessageSendCheckpointPlan>(encoded)!;
        Assert.Equal(plan.Request.Scope, decoded.Request.Scope); Assert.Equal(plan.Request.WorkflowExecutionId, decoded.Request.WorkflowExecutionId);
        Assert.Equal(plan.IntentFingerprint, RuntimeCheckpointCommitFingerprint.ComputeIntent(decoded.Intent));
        Assert.Equal(plan.PinnedExecutable, decoded.PinnedExecutable); Assert.DoesNotContain("Payment", plan.ToString());
        Assert.Throws<RuntimeSchedulerWorkUnavailableException>(() => NativeMessageSendCheckpointPreparation.RefuseUnqualified(Commit(decoded)));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("string-version")]
    [InlineData("unknown")]
    public void Owned_plan_wire_refuses_unrecognized_shape_instead_of_dropping_it(string changed)
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(Prepare(Fixture())))!.AsObject();
        if (changed == "version") node["version"] = 2;
        if (changed == "string-version") node["version"] = "1";
        if (changed == "unknown") node["future"] = true;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NativeMessageSendCheckpointPlan>(node.ToJsonString()));
    }

    [Fact]
    public void Null_participant_stays_absent_on_ordinary_checkpoint_wire_and_clone_retains_present_plan()
    {
        var plan = Prepare(Fixture()); var legacy = Commit(null);
        Assert.False(JsonSerializer.SerializeToElement(legacy).TryGetProperty("NativeMessageSend", out _));
        Assert.False(JsonSerializer.SerializeToElement(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)).TryGetProperty("nativeMessageSend", out _));
        Assert.Same(plan, (Commit(plan) with { RetainSchedulerWork = true }).NativeMessageSend);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(legacy), RuntimeCheckpointCommitFingerprint.Compute(legacy with { NativeMessageSend = null }));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(legacy), RuntimeCheckpointCommitFingerprint.Compute(Commit(plan)));
        Assert.Throws<ArgumentException>(() => legacy with { NativeMessageSend = Prepare(Fixture(Request(workflow: "other"))) });
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("declared")]
    [InlineData("materialized")]
    public async Task All_current_checkpoint_paths_refuse_before_state_outbox_queue_or_coalescing_effects(string form)
    {
        var plan = Prepare(Fixture()); var commit = Commit(form == "plan" ? plan : null);
        if (form == "declared") commit = commit with { PostCommitIntents = [plan.Intent] };
        if (form == "materialized") commit = commit with
        { StateChanges = commit.StateChanges.WithPostCommitOutbox([new(plan.OutboxItemId, RuntimeStateChangeOperation.Upsert,
            new(plan.OutboxItemId, plan.Intent, RuntimePostCommitOutboxStatus.Pending, Now, Now), new Dictionary<string, string>())]) };
        var physical = new InMemoryRuntimeCheckpointCommitStore();
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => physical.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Empty(physical.ListCommits());
        var recorder = new NeverWritten(); var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), recorder,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => committer.CommitAsync(commit).AsTask()); Assert.Equal(0, recorder.Calls);
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor(); var session = new RuntimeCoalescingSession("workflow", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        using var current = sessions.Push(session);
        var folding = new CoalescingRuntimeCheckpointCommitStore(new(physical), sessions);
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => folding.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Deferred)).AsTask());
        Assert.False(session.HasBufferedChanges); Assert.Empty(physical.ListCommits());
    }

    private static NativeMessageSendRequest Request(string scope = "scope", string workflow = "workflow", string node = "node",
        string attempt = "attempt", string operation = "invocation", string name = "Payment", string key = "order", JsonElement? payload = null,
        string? producer = null, int? lifetime = null) => new(new(scope), workflow, "invocation", attempt, node, operation, null, Now,
            new(name, key, payload ?? Json("{\"a\":1}"), producer, lifetime));
    private static (NativeMessageSendRequest Request, NativeMessageSendWaitState Wait, WorkflowExecutable Executable,
        RuntimeInvokeActivityCommandPayload Invoke, ActivityExecutionState State, string Target) Fixture(NativeMessageSendRequest? request = null)
    {
        request ??= Request(); var wait = new NativeMessageSendStagingBuffer().Stage(request); var descriptor = Json("{}");
        var inputContracts = new[] { "MessageName", "BusinessKey", "Payload", "ProducerMessageId", "LifetimeSeconds" }
            .Select(key => new ActivityInputContract(key, key, new(key == "Payload" ? "Json" : key == "LifetimeSeconds" ? "Int32" : "String"),
                key is "MessageName" or "BusinessKey", key is not ("MessageName" or "BusinessKey"), false, null, ActivityValuePolicy.Default));
        var contract = new ActivityContract("send", "1", "send", descriptor, inputContracts,
            new ActivityResultContract(new("Receipt"), true, ActivityValuePolicy.Default, []), ["Done"], new("typed", "send"));
        var node = new ExecutableNode(request.ExecutableNodeId, "authored", "send", "1", "send", descriptor,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract);
        var target = request.ExecutableNodeId + ":" + NativeMessageSendCheckpointPlan.ResumeTargetKey;
        var executable = new WorkflowExecutable(new("artifact", "definition", "version", "1", "hash"), node,
            new Dictionary<string, WorkflowExecutableResumeTarget> { [target] = new(target, request.ExecutableNodeId, "Resume", new Dictionary<string, string>(), NativeMessageSendCheckpointPlan.ResumeTargetKey) },
            Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        var values = new Dictionary<string, ValueEnvelope>
        {
            ["MessageName"] = Inline(request.Submission.Name), ["BusinessKey"] = Inline(request.Submission.CorrelationKey),
            ["Payload"] = ValueEnvelope.Inline(new("Json"), request.Submission.Payload, ValueProtectionPolicy.InstanceInline),
            ["ProducerMessageId"] = ValueEnvelope.Null(new("String"), ValueProtectionPolicy.InstanceInline),
            ["LifetimeSeconds"] = ValueEnvelope.Null(new("Int32"), ValueProtectionPolicy.InstanceInline)
        };
        if (request.Submission.ProducerMessageId is { } producer) values["ProducerMessageId"] = Inline(producer);
        if (request.Submission.LifetimeSeconds is { } lifetime) values["LifetimeSeconds"] = ValueEnvelope.Inline(new("Int32"), JsonSerializer.SerializeToElement(lifetime), ValueProtectionPolicy.InstanceInline);
        var state = new ActivityExecutionState(new("invocation", request.WorkflowExecutionId, request.ExecutableNodeId, "authored", "send", "1"),
            ActivityExecutionStatus.Suspended, null, Now, Now, null, null, null, null, null, null, [], [], 0, 0, new Dictionary<string, string>())
        {
            ContractIdentity = new("send", "1", contract.SchemaFingerprint), InputSnapshot = new("invocation", contract.SchemaFingerprint, "bindings", values, Now),
            Attempts = [new(request.AttemptId, "invocation", 1, ActivityAttemptReason.Initial, Now, Now, transitionKind: Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Suspend)],
            PrivateState = Private(wait), TriggerRegistrations = [Registration(target, wait.StimulusHash)]
        };
        return (request, wait, executable, new(executable.Identity, request.ExecutableNodeId, "invocation", "StartedActivity"), state, target);
    }
    private static NativeMessageSendCheckpointPlan Prepare((NativeMessageSendRequest Request, NativeMessageSendWaitState Wait, WorkflowExecutable Executable,
        RuntimeInvokeActivityCommandPayload Invoke, ActivityExecutionState State, string Target) f) => new NativeMessageSendCheckpointPreparation().Prepare(f.Request, f.Invoke, f.Executable, f.State, "suspension-commit", f.Request.Scope);
    private static ActivityPrivateState Private(NativeMessageSendWaitState state) => new("invocation", 1,
        ValueEnvelope.Inline(new(TypeAliasConvention.CanonicalAlias(typeof(NativeMessageSendWaitState)), schemaVersion: 1), JsonSerializer.SerializeToElement(state), ValueProtectionPolicy.InstanceInline), "attempt", Now);
    private static Elsa.Workflows.Runtime.Core.Models.ActivityTriggerRegistration Registration(string target, string hash) => new("temporary", "invocation", target,
        new(TypeAliasConvention.CanonicalAlias(typeof(NativeMessageSendResult)), schemaVersion: 1), NativeMessageSendCheckpointPlan.WaitStimulusType, hash, ActivityTriggerDeduplicationPolicy.Once);
    private static RuntimeCheckpointCommit Commit(NativeMessageSendCheckpointPlan? plan) => new("suspension-commit",
        new("checkpoint", "ActivitySuspended", plan?.Request.WorkflowExecutionId ?? "workflow", Now, [], new Dictionary<string, string>()), new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { NativeMessageSend = plan };
    private static ValueEnvelope Inline(string value) => ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline);
    private static JsonElement Json(string text) { using var json = JsonDocument.Parse(text); return json.RootElement.Clone(); }
    private sealed class NeverWritten : IRuntimeCheckpointCommitStore
    {
        internal int Calls;
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("An unsupported native send reached the backend."); }
    }
}
