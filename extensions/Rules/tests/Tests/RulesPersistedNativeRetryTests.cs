using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Rules.Tests;

/// <summary>
/// Exercises a real compiled Rules bookmark resume when its retained result is briefly unavailable at the native
/// activity boundary. The scheduler must keep the consumed trigger's work item and redrive it after visibility delay.
/// </summary>
public sealed class RulesPersistedNativeRetryTests
{
    private static readonly TimeSpan UnavailableRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AcknowledgementTimeout = TimeSpan.FromSeconds(8);

    [Fact]
    public async Task Resume_store_unavailability_retains_trigger_redrives_once_and_acknowledges_after_completion()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync();
        var gate = new OneShotUnavailableFindGate();
        var dispatchProbe = new BookmarkResumeDispatchProbe();
        var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
        await using var fixture = await RulesPersistedNativeFixture.CreateAsync(
            executable,
            $"rules-native-retry-{Guid.NewGuid():N}",
            configureServices: services =>
            {
                services.RemoveAll<IRuleRunReader>();
                services.AddSingleton(gate);
                services.AddScoped<IRuleRunReader>(provider => new OneShotUnavailableRuleRunReader(
                    provider.GetRequiredService<IRuleRunStore>() as IRuleRunReader
                    ?? throw new InvalidOperationException("The Rules store must also implement its read contract."),
                    gate));

                services.AddSingleton(dispatchProbe);
                services.AddScoped<BookmarkResumeDispatcher>();
                services.RemoveAll<IBookmarkResumeDispatcher>();
                services.AddScoped<IBookmarkResumeDispatcher>(provider => new ArmingBookmarkResumeDispatcher(
                    provider.GetRequiredService<BookmarkResumeDispatcher>(),
                    provider.GetRequiredService<OneShotUnavailableFindGate>(),
                    provider.GetRequiredService<BookmarkResumeDispatchProbe>()));

                services.RemoveAll<RuntimeSchedulerWorkClaimOptions>();
                services.AddSingleton(new RuntimeSchedulerWorkClaimOptions
                {
                    UnavailableRetryDelay = UnavailableRetryDelay
                });

                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            },
            deliveryLeaseSeconds: 1);

        var reference = await fixture.PublishAsync($"retry-{fixture.WorkflowExecutionId}");
        await fixture.StartPublishedAsync(reference);
        await fixture.SweepPartitionAsync();

        var identity = await fixture.ReadRulesIdentityAsync(RulesCompiledPublicationFixture.RulesNodeId);
        var terminal = await fixture.DriveRulesToTerminalAsync(identity);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.Equal(RuleTerminalKind.Succeeded, terminal.Result!.Kind);
        var canonicalTerminal = RuleRecordCodec.TerminalBytes(terminal.Result);
        var terminalView = await fixture.FindRunAsync(identity);
        Assert.Equal(RuleRunReadDisposition.Found, terminalView.Disposition);
        var terminalRevision = terminalView.View!.ModelRevision;
        gate.SetTarget(identity);

        Assert.False(await fixture.DeliverOneAsync("native-unavailable-retry"));
        Assert.Equal(1, dispatchProbe.DispatchCount);
        Assert.Equal(1, gate.FailureCount);
        Assert.Equal(identity, gate.FailedIdentity);

        var afterUnavailable = await fixture.ActivityStatesAsync();
        var rulesAfterUnavailable = RulesPersistedNativeFixture.StateFor(
            afterUnavailable, RulesCompiledPublicationFixture.RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Running, rulesAfterUnavailable.Status);
        Assert.DoesNotContain(afterUnavailable,
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowStateAsync())!.Status);

        var consumedReceipt = Assert.Single(rulesAfterUnavailable.TriggerDeliveries!,
            delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(RulesNativeBoundary.ProviderId, consumedReceipt.ProviderId);
        Assert.Equal(RulesNativeBoundary.CompletionType, consumedReceipt.PayloadType);
        var openResumeAttempt = Assert.Single(rulesAfterUnavailable.Attempts!, attempt => attempt.EndedAt is null);
        Assert.Equal(ActivityAttemptReason.Resume, openResumeAttempt.Reason);
        Assert.Equal(consumedReceipt.DeliveryId, openResumeAttempt.TriggerDeliveryId);
        Assert.True(rulesAfterUnavailable.Metadata.TryGetValue(
            RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId, out var retainedWorkItemId));

        var retainedWork = await PendingResumeWorkAsync(fixture);
        Assert.Equal(retainedWorkItemId, retainedWork.WorkItemId);
        Assert.Equal(consumedReceipt.DeliveryId, retainedWork.CommandId);
        Assert.Empty(await PoisonRecordsAsync(fixture));
        var unavailableView = await fixture.FindRunAsync(identity);
        Assert.Equal(RuleRunReadDisposition.Found, unavailableView.Disposition);
        Assert.Equal(terminalRevision, unavailableView.View!.ModelRevision);
        Assert.Equal(canonicalTerminal, RuleRecordCodec.TerminalBytes(
            (await fixture.ReadTerminalAsync(identity)).Result!));

        // The row remains durable while its visibility lease is in the future. The query uses the runtime clock,
        // so advancing it exercises the same claimability predicate the resumption sweep consumes.
        Assert.Empty(await ClaimableExecutionsAsync(fixture, clock.GetUtcNow()));
        Assert.Single(await PendingSchedulerWorkAsync(fixture), item => item.WorkItemId == retainedWork.WorkItemId);
        clock.Advance(UnavailableRetryDelay - TimeSpan.FromTicks(1));
        Assert.Empty(await ClaimableExecutionsAsync(fixture, clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal([fixture.WorkflowExecutionId], await ClaimableExecutionsAsync(fixture, clock.GetUtcNow()));

        await fixture.SweepPartitionAsync();

        var completedStates = await fixture.ActivityStatesAsync();
        var rulesCompleted = RulesPersistedNativeFixture.StateFor(
            completedStates, RulesCompiledPublicationFixture.RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, rulesCompleted.Status);
        Assert.Equal(retainedWork.WorkItemId,
            rulesCompleted.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaimWorkItemId]);
        var completedReceipt = Assert.Single(rulesCompleted.TriggerDeliveries!,
            delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(consumedReceipt.DeliveryId, completedReceipt.DeliveryId);
        Assert.Equal(consumedReceipt.RegistrationId, completedReceipt.RegistrationId);
        Assert.Equal(consumedReceipt.DeduplicationKey, completedReceipt.DeduplicationKey);
        Assert.Equal(consumedReceipt.ProviderId, completedReceipt.ProviderId);
        Assert.Equal(new ValueTypeDescriptor(typeof(RuleTerminalResult).FullName!), rulesCompleted.Completion!.Result.Type);
        Assert.True(rulesCompleted.Completion.Result.InlineValue.HasValue,
            "The compiled CLR result contract declares the bounded RuleTerminalResult inline.");
        var nativeResult = JsonSerializer.Deserialize<RuleTerminalResult>(
            rulesCompleted.Completion.Result.InlineValue!.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(nativeResult);
        Assert.Equal(canonicalTerminal, RuleRecordCodec.TerminalBytes(nativeResult!));

        var resumeAttempts = rulesCompleted.Attempts!
            .Where(attempt => attempt.Reason == ActivityAttemptReason.Resume)
            .OrderBy(attempt => attempt.Ordinal)
            .ToArray();
        Assert.Equal(2, resumeAttempts.Length);
        Assert.All(resumeAttempts, attempt => Assert.Equal(consumedReceipt.DeliveryId, attempt.TriggerDeliveryId));
        Assert.Equal(ActivityTransitionKind.Fault, resumeAttempts[0].TransitionKind);
        Assert.Equal(ActivityTransitionKind.Complete, resumeAttempts[1].TransitionKind);
        Assert.Null(rulesCompleted.Fault);
        var completedView = await fixture.FindRunAsync(identity);
        Assert.Equal(RuleRunReadDisposition.Found, completedView.Disposition);
        Assert.Equal(terminalRevision, completedView.View!.ModelRevision);
        Assert.Equal(canonicalTerminal, RuleRecordCodec.TerminalBytes(
            (await fixture.ReadTerminalAsync(identity)).Result!));

        var afterRules = RulesPersistedNativeFixture.StateFor(
            completedStates, RulesPersistedNativeFixture.AfterRulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, afterRules.Status);
        var afterRulesAttempt = Assert.Single(afterRules.Attempts!);
        Assert.Equal(ActivityTransitionKind.Complete, afterRulesAttempt.TransitionKind);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowStateAsync())!.Status);
        Assert.DoesNotContain(await PendingSchedulerWorkAsync(fixture), item => item.WorkItemId == retainedWork.WorkItemId);
        Assert.Empty(await PoisonRecordsAsync(fixture));

        // The first native delivery attempt was deliberately unproven and left its DB-time lease intact. After it
        // expires, the completed Sequence parent supplies terminal proof, settling the Rules obligation without
        // redispatching the bookmark. The native completion payload itself was checked above against the full result.
        var acknowledgementDeadline = DateTimeOffset.UtcNow + AcknowledgementTimeout;
        var acknowledged = false;
        while (!acknowledged && DateTimeOffset.UtcNow < acknowledgementDeadline)
        {
            acknowledged = await fixture.DeliverOneAsync("native-unavailable-retry");
            if (!acknowledged)
                await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(acknowledged, "The expired terminal-delivery lease was not reclaimed and acknowledged.");
        Assert.Equal(1, dispatchProbe.DispatchCount);
        Assert.Equal(1, gate.FailureCount);
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            await fixture.ClaimDeliveryDispositionAsync("native-unavailable-after-ack"));
    }

    private static async Task<RuntimeSchedulerWorkItem> PendingResumeWorkAsync(RulesPersistedNativeFixture fixture)
    {
        var resumeItems = (await PendingSchedulerWorkAsync(fixture))
            .Where(item => item.CommandKind == WorkflowExecutionCommandKind.ResumeBookmark)
            .ToArray();
        return Assert.Single(resumeItems);
    }

    private static async Task<IReadOnlyCollection<RuntimeSchedulerWorkItem>> PendingSchedulerWorkAsync(
        RulesPersistedNativeFixture fixture)
    {
        await using var scope = await fixture.CreatePartitionScopeAsync();
        var queue = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        return (await queue.ListAsync(new RuntimeSchedulerWorkQuery(fixture.WorkflowExecutionId))).Items;
    }

    private static async Task<IReadOnlyCollection<string>> ClaimableExecutionsAsync(
        RulesPersistedNativeFixture fixture,
        DateTimeOffset now)
    {
        await using var scope = await fixture.CreatePartitionScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListClaimableWorkflowExecutionIdsAsync(new RuntimeSchedulerClaimableBacklogQuery(now));
    }

    private static async Task<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> PoisonRecordsAsync(
        RulesPersistedNativeFixture fixture)
    {
        await using var scope = await fixture.CreatePartitionScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>()
            .ListAsync(fixture.WorkflowExecutionId);
    }

    private sealed class OneShotUnavailableFindGate
    {
        private readonly object _sync = new();
        private RuleRunIdentity? _target;
        private RuleRunIdentity? _failedIdentity;
        private bool _armed;
        private int _failureCount;

        public int FailureCount
        {
            get { lock (_sync) return _failureCount; }
        }

        public RuleRunIdentity? FailedIdentity
        {
            get { lock (_sync) return _failedIdentity; }
        }

        public void SetTarget(RuleRunIdentity identity)
        {
            lock (_sync) _target = identity;
        }

        public void Arm()
        {
            lock (_sync) _armed = true;
        }

        public bool TryConsume(RuleRunIdentity identity)
        {
            lock (_sync)
            {
                if (!_armed || _target != identity)
                    return false;
                _armed = false;
                _failedIdentity = identity;
                _failureCount++;
                return true;
            }
        }
    }

    private sealed class BookmarkResumeDispatchProbe
    {
        private int _dispatchCount;
        public int DispatchCount => Volatile.Read(ref _dispatchCount);
        public void RecordDispatch() => Interlocked.Increment(ref _dispatchCount);
    }

    private sealed class OneShotUnavailableRuleRunReader(IRuleRunReader inner, OneShotUnavailableFindGate gate)
        : IRuleRunReader
    {
        public ValueTask<RuleWorkReadResult> ReadWorkAsync(
            RuleWorkToken token, CancellationToken cancellationToken = default) =>
            inner.ReadWorkAsync(token, cancellationToken);

        public ValueTask<RuleDeliveryReadResult> ReadDeliveryAsync(
            RuleDeliveryLease lease, CancellationToken cancellationToken = default) =>
            inner.ReadDeliveryAsync(lease, cancellationToken);

        public ValueTask<RuleRunReadResult> FindAsync(
            RuleRunIdentity identity, CancellationToken cancellationToken = default) =>
            gate.TryConsume(identity)
                ? ValueTask.FromResult(new RuleRunReadResult(RuleRunReadDisposition.Unavailable, null))
                : inner.FindAsync(identity, cancellationToken);

        public ValueTask<RuleTerminalReadResult> ReadTerminalAsync(
            RuleRunIdentity identity, CancellationToken cancellationToken = default) =>
            inner.ReadTerminalAsync(identity, cancellationToken);

        public ValueTask<RuleProgressReadResult> ReadProgressAsync(
            RuleRunIdentity identity, RuleProgressPosition? after, int take,
            CancellationToken cancellationToken = default) =>
            inner.ReadProgressAsync(identity, after, take, cancellationToken);
    }

    private sealed class ArmingBookmarkResumeDispatcher(
        BookmarkResumeDispatcher inner,
        OneShotUnavailableFindGate gate,
        BookmarkResumeDispatchProbe probe) : IBookmarkResumeDispatcher
    {
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(
            BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            probe.RecordDispatch();
            gate.Arm();
            return inner.DispatchAsync(request, dispatchOptions, cancellationToken);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private long _utcTicks = initialUtcNow.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

        public void Advance(TimeSpan duration) => Interlocked.Add(ref _utcTicks, duration.Ticks);
    }
}
