using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStructuralContinuationTests
{
    [Fact]
    public void State_and_control_clones_preserve_immutable_and_lazy_owner_facts_until_the_exact_handoff()
    {
        var at = DateTimeOffset.UnixEpoch;
        var supplied = new List<RuntimeHistorySourceFact> { new(RuntimeHistorySourceFactKind.ControlApplied, "eager", at) };
        var calls = 0;
        var continuation = RuntimeStructuralContinuation.Defer.WithHistorySourceFacts(supplied)
            .WithHistorySourceFacts(observed => { calls++; return [new(RuntimeHistorySourceFactKind.ControlApplied, "lazy", observed)]; });
        supplied.Clear();
        var value = ValueEnvelope.Inline(new("state"), JsonSerializer.SerializeToElement(new { cursor = 1 }), ValueProtectionPolicy.InstanceInline);
        var clone = continuation.WithState(value).WithControl(new(1, "consumer", 1, "run", "activity", "work", 0, false, [new("source-notice")]));
        Assert.Equal(0, calls);
        Assert.Single(clone.HistorySourceFacts!);
        var facts = clone.MaterializeHistorySourceFacts(at.AddSeconds(1));
        Assert.Equal(new[] { "eager", "lazy" }, facts.Select(fact => fact.SubjectId));
        Assert.Equal(at.AddSeconds(1), facts[1].OccurredAt);
        Assert.Equal(1, calls);
        Assert.NotNull(clone.StateUpdate);
        Assert.NotNull(clone.Control);
        Assert.Null(RuntimeStructuralContinuation.Defer.HistorySourceFacts);
        Assert.Throws<ArgumentNullException>(() => RuntimeStructuralContinuation.Defer.WithHistorySourceFacts((IReadOnlyList<RuntimeHistorySourceFact>)null!));
    }

    [Fact]
    public void Complete_CarriesOneImmutableOutcome()
    {
        var continuation = RuntimeStructuralContinuation.Complete("Approved");

        Assert.Equal(RuntimeStructuralContinuationKind.Complete, continuation.Kind);
        Assert.Equal("Approved", continuation.OutcomeName);
        Assert.False(typeof(RuntimeStructuralContinuation).GetProperty(nameof(RuntimeStructuralContinuation.OutcomeName))!.CanWrite);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Complete_RejectsBlankOutcome(string outcomeName) =>
        Assert.Throws<ArgumentException>(() => RuntimeStructuralContinuation.Complete(outcomeName));

    [Fact]
    public void Defer_CarriesNoTerminalPayload()
    {
        var continuation = RuntimeStructuralContinuation.Defer;

        Assert.Equal(RuntimeStructuralContinuationKind.Defer, continuation.Kind);
        Assert.Null(continuation.OutcomeName);
        Assert.Null(continuation.Fault);
        Assert.Null(continuation.CancellationReason);
    }

    [Fact]
    public void FaultAndCancel_CarryTheirTypedPayloads()
    {
        var fault = new ActivityFault("test.failure", "Expected failure.");

        Assert.Same(fault, RuntimeStructuralContinuation.Faulted(fault).Fault);
        Assert.Equal("Cancelled by test.", RuntimeStructuralContinuation.Cancel("Cancelled by test.").CancellationReason);
        Assert.Equal(ActivityOutcomes.Done, RuntimeStructuralContinuation.Complete().OutcomeName);
    }

    [Fact]
    public void Structural_state_is_one_typed_persistable_document_not_a_metadata_patch()
    {
        var type = new ValueTypeDescriptor("Elsa.Flowchart.State", schemaVersion: 2);
        var value = ValueEnvelope.Inline(
            type,
            JsonSerializer.SerializeToElement(new { cursor = 3 }),
            ValueProtectionPolicy.InstanceInline);

        var continuation = RuntimeStructuralContinuation.Defer.WithState(value, stateVersion: 2);

        Assert.Equal(2, continuation.StateUpdate!.StateVersion);
        Assert.Same(value, continuation.StateUpdate.Value);
        Assert.Null(typeof(RuntimeStructuralContinuation).GetProperty("PrivateMetadata"));
    }
}
