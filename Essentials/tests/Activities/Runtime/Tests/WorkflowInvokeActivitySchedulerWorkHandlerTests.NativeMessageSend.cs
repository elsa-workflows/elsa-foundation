using System.Reflection;
using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed partial class WorkflowInvokeActivitySchedulerWorkHandlerTests
{
    [Fact]
    public async Task Native_send_actual_invoke_assembles_exact_private_wait_and_real_intent_then_backend_refuses_before_effects()
    {
        var buffer = SendBuffer(); var access = new SendAccess(); var capture = new SendCapture();
        var executable = NativeSendExecutable();
        await _executableStore.SaveAsync(executable); await _activityStateStore.SaveAsync(NativeSendRunning(executable));
        // A leftover transient proposal from a previous activation must not survive the engine's initial reset.
        buffer.Stage(new(access.Current.RequireScope(), "wfexec-1", "actexec-1", "old-attempt", "node-start", "actexec-1", null,
            _now, new("stale-name", "stale-key", Json("null"))));
        var activity = new SendMessage(buffer, access, _activityStateStore);
        await using var provider = NewProvider(new FixedActivityActivator(activity), configure: services =>
        {
            services.AddSingleton<INativeMessageSendStager>(buffer);
            services.AddSingleton((INativeMessageSendStagingAccessor)buffer);
            services.AddSingleton<IPersistenceAccessContextAccessor>(access);
            services.AddSingleton<IRuntimeCheckpointCommitEnricher>(capture);
        });
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => NewHandler(provider).HandleAsync(NewInvokeWorkItem(executable.Identity)).AsTask());
        Assert.Equal("message.send.checkpoint-unavailable", failure.Code);
        var proposed = Assert.Single(capture.Sends); var plan = Assert.IsType<NativeMessageSendCheckpointPlan>(proposed.NativeMessageSend);
        var bookmark = Assert.Single(proposed.StateChanges.Bookmarks).State;
        var suspended = Assert.Single(proposed.StateChanges.ActivityExecutions).State;
        Assert.Equal(plan.BookmarkId, bookmark.BookmarkId); Assert.Equal(plan.ResumeTargetId, bookmark.ResumeTargetId);
        Assert.Equal(plan.BookmarkId, Assert.Single(suspended.BookmarkIds));
        Assert.Equal(plan.BookmarkId, Assert.Single(suspended.TriggerRegistrations!).RegistrationId);
        Assert.Equal("Payment", plan.Request.Submission.Name); Assert.Equal(_now, plan.Intent.RecordedAt);
        Assert.Same(plan.Intent, Assert.Single(proposed.PostCommitIntents)); // No separate CreateBookmark scheduler intent.
        Assert.Empty(proposed.StateChanges.WorkflowDispatches);
        Assert.Null(((INativeMessageSendStagingAccessor)buffer).Take(access.Current.RequireScope(), "wfexec-1", "actexec-1"));
        var actual = (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!;
        Assert.Equal(ActivityExecutionStatus.Running, actual.Status); Assert.Null(actual.PrivateState); Assert.Empty(actual.BookmarkIds);
        Assert.All(_checkpointWriter.ListCommits(), write =>
        {
            Assert.Null(write.Commit.NativeMessageSend);
            Assert.DoesNotContain(write.Commit.PostCommitIntents, item => item.Kind == NativeMessageSendCheckpointPlan.IntentKind);
            Assert.Empty(write.Commit.StateChanges.Bookmarks);
        });
        Assert.Empty(await _schedulerWorkQueue.ListAllAsync("wfexec-1"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_send_staging_is_reset_after_body_fault_or_requested_cancellation(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var buffer = SendBuffer(); var access = new SendAccess(); var executable = NativeSendExecutable();
        await _executableStore.SaveAsync(executable); await _activityStateStore.SaveAsync(NativeSendRunning(executable));
        var stager = new FailAfterSendStage(buffer, cancel ? cancellation : null);
        await using var provider = NewProvider(new FixedActivityActivator(new SendMessage(stager, access, _activityStateStore)), configure: services =>
        {
            services.AddSingleton<INativeMessageSendStagingAccessor>((INativeMessageSendStagingAccessor)buffer);
            services.AddSingleton<IPersistenceAccessContextAccessor>(access);
        });
        var operation = NewHandler(provider).HandleAsync(NewInvokeWorkItem(executable.Identity), cancellation.Token).AsTask();
        if (cancel) await Assert.ThrowsAsync<OperationCanceledException>(() => operation); else await operation;
        Assert.Null(((INativeMessageSendStagingAccessor)buffer).Take(access.Current.RequireScope(), "wfexec-1", "actexec-1"));
        Assert.All(_checkpointWriter.ListCommits(), write =>
        {
            Assert.Null(write.Commit.NativeMessageSend);
            Assert.DoesNotContain(write.Commit.PostCommitIntents, item => item.Kind == NativeMessageSendCheckpointPlan.IntentKind);
            Assert.Empty(write.Commit.StateChanges.Bookmarks);
        });
        if (!cancel) Assert.Equal(ActivityExecutionStatus.Faulted, (await _activityStateStore.FindAsync("wfexec-1", "actexec-1"))!.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Constructed_receipt_or_rejection_payload_never_completes_or_faults_without_actual_SourceOutcome(bool accepted)
    {
        var buffer = SendBuffer(); var access = new SendAccess();
        IStatefulActivity<MessageReceipt, NativeMessageSendWaitState, NativeMessageSendResult> activity = new SendMessage(buffer, access, _activityStateStore);
        var result = accepted
            ? new NativeMessageSendResult(new(new MessageReceipt("constructed-receipt", _now, MessageDeliveryMode.ImmediateOnly), false), null)
            : new NativeMessageSendResult(null, MessageSubmissionFailureKind.Forbidden);
        var attempt = new ActivityExecutionContext("wfexec-1", "actexec-1", "resume-attempt", "node-start", CancellationToken.None);
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => activity.ResumeAsync(new(attempt,
            new("constructed-intent", "constructed-wait", "constructed-hash"), result, "constructed-registration", "constructed-delivery")).AsTask());
        Assert.Equal("message.send.receipt-unavailable", failure.Code);
        Assert.Empty(_checkpointWriter.ListCommits());
    }

    [Fact]
    public async Task Hidden_send_accepts_JSON_null_as_authored_content_and_never_calls_a_publisher()
    {
        Assert.False(typeof(SendMessage).GetCustomAttribute<ActivityAttribute>()!.IsDiscoverable);
        var buffer = SendBuffer(); var access = new SendAccess(); var executable = NativeSendExecutable();
        var state = NativeSendRunning(executable);
        var values = state.InputSnapshot!.Values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        values["Payload"] = ValueEnvelope.Null(new("Json"), ValueProtectionPolicy.InstanceInline);
        state = state with
        {
            Metadata = new Dictionary<string, string> { [Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.ActivityAttemptActivationClaim] = "actexec-1:attempt:1" },
            InputSnapshot = new(state.InvocationId, state.InputSnapshot.ContractFingerprint, state.InputSnapshot.BindingFingerprint, values, _now)
        };
        await _activityStateStore.SaveAsync(state);
        IActivity activity = new SendMessage(buffer, access, _activityStateStore) { MessageName = "Payment", BusinessKey = "order", Payload = null };
        var transition = await activity.ExecuteAsync(new("wfexec-1", "actexec-1", "actexec-1:attempt:1", "node-start", CancellationToken.None));
        Assert.IsAssignableFrom<IStatefulActivitySuspensionTransition>(transition);
        var staged = ((INativeMessageSendStagingAccessor)buffer).Take(access.Current.RequireScope(), "wfexec-1", "actexec-1")!;
        Assert.Equal(JsonValueKind.Null, staged.Submission.Payload.ValueKind); Assert.Empty(_checkpointWriter.ListCommits());
    }

    private WorkflowExecutable NativeSendExecutable()
    {
        var original = NewTypedExecutable(); var descriptor = Json("{}");
        var inputs = new[]
        {
            new ActivityInputContract("MessageName", "MessageName", new("String"), true, false, false, null, ActivityValuePolicy.Default),
            new ActivityInputContract("BusinessKey", "BusinessKey", new("String"), true, false, false, null, ActivityValuePolicy.Default),
            new ActivityInputContract("Payload", "Payload", new("Json"), false, true, true, Json("null"), ActivityValuePolicy.Default),
            new ActivityInputContract("ProducerMessageId", "ProducerMessageId", new("String"), false, true, false, null, ActivityValuePolicy.Default),
            new ActivityInputContract("LifetimeSeconds", "LifetimeSeconds", new("Int32"), false, true, false, null, ActivityValuePolicy.Default)
        };
        var contract = new ActivityContract(SendMessage.ActivityType, "1", "typed", descriptor, inputs,
            new ActivityResultContract(new(TypeAliasConvention.CanonicalAlias(typeof(MessageReceipt))), true, ActivityValuePolicy.Default, []),
            ["Done"], new("typed", SendMessage.ActivityType));
        var node = new ExecutableNode("node-start", "authored", SendMessage.ActivityType, "1", "typed", descriptor,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: contract);
        var target = "node-start:" + SendMessage.ResumeTargetId;
        return new(original.Identity, node, new Dictionary<string, WorkflowExecutableResumeTarget>
        { [target] = new(target, "node-start", "ResumeAsync", new Dictionary<string, string>(), SendMessage.ResumeTargetId) },
            _now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }
    private ActivityExecutionState NativeSendRunning(WorkflowExecutable executable) => NewRunningState(executable, new Dictionary<string, ValueEnvelope>
    {
        ["MessageName"] = ValueEnvelope.Inline(new("String"), Json("\"Payment\""), ValueProtectionPolicy.InstanceInline),
        ["BusinessKey"] = ValueEnvelope.Inline(new("String"), Json("\"order\""), ValueProtectionPolicy.InstanceInline),
        ["Payload"] = ValueEnvelope.Inline(new("Json"), Json("{\"a\":1}"), ValueProtectionPolicy.InstanceInline),
        ["ProducerMessageId"] = ValueEnvelope.Null(new("String"), ValueProtectionPolicy.InstanceInline),
        ["LifetimeSeconds"] = ValueEnvelope.Null(new("Int32"), ValueProtectionPolicy.InstanceInline)
    });
    private static INativeMessageSendStager SendBuffer()
    {
        // Test the actual Runtime-owned internal buffer without widening product friendship or registering a feature.
        var type = typeof(NativeMessageSendCheckpointPreparation).Assembly.GetType("Elsa.Workflows.Runtime.Messaging.NativeMessageSendStagingBuffer", true)!;
        return (INativeMessageSendStager)Activator.CreateInstance(type, nonPublic: true)!;
    }
    private sealed class SendAccess : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("scope")); }
    private sealed class SendCapture : IRuntimeCheckpointCommitEnricher
    {
        internal readonly List<RuntimeCheckpointCommit> Sends = [];
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        { if (commit.NativeMessageSend is not null) Sends.Add(commit); return ValueTask.FromResult(commit); }
    }
    private sealed class FailAfterSendStage(INativeMessageSendStager inner, CancellationTokenSource? cancellation) : INativeMessageSendStager
    {
        public NativeMessageSendWaitState Stage(NativeMessageSendRequest request)
        {
            inner.Stage(request);
            if (cancellation is not null) { cancellation.Cancel(); cancellation.Token.ThrowIfCancellationRequested(); }
            throw new InvalidOperationException("Injected fault after transient send staging.");
        }
    }
}
