using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Primitives.Activities;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Rules.Tests;

/// <summary>
/// Runs the registered compiler's unchanged Sequence/RunRules output through durable native persistence. The
/// compiler fixture supplies a synthetic source-build semantic identity; these tests are runtime evidence, not
/// package provenance or the full workflow publication/REST path.
/// </summary>
public sealed class RulesPersistedCompiledSequenceTests
{
    private const string SourceReferenceId = "rules-native-compiled-sequence-source";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Published_sequence_waits_for_full_terminal_delivery_before_running_following_activity_once()
    {
        // Carry the actual published artifact into an isolated runtime with no design-store composition.
        // The runtime fixture supplies its own live reference; REST/deployment provenance remain separate gates.
        var executable = (await RulesCompiledPublicationFixture.SubmitAndPublishAsync()).Executable;
        var expectedIdentity = executable.Identity;
        await using var fixture = await RulesPersistedNativeFixture.CreateAsync(
            executable, $"rules-native-compiled-{Guid.NewGuid():N}");

        var reference = await fixture.PublishAsync(SourceReferenceId);
        Assert.Equal(expectedIdentity.ArtifactHash, executable.Identity.ArtifactHash);
        await fixture.StartPublishedAsync(reference);
        await fixture.SweepPartitionAsync();

        var rulesIdentity = await fixture.ReadRulesIdentityAsync(RulesCompiledPublicationFixture.RulesNodeId);
        var initiallySuspended = await fixture.ActivityStatesAsync();
        Assert.Equal(ActivityExecutionStatus.Suspended,
            RulesPersistedNativeFixture.StateFor(initiallySuspended, RulesCompiledPublicationFixture.RulesNodeId).Status);
        Assert.DoesNotContain(initiallySuspended,
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowStateAsync())!.Status);

        var admitted = await fixture.FindRunAsync(rulesIdentity);
        Assert.Equal(RuleRunReadDisposition.Found, admitted.Disposition);
        Assert.Equal(RuleRunStatus.Running, admitted.View!.Status);
        Assert.Equal(RuleTerminalReadDisposition.NotTerminal, (await fixture.ReadTerminalAsync(rulesIdentity)).Disposition);

        var terminal = await fixture.DriveRulesToTerminalAsync(rulesIdentity);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        var durableResult = terminal.Result!;
        Assert.Equal(RuleTerminalKind.Succeeded, durableResult.Kind);
        // The first round adds `derived`; the second is the required fixed-point round with no additions.
        Assert.Equal(2, durableResult.CommittedRounds);
        Assert.Equal(1, durableResult.ResultSchemaVersion);
        Assert.Null(durableResult.Diagnostic);
        Assert.Equal(new[] { "derived", "seed" }, durableResult.ResultFacts
            .Select(fact => fact.Atom).Order(StringComparer.Ordinal));
        Assert.Equal(RuleRecordCodec.FactsHash(RuleTestData.Facts("derived", "seed")), durableResult.FinalFactsSha256);

        var beforeDelivery = await fixture.ActivityStatesAsync();
        Assert.Equal(ActivityExecutionStatus.Suspended,
            RulesPersistedNativeFixture.StateFor(beforeDelivery, RulesCompiledPublicationFixture.RulesNodeId).Status);
        Assert.DoesNotContain(beforeDelivery,
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowStateAsync())!.Status);

        Assert.True(await fixture.DeliverOneAsync("compiled-sequence-native-delivery"));

        var completed = await fixture.ActivityStatesAsync();
        var rules = RulesPersistedNativeFixture.StateFor(completed, RulesCompiledPublicationFixture.RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, rules.Status);
        Assert.Equal(1, rules.TriggerDeliveries!.Count(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed));
        Assert.Equal(new ValueTypeDescriptor(typeof(RuleTerminalResult).FullName!), rules.Completion!.Result.Type);
        Assert.True(rules.Completion.Result.InlineValue.HasValue,
            "The compiled CLR result contract declares the bounded RuleTerminalResult inline.");
        var nativeResult = JsonSerializer.Deserialize<RuleTerminalResult>(rules.Completion.Result.InlineValue!.Value, WebJson);
        Assert.NotNull(nativeResult);
        Assert.Equal(RuleRecordCodec.TerminalBytes(durableResult), RuleRecordCodec.TerminalBytes(nativeResult!));

        var afterRules = RulesPersistedNativeFixture.StateFor(completed, RulesPersistedNativeFixture.AfterRulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, afterRules.Status);
        var afterRulesAttempt = Assert.Single(afterRules.Attempts!);
        Assert.Equal(Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind.Complete, afterRulesAttempt.TransitionKind);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowStateAsync())!.Status);
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            await fixture.ClaimDeliveryDispositionAsync("compiled-sequence-after-ack"));
    }

    [Fact]
    public async Task Compiled_round_budget_fault_is_durably_projected_and_acknowledged()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync(maxRounds: 1);
        await using var fixture = await RulesPersistedNativeFixture.CreateAsync(
            executable, $"rules-native-budget-{Guid.NewGuid():N}");

        var reference = await fixture.PublishAsync($"{SourceReferenceId}-budget");
        await fixture.StartPublishedAsync(reference);
        await fixture.SweepPartitionAsync();
        var rulesIdentity = await fixture.ReadRulesIdentityAsync(RulesCompiledPublicationFixture.RulesNodeId);

        var terminal = await fixture.DriveRulesToTerminalAsync(rulesIdentity);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.Equal(RuleTerminalKind.Faulted, terminal.Result!.Kind);
        Assert.Equal(1, terminal.Result.CommittedRounds);
        Assert.Equal("rules.round-budget-exhausted", terminal.Result.Diagnostic?.Code);
        Assert.Equal(new[] { "derived", "seed" }, terminal.Result.ResultFacts
            .Select(fact => fact.Atom).Order(StringComparer.Ordinal));
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowStateAsync())!.Status);
        var pending = await fixture.ActivityStatesAsync();
        Assert.Equal(ActivityExecutionStatus.Suspended,
            RulesPersistedNativeFixture.StateFor(pending, RulesCompiledPublicationFixture.RulesNodeId).Status);
        Assert.DoesNotContain(pending,
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);

        Assert.True(await fixture.DeliverOneAsync("compiled-budget-native-delivery"));

        var faulted = await fixture.ActivityStatesAsync();
        var rules = RulesPersistedNativeFixture.StateFor(faulted, RulesCompiledPublicationFixture.RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, rules.Status);
        Assert.Null(rules.Completion);
        Assert.Equal("rules.round-budget-exhausted", rules.Fault?.Code);
        Assert.Equal(typeof(ActivityFault).FullName, rules.Fault?.ExceptionType);
        Assert.True(rules.Fault!.Metadata.TryGetValue(ActivityFault.FaultTypeMetadataKey, out var marker));
        Assert.StartsWith("rules.terminal.v1:", marker, StringComparison.Ordinal);
        Assert.Equal(64, marker["rules.terminal.v1:".Length..].Length);
        Assert.Equal(1, rules.TriggerDeliveries!.Count(delivery => delivery.Status == ActivityTriggerDeliveryStatus.Consumed));
        Assert.DoesNotContain(faulted,
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.WorkflowStateAsync())!.Status);
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            await fixture.ClaimDeliveryDispositionAsync("compiled-budget-after-ack"));
    }

    [Fact]
    public async Task Semantic_refusal_is_a_retained_result_and_sequence_continues_on_refused_outcome()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync(initialFacts: ["outside-vocabulary"]);
        await using var fixture = await RulesPersistedNativeFixture.CreateAsync(
            executable, $"rules-native-refused-{Guid.NewGuid():N}");
        var reference = await fixture.PublishAsync($"{SourceReferenceId}-refused");
        await fixture.StartPublishedAsync(reference);
        await fixture.SweepPartitionAsync();

        var identity = await fixture.ReadRulesIdentityAsync();
        var terminal = await fixture.ReadTerminalAsync(identity);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.Equal(RuleTerminalKind.Refused, terminal.Result!.Kind);
        Assert.Equal("rules.invalid-finite-model", terminal.Result.Diagnostic?.Code);
        Assert.Equal(0, terminal.Result.CommittedRounds);
        Assert.Empty(terminal.Result.ResultFacts);
        Assert.DoesNotContain(await fixture.ActivityStatesAsync(),
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);

        Assert.True(await fixture.DeliverOneAsync("compiled-refused-native-delivery"));
        var states = await fixture.ActivityStatesAsync();
        var rules = RulesPersistedNativeFixture.StateFor(states, RulesCompiledPublicationFixture.RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Completed, rules.Status);
        Assert.Equal("Refused", rules.Completion!.OutcomeKey);
        var delivered = JsonSerializer.Deserialize<RuleTerminalResult>(rules.Completion.Result.InlineValue!.Value, WebJson);
        Assert.NotNull(delivered);
        Assert.Equal(RuleRecordCodec.TerminalBytes(terminal.Result), RuleRecordCodec.TerminalBytes(delivered!));
        Assert.Equal(ActivityExecutionStatus.Completed,
            RulesPersistedNativeFixture.StateFor(states, RulesPersistedNativeFixture.AfterRulesNodeId).Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowStateAsync())!.Status);
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            await fixture.ClaimDeliveryDispositionAsync("compiled-refused-after-ack"));
    }

    [Fact]
    public async Task Parent_cancellation_before_first_model_round_settles_real_cancelled_delivery()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync();
        await using var fixture = await RulesPersistedNativeFixture.CreateAsync(
            executable, $"rules-native-cancel-{Guid.NewGuid():N}");

        var reference = await fixture.PublishAsync($"{SourceReferenceId}-cancel");
        await fixture.StartPublishedAsync(reference);
        await fixture.SweepPartitionAsync();

        var rulesIdentity = await fixture.ReadRulesIdentityAsync(RulesCompiledPublicationFixture.RulesNodeId);
        var admitted = await fixture.FindRunAsync(rulesIdentity);
        Assert.Equal(RuleRunReadDisposition.Found, admitted.Disposition);
        Assert.Equal(RuleRunStatus.Running, admitted.View!.Status);
        Assert.Equal(0, admitted.View.CommittedRounds);
        Assert.Equal(RuleTerminalReadDisposition.NotTerminal, (await fixture.ReadTerminalAsync(rulesIdentity)).Disposition);
        var beforeCancel = await fixture.ActivityStatesAsync();
        Assert.Equal(ActivityExecutionStatus.Suspended,
            RulesPersistedNativeFixture.StateFor(beforeCancel, RulesCompiledPublicationFixture.RulesNodeId).Status);
        Assert.DoesNotContain(beforeCancel,
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);

        await fixture.Harness.CancelAsync(fixture.WorkflowExecutionId, RulesPersistedNativeFixture.Partition);
        await fixture.SweepPartitionAsync();

        var terminal = await fixture.ReadTerminalAsync(rulesIdentity);
        Assert.Equal(RuleTerminalReadDisposition.Found, terminal.Disposition);
        Assert.Equal(RuleTerminalKind.Cancelled, terminal.Result!.Kind);
        Assert.Equal(0, terminal.Result.CommittedRounds);
        Assert.Null(terminal.Result.Diagnostic);
        Assert.Equal(new[] { "seed" }, terminal.Result.ResultFacts.Select(fact => fact.Atom));
        var cancelled = await fixture.FindRunAsync(rulesIdentity);
        Assert.Equal(RuleRunReadDisposition.Found, cancelled.Disposition);
        Assert.Equal(RuleRunStatus.Cancelled, cancelled.View!.Status);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await fixture.WorkflowStateAsync())!.Status);

        var native = await fixture.ActivityStatesAsync();
        Assert.Equal(ActivityExecutionStatus.Cancelled,
            RulesPersistedNativeFixture.StateFor(native, RulesCompiledPublicationFixture.RulesNodeId).Status);
        Assert.DoesNotContain(native,
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);

        // The parent is already terminal, so the native worker settles this obligation with parent-terminal proof;
        // a child cancellation alone is not treated as evidence that the parent consumed a result.
        Assert.True(await fixture.DeliverOneAsync("compiled-cancel-native-delivery"));
        Assert.Equal(RuleDeliveryClaimDisposition.NoEligibleDelivery,
            await fixture.ClaimDeliveryDispositionAsync("compiled-cancel-after-ack"));
    }

    [Fact]
    public async Task Invalid_round_budget_faults_through_native_boundary_without_creating_a_rules_run()
    {
        var executable = await RulesCompiledPublicationFixture.CompileAsync(maxRounds: 0);
        await using var fixture = await RulesPersistedNativeFixture.CreateAsync(
            executable, $"rules-native-invalid-budget-{Guid.NewGuid():N}");

        var reference = await fixture.PublishAsync($"{SourceReferenceId}-invalid-budget");
        await fixture.StartPublishedAsync(reference);
        await fixture.SweepPartitionAsync();

        var states = await fixture.ActivityStatesAsync();
        var rules = RulesPersistedNativeFixture.StateFor(states, RulesCompiledPublicationFixture.RulesNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, rules.Status);
        Assert.Equal(RulesNativeBoundary.InvalidRoundLimit, rules.Fault?.Code);
        Assert.Equal(typeof(ActivityFault).FullName, rules.Fault?.ExceptionType);
        var address = RulesNativeBoundary.ReadAddress(rules);
        var request = RulesNativeBoundary.ReadState(rules);
        Assert.Equal(0, request.MaxRounds);

        var receipt = Assert.Single(rules.TriggerDeliveries!, item =>
            item.Status == ActivityTriggerDeliveryStatus.Consumed);
        Assert.Equal(address.RegistrationId, receipt.RegistrationId);
        Assert.Equal(RulesNativeBoundary.CompletionType, receipt.PayloadType);
        Assert.Equal(RulesNativeBoundary.ProviderId, receipt.ProviderId);
        Assert.Equal(RulesNativeBoundary.DeliveryDeduplicationKey(
            address, RulesNativeBoundary.InvalidDeliveryId(request, address)), receipt.DeduplicationKey);

        var resumeAttempt = Assert.Single(rules.Attempts!, attempt => attempt.Reason == ActivityAttemptReason.Resume);
        Assert.Equal(receipt.DeliveryId, resumeAttempt.TriggerDeliveryId);
        Assert.Equal(ActivityTransitionKind.Fault, resumeAttempt.TransitionKind);
        Assert.NotNull(resumeAttempt.EndedAt);

        var identity = new RuleRunIdentity(request.Scope, request.RunId);
        Assert.Equal(RuleRunReadDisposition.NotFound, (await fixture.FindRunAsync(identity)).Disposition);
        await using (var scope = fixture.Harness.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RulesDbContext>();
            Assert.Empty(await db.Runs.AsNoTracking().ToArrayAsync());
        }

        Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.WorkflowStateAsync())!.Status);
        Assert.DoesNotContain(states,
            state => state.Execution.ExecutableNodeId == RulesPersistedNativeFixture.AfterRulesNodeId);
        await AssertNoUnsettledStartIntentAsync(fixture);
    }

    private static async Task AssertNoUnsettledStartIntentAsync(RulesPersistedNativeFixture fixture)
    {
        await using var scope = await fixture.CreatePartitionScopeAsync();
        var outbox = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var pending = await outbox.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            DateTimeOffset.MaxValue, limit: 16, workflowExecutionId: fixture.WorkflowExecutionId,
            intentKind: RulesNativeBoundary.StartKind));
        Assert.Empty(pending);

        var claims = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var unsettled = await claims.ListClaimedAsync(new RuntimePostCommitOutboxClaimedQuery(
            fixture.WorkflowExecutionId, RulesNativeBoundary.StartKind, limit: 16));
        Assert.Empty(unsettled);
    }
}
