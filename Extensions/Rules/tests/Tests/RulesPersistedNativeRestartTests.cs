using Elsa.Activities.Runtime.Core.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Rules.Tests;

/// <summary>
/// A native parent waiting on RunRules survives a host restart over the same SQLite file. The restart is an in-process
/// host rebuild (<see cref="RulesPersistedNativeFixture.ReopenAsync"/>): every worker reference, DI container and SQLite
/// connection of the first host is gone before the second host reads the file. It is not a process kill; that witness is
/// the following leaf. Each case cuts at a different point of the Rules hand-off and asserts exactly one native outcome.
/// </summary>
public sealed class RulesPersistedNativeRestartTests
{
    private const string SourceReferenceId = "rules-native-restart-source";

    [Fact]
    public async Task A_suspended_RunRules_survives_a_host_rebuild_before_any_round_and_completes_once()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync();
        var first = await RulesPersistedNativeFixture.CreateAsync(executable, $"rules-restart-wait-{Guid.NewGuid():N}");
        RulesPersistedNativeFixture fixture = first;
        try
        {
            var reference = await first.PublishAsync($"{SourceReferenceId}-wait");
            await first.StartPublishedAsync(reference);
            await first.SweepPartitionAsync();
            var rulesIdentity = await first.ReadRulesIdentityAsync(RulesCompiledPublicationFixture.RulesNodeId);
            Assert.Equal(RuleRunStatus.Running, (await first.FindRunAsync(rulesIdentity)).View!.Status);
            Assert.Equal(ActivityExecutionStatus.Suspended,
                RulesPersistedNativeFixture.StateFor(await first.ActivityStatesAsync(), RulesCompiledPublicationFixture.RulesNodeId).Status);

            // Cut: the parent is durably suspended and the run is admitted; nothing has executed a round yet.
            fixture = await first.ReopenAsync();

            Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowStateAsync())!.Status);
            Assert.Equal(RuleRunStatus.Running, (await fixture.FindRunAsync(rulesIdentity)).View!.Status);
            var terminal = await fixture.DriveRulesToTerminalAsync(rulesIdentity);
            Assert.Equal(RuleTerminalKind.Succeeded, terminal.Result!.Kind);
            Assert.True(await fixture.DeliverOneAsync("restart-wait-delivery"));

            await AssertCompletedOnceAsync(fixture);
            Assert.False(await fixture.DeliverOneAsync("restart-wait-late-delivery"));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_terminal_result_claimed_for_delivery_before_the_rebuild_is_consumed_exactly_once_after_the_lease_expires()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync();
        // Rules lease expiry is database time, so this case uses a short real lease and guards its negative assertion by the
        // elapsed time; the reopen itself takes well under the lease.
        const int leaseSeconds = 3;
        var first = await RulesPersistedNativeFixture.CreateAsync(executable, $"rules-restart-claimed-{Guid.NewGuid():N}",
            deliveryLeaseSeconds: leaseSeconds);
        RulesPersistedNativeFixture fixture = first;
        try
        {
            var reference = await first.PublishAsync($"{SourceReferenceId}-claimed");
            await first.StartPublishedAsync(reference);
            await first.SweepPartitionAsync();
            var rulesIdentity = await first.ReadRulesIdentityAsync(RulesCompiledPublicationFixture.RulesNodeId);
            var terminal = await first.DriveRulesToTerminalAsync(rulesIdentity);
            Assert.Equal(RuleTerminalKind.Succeeded, terminal.Result!.Kind);

            // The first host claims the delivery lease and then dies before dispatching or acknowledging. The store's
            // own expiry is the authority for both the negative window and the wait.
            var claimed = await first.ClaimDeliveryAsync("dead-host");
            Assert.Equal(RuleDeliveryClaimDisposition.Claimed, claimed.Disposition);
            var expiry = claimed.Lease!.ExpiresAt;
            Assert.Equal(ActivityExecutionStatus.Suspended,
                RulesPersistedNativeFixture.StateFor(await first.ActivityStatesAsync(), RulesCompiledPublicationFixture.RulesNodeId).Status);

            fixture = await first.ReopenAsync(deliveryLeaseSeconds: leaseSeconds);

            // The dead owner's lease is honoured: while it is in force the new host finds nothing to deliver.
            Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowStateAsync())!.Status);
            Assert.True(DateTimeOffset.UtcNow < expiry, "The reopen took longer than the lease; the negative assertion is not meaningful on this machine.");
            Assert.False(await fixture.DeliverOneAsync("restart-claimed-early-delivery"));
            Assert.Equal(ActivityExecutionStatus.Suspended,
                RulesPersistedNativeFixture.StateFor(await fixture.ActivityStatesAsync(), RulesCompiledPublicationFixture.RulesNodeId).Status);

            var remaining = expiry + TimeSpan.FromMilliseconds(500) - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining);
            Assert.True(await fixture.DeliverOneAsync("restart-claimed-delivery"));

            await AssertCompletedOnceAsync(fixture);
            Assert.False(await fixture.DeliverOneAsync("restart-claimed-late-delivery"));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_round_budget_fault_survives_a_host_rebuild_and_faults_the_parent_once()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync(maxRounds: 1);
        var first = await RulesPersistedNativeFixture.CreateAsync(executable, $"rules-restart-fault-{Guid.NewGuid():N}");
        RulesPersistedNativeFixture fixture = first;
        try
        {
            var reference = await first.PublishAsync($"{SourceReferenceId}-fault");
            await first.StartPublishedAsync(reference);
            await first.SweepPartitionAsync();
            var rulesIdentity = await first.ReadRulesIdentityAsync(RulesCompiledPublicationFixture.RulesNodeId);
            var terminal = await first.DriveRulesToTerminalAsync(rulesIdentity);
            Assert.Equal(RuleTerminalKind.Faulted, terminal.Result!.Kind);
            Assert.Equal("rules.round-budget-exhausted", terminal.Result.Diagnostic?.Code);

            // Cut: the fault is durable in the Rules store, the parent still waits.
            fixture = await first.ReopenAsync();

            Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowStateAsync())!.Status);
            Assert.Equal(RuleTerminalKind.Faulted, (await fixture.ReadTerminalAsync(rulesIdentity)).Result!.Kind);
            Assert.True(await fixture.DeliverOneAsync("restart-fault-delivery"));

            var faulted = await fixture.ActivityStatesAsync();
            var rules = RulesPersistedNativeFixture.StateFor(faulted, RulesCompiledPublicationFixture.RulesNodeId);
            Assert.Equal(ActivityExecutionStatus.Faulted, rules.Status);
            Assert.Null(rules.Completion);
            Assert.Equal("rules.round-budget-exhausted", rules.Fault?.Code);
            Assert.Equal(1, rules.TriggerDeliveries!.Count(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed));
            Assert.DoesNotContain(faulted, state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);
            Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.WorkflowStateAsync())!.Status);
            Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery, await fixture.ClaimDeliveryDispositionAsync("restart-fault-after-ack"));
            Assert.False(await fixture.DeliverOneAsync("restart-fault-late-delivery"));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    [Fact]
    public async Task A_parent_cancellation_issued_before_the_rebuild_settles_the_rules_run_after_it()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync();
        var first = await RulesPersistedNativeFixture.CreateAsync(executable, $"rules-restart-cancel-{Guid.NewGuid():N}");
        RulesPersistedNativeFixture fixture = first;
        try
        {
            var reference = await first.PublishAsync($"{SourceReferenceId}-cancel");
            await first.StartPublishedAsync(reference);
            await first.SweepPartitionAsync();
            var rulesIdentity = await first.ReadRulesIdentityAsync(RulesCompiledPublicationFixture.RulesNodeId);
            Assert.Equal(RuleRunStatus.Running, (await first.FindRunAsync(rulesIdentity)).View!.Status);

            // The parent is cancelled. The native side commits inline; the cancel intent to Rules sits in the native
            // post-commit outbox and the Rules run is still Running when the host dies.
            await first.Harness.CancelAsync(first.WorkflowExecutionId, RulesPersistedNativeFixture.Partition);
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await first.WorkflowStateAsync())!.Status);
            Assert.Equal(RuleRunStatus.Running, (await first.FindRunAsync(rulesIdentity)).View!.Status);
            Assert.Equal(RuleTerminalReadDisposition.NotTerminal, (await first.ReadTerminalAsync(rulesIdentity)).Disposition);

            fixture = await first.ReopenAsync();

            await fixture.SweepPartitionAsync();
            var terminal = await fixture.ReadTerminalAsync(rulesIdentity);
            Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
            Assert.Equal(RuleTerminalKind.Cancelled, terminal.Result!.Kind);
            Assert.Equal(RuleRunStatus.Cancelled, (await fixture.FindRunAsync(rulesIdentity)).View!.Status);

            var native = await fixture.ActivityStatesAsync();
            Assert.Equal(ActivityExecutionStatus.Cancelled,
                RulesPersistedNativeFixture.StateFor(native, RulesCompiledPublicationFixture.RulesNodeId).Status);
            Assert.DoesNotContain(native, state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);
            Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.WorkflowStateAsync())!.Status);

            // The delivery obligation is acknowledged against the terminal parent and nothing is left to deliver.
            Assert.True(await fixture.DeliverOneAsync("restart-cancel-delivery"));
            Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery, await fixture.ClaimDeliveryDispositionAsync("restart-cancel-after-ack"));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static async Task AssertCompletedOnceAsync(RulesPersistedNativeFixture fixture)
    {
        var completed = await fixture.ActivityStatesAsync();
        var rules = RulesPersistedNativeFixture.StateFor(completed, RulesCompiledPublicationFixture.RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, rules.Status);
        Assert.Equal(1, rules.TriggerDeliveries!.Count(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed));
        var afterRules = RulesPersistedNativeFixture.StateFor(completed, RulesPersistedNativeFixture.AfterRulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, afterRules.Status);
        Assert.Single(afterRules.Attempts!);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowStateAsync())!.Status);
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery, await fixture.ClaimDeliveryDispositionAsync("restart-after-ack"));
    }
}
