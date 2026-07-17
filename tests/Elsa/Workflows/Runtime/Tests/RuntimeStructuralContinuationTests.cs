using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStructuralContinuationTests
{
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
}
