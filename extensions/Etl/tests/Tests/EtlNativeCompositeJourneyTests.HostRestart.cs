using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Etl.Tests;

/// <summary>
/// A native parent waiting on RunEtl survives a host restart over the same SQLite files and output directory. The restart
/// is an in-process host rebuild (<see cref="NativeEtlJourney.RebuildAsync"/>): the ETL worker, every DI container and
/// SQLite connection of the first host are gone before the second host reads the files. It is not a process kill; that
/// witness is the following leaf. Each case cuts at a different point of the ETL hand-off and asserts one native outcome.
/// </summary>
public sealed partial class EtlNativeCompositeJourneyTests
{
    private static readonly TimeSpan HeldDeliveryLease = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData(CompositeKind.Sequence)]
    [InlineData(CompositeKind.Flowchart)]
    [InlineData(CompositeKind.CompensationScope)]
    public Task A_suspended_RunEtl_survives_a_host_rebuild_before_any_processing_and_completes_once(CompositeKind compositeKind) =>
        WithRestartableJourneyAsync(compositeKind, NativeOutcome.Succeeded, async chain =>
        {
            var invocation = await chain.Current.StartSuspendedAsync();

            // Cut: the parent is durably suspended and the run is admitted; the worker never ran in this host.
            await chain.RebuildAsync();

            await chain.Current.StartWorkerAsync();
            await chain.Current.WaitForNativeResultAsync(invocation, ActivityExecutionStatus.Completed, WorkflowExecutionStatus.Completed);
            await AssertCompletedOnceAsync(chain.Current, invocation);
        });

    [Theory]
    [InlineData(CompositeKind.Sequence)]
    [InlineData(CompositeKind.Flowchart)]
    [InlineData(CompositeKind.CompensationScope)]
    public Task A_result_claimed_for_delivery_before_native_consumption_survives_a_host_rebuild_and_is_consumed_once(CompositeKind compositeKind)
    {
        var hold = new EtlDeliveryHold();
        var leaseSeconds = (int)HeldDeliveryLease.TotalSeconds;
        return WithRestartableJourneyAsync(compositeKind, NativeOutcome.Succeeded, async chain =>
        {
            var invocation = await chain.Current.StartSuspendedAsync();
            await chain.Current.StartWorkerAsync();

            // The first host runs the pipeline to its terminal result and claims the delivery lease, then dies before
            // dispatching into the parent or acknowledging the delivery.
            await hold.Claimed.Task.WaitAsync(TimeSpan.FromSeconds(30));
            var claimedAt = DateTimeOffset.UtcNow;
            var run = Assert.IsType<EtlRunView>(await chain.Current.ReadRunAsync(invocation.Scope, invocation.RunId));
            Assert.Equal(EtlRunStatus.Succeeded, run.Status);
            Assert.Null((await chain.Current.ReadOutboxAsync(invocation.Scope, invocation.RunId)).AcknowledgedAtUtcTicks);
            var pending = await chain.Current.ReadNativeStateAsync();
            Assert.Equal(ActivityExecutionStatus.Suspended,
                Assert.Single(pending.Activities, state => state.Execution.ExecutableNodeId == chain.Current.RunEtlNodeId).Status);

            // The rebuilt host reads time from a test clock that starts at the real now, so the dead owner's lease is still
            // in force; its worker must not deliver until the clock passes the lease's expiry.
            var clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            await chain.RebuildAsync(deliveryLeaseSeconds: leaseSeconds, clock: clock);
            await chain.Current.StartWorkerAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            Assert.Null((await chain.Current.ReadOutboxAsync(invocation.Scope, invocation.RunId)).AcknowledgedAtUtcTicks);
            Assert.Equal(ActivityExecutionStatus.Suspended,
                Assert.Single((await chain.Current.ReadNativeStateAsync()).Activities, state => state.Execution.ExecutableNodeId == chain.Current.RunEtlNodeId).Status);

            clock.Now = claimedAt + HeldDeliveryLease + TimeSpan.FromSeconds(1);
            await chain.Current.WaitForNativeResultAsync(invocation, ActivityExecutionStatus.Completed, WorkflowExecutionStatus.Completed);
            await AssertCompletedOnceAsync(chain.Current, invocation);
        }, deliveryHold: hold, deliveryLeaseSeconds: leaseSeconds);
    }

    [Theory]
    [InlineData(CompositeKind.Sequence, NativeOutcome.BusinessError)]
    [InlineData(CompositeKind.Flowchart, NativeOutcome.BusinessError)]
    [InlineData(CompositeKind.CompensationScope, NativeOutcome.BusinessError)]
    [InlineData(CompositeKind.Sequence, NativeOutcome.Faulted)]
    [InlineData(CompositeKind.Flowchart, NativeOutcome.Faulted)]
    [InlineData(CompositeKind.CompensationScope, NativeOutcome.Faulted)]
    public Task A_terminal_outcome_reached_after_a_host_rebuild_surfaces_once(CompositeKind compositeKind, NativeOutcome outcome) =>
        WithRestartableJourneyAsync(compositeKind, outcome, async chain =>
        {
            var invocation = await chain.Current.StartSuspendedAsync();

            await chain.RebuildAsync();

            var childStatus = outcome == NativeOutcome.BusinessError ? ActivityExecutionStatus.Completed : ActivityExecutionStatus.Faulted;
            var parentStatus = outcome switch
            {
                NativeOutcome.BusinessError => WorkflowExecutionStatus.Completed,
                NativeOutcome.Faulted when compositeKind == CompositeKind.CompensationScope => WorkflowExecutionStatus.Completed,
                _ => WorkflowExecutionStatus.Faulted
            };
            await chain.Current.StartWorkerAsync();
            await chain.Current.WaitForNativeResultAsync(invocation, childStatus, parentStatus);

            var native = await chain.Current.ReadNativeStateAsync();
            var child = Assert.Single(native.Activities, state => state.Execution.ExecutableNodeId == chain.Current.RunEtlNodeId);
            Assert.Equal(childStatus, child.Status);
            await AssertConsumedDeliveryAsync(chain.Current, invocation, child);
            var run = Assert.IsType<EtlRunView>(await chain.Current.ReadRunAsync(invocation.Scope, invocation.RunId));
            if (outcome == NativeOutcome.BusinessError)
            {
                Assert.Equal(EtlRunStatus.BusinessError, run.Status);
                Assert.Equal(1, run.TerminalResult!.RejectedRecords);
                Assert.Equal(["BusinessError"], WorkflowExecutionRun.CompletionOutcomes(child));
                Assert.Equal(ActivityExecutionStatus.Completed,
                    Assert.Single(native.Activities, state => state.Execution.ExecutableNodeId == chain.Current.AfterEtlNodeId).Status);
            }
            else
            {
                Assert.Equal(EtlRunStatus.Faulted, run.Status);
                Assert.Equal("etl.output-write-failed", run.TerminalResult!.Diagnostic?.Code);
                Assert.Equal("etl.output-write-failed", child.Fault?.Code);
                Assert.DoesNotContain(native.Activities, state => state.Execution.ExecutableNodeId == chain.Current.AfterEtlNodeId);
            }
        });

    [Theory]
    [InlineData(CompositeKind.Sequence)]
    [InlineData(CompositeKind.Flowchart)]
    [InlineData(CompositeKind.CompensationScope)]
    public Task A_parent_cancelled_before_a_host_rebuild_settles_the_admitted_run_after_it_and_refuses_a_late_result(CompositeKind compositeKind) =>
        WithRestartableJourneyAsync(compositeKind, NativeOutcome.Succeeded, async chain =>
        {
            var invocation = await chain.Current.StartSuspendedAsync();

            // The parent is cancelled while the child is in flight. The native side commits at once; the cancel intent
            // to ETL sits in the native post-commit outbox and the run is still admitted when the host dies.
            await chain.Current.CancelNativeAsync(subtree: false);
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await chain.Current.ReadNativeStateAsync()).Workflow.Status);
            var admitted = Assert.IsType<EtlRunView>(await chain.Current.ReadRunAsync(invocation.Scope, invocation.RunId));
            Assert.NotEqual(EtlRunStatus.Cancelled, admitted.Status);

            await chain.RebuildAsync();

            // The rebuilt host's sweep delivers the retained intent; the shared assertion then starts a worker only now
            // and proves the late terminal is settled with parent-terminal proof instead of resurrecting the run.
            await chain.Current.WaitForNativeCancellationAsync(invocation, subtree: false);
            await AssertNativeCancellationAsync(chain.Current, invocation, subtree: false);
        });

    private static async Task AssertCompletedOnceAsync(NativeEtlJourney journey, EtlInvocation invocation)
    {
        var completed = await journey.ReadNativeStateAsync();
        Assert.Equal(WorkflowExecutionStatus.Completed, completed.Workflow.Status);
        var child = Assert.Single(completed.Activities, state => state.Execution.ExecutableNodeId == journey.RunEtlNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, child.Status);
        await AssertConsumedDeliveryAsync(journey, invocation, child);
        var afterEtl = Assert.Single(completed.Activities, state => state.Execution.ExecutableNodeId == journey.AfterEtlNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, afterEtl.Status);
        Assert.Single(afterEtl.Attempts!);
        var run = Assert.IsType<EtlRunView>(await journey.ReadRunAsync(invocation.Scope, invocation.RunId));
        Assert.Equal(EtlRunStatus.Succeeded, run.Status);
        AssertCompletionResult(child, run);
        Assert.NotNull((await journey.ReadOutboxAsync(invocation.Scope, invocation.RunId)).AcknowledgedAtUtcTicks);
    }

    private static async Task WithRestartableJourneyAsync(CompositeKind kind, NativeOutcome outcome, Func<JourneyChain, Task> test,
        EtlDeliveryHold? deliveryHold = null, int deliveryLeaseSeconds = 30)
    {
        var chain = new JourneyChain(await NativeEtlJourney.CreateAsync(kind, outcome, deliveryHold: deliveryHold, deliveryLeaseSeconds: deliveryLeaseSeconds));
        try
        {
            await test(chain);
        }
        catch (Exception exception)
        {
            await chain.Current.PreserveFailureAsync(exception);
            throw;
        }
        finally
        {
            await chain.Current.DisposeAsync();
        }
    }

    /// <summary>Tracks the live host generation so failure evidence and disposal always target the current one.</summary>
    private sealed class JourneyChain(NativeEtlJourney first)
    {
        public NativeEtlJourney Current { get; private set; } = first;

        public async Task RebuildAsync(int deliveryLeaseSeconds = 30, TimeProvider? clock = null) =>
            Current = await Current.RebuildAsync(deliveryLeaseSeconds: deliveryLeaseSeconds, clock: clock);
    }

    private sealed partial class NativeEtlJourney
    {
        /// <summary>
        /// Simulates a host restart: stops the worker and disposes this host's DI container and SQLite connections while
        /// keeping the directory, then builds the next host generation over the same files, execution id and outcome
        /// configuration. The returned journey owns the directory from then on; if the rebuild fails, the files stay as
        /// evidence of the previous generation.
        /// </summary>
        public async Task<NativeEtlJourney> RebuildAsync(EtlDeliveryHold? deliveryHold = null, int deliveryLeaseSeconds = 30, TimeProvider? clock = null)
        {
            _directoryHandedOff = true;
            await DisposeAsync();
            return await CreateAsync(CompositeKind, Outcome, gate: null, includeBindings: _includeBindings, http: _http,
                deliveryHold: deliveryHold, deliveryLeaseSeconds: deliveryLeaseSeconds,
                existingDirectory: _directory, retainedWorkflowExecutionId: WorkflowExecutionId, generation: _generation + 1, clock: clock);
        }
    }

    /// <summary>Signals that the worker claimed a delivery lease; the held delivery never dispatches or acknowledges.</summary>
    private sealed class EtlDeliveryHold
    {
        public TaskCompletionSource Claimed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class HeldEtlResultDelivery(EtlDeliveryHold hold) : IEtlResultDelivery
    {
        public ValueTask<EtlDeliveryDisposition?> DeliverAsync(EtlDeliveryLease delivery, CancellationToken cancellationToken = default)
        {
            hold.Claimed.TrySetResult();
            // No disposition: the worker leaves the lease claimed and unacknowledged, as a crash mid-delivery would.
            return ValueTask.FromResult<EtlDeliveryDisposition?>(null);
        }
    }

    /// <summary>Overrides only the wall clock; timers stay real, so the worker keeps polling while the lease clock moves.</summary>
    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
