using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ActivityCompletionContractTests
{
    private static readonly ValueTypeDescriptor ResultType = new("Payments.ChargeResult");
    private static readonly ValueTypeDescriptor StringType = new("String");

    [Fact]
    public void Completion_commits_one_result_outcome_and_read_only_projections()
    {
        var contract = Contract();
        var attempt = Attempt();

        var projected = new ActivityCompletionProjector().Project(
            "invocation-1",
            attempt,
            contract,
            ActivityTransition.Complete(new ChargeResult("receipt-1", true), "Charged"),
            DateTimeOffset.UtcNow);

        Assert.Equal("Charged", projected.Completion.OutcomeKey);
        Assert.Equal(ResultType, projected.Completion.Result.Type);
        Assert.Equal("receipt-1", projected.Projections["receipt-id"].InlineValue!.Value.GetString());
        Assert.True(projected.Projections["approved"].InlineValue!.Value.GetBoolean());
    }

    [Fact]
    public void Projection_failure_rejects_the_whole_completion()
    {
        var contract = Contract(requiredProjectionPath: "missing");

        var exception = Assert.Throws<InvalidOperationException>(() => new ActivityCompletionProjector().Project(
            "invocation-1",
            Attempt(),
            contract,
            ActivityTransition.Complete(new ChargeResult("receipt-1", true), "Charged"),
            DateTimeOffset.UtcNow));

        Assert.Contains("VF-ACT-006", exception.Message);
    }

    [Fact]
    public void Undeclared_outcome_and_nonpersistable_result_are_rejected()
    {
        var projector = new ActivityCompletionProjector();
        Assert.Throws<InvalidOperationException>(() => projector.Project(
            "invocation-1", Attempt(), Contract(), ActivityTransition.Complete(new ChargeResult("r", true), "Unknown"), DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => projector.Project(
            "invocation-1", Attempt(), Contract(resultPolicy: ActivityValuePolicy.Default with { IsPersistable = false }), ActivityTransition.Complete(new ChargeResult("r", true), "Charged"), DateTimeOffset.UtcNow));
    }

    private static ActivityContract Contract(
        string requiredProjectionPath = "receiptId",
        ActivityValuePolicy? resultPolicy = null) =>
        new(
            "Payments.Charge",
            "1.0.0",
            "clr",
            System.Text.Json.JsonSerializer.SerializeToElement(new { typeAlias = "Payments.Charge" }),
            [],
            new ActivityResultContract(
                ResultType,
                true,
                resultPolicy ?? ActivityValuePolicy.Default,
                [
                    new ActivityResultProjectionContract("receipt-id", requiredProjectionPath, StringType, true, ActivityValuePolicy.Default),
                    new ActivityResultProjectionContract("approved", "approved", new ValueTypeDescriptor("Boolean"), true, ActivityValuePolicy.Default)
                ]),
            ["Charged", "Declined"],
            new ActivityActivationRequirement("clr", "Payments.Charge"));

    private static ActivityAttempt Attempt() =>
        new("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow);

    private sealed record ChargeResult(string ReceiptId, bool Approved);
}
