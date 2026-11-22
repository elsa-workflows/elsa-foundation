using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Spec 1208 FR-003: a background terminal result keeps its outcome selection (unspecified, several names in order, or
/// explicitly empty) through the work store's serializer, and its receipt digest is stable across that round trip and
/// tells the selection states apart, so a replayed receipt is recognised and a different selection is not.
/// </summary>
public sealed class BackgroundActivityWorkTerminalResultSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    // EfBackgroundActivityWorkStore (and the in-memory stores) keep the whole work item as JSON with the Web defaults.
    private static readonly JsonSerializerOptions StoreOptions = new(JsonSerializerDefaults.Web);

    public static TheoryData<OutcomeSelection> Selections() =>
        [OutcomeSelection.Unspecified, OutcomeSelection.Named("A", "B"), OutcomeSelection.Named("B", "A"), OutcomeSelection.Empty];

    [Theory]
    [MemberData(nameof(Selections))]
    public void The_selection_round_trips_through_the_work_store_serializer(OutcomeSelection selection)
    {
        var item = Item(Complete(selection));

        var read = JsonSerializer.Deserialize<BackgroundActivityWorkItem>(JsonSerializer.Serialize(item, StoreOptions), StoreOptions)!;

        Assert.Equal(BackgroundActivityWorkResultKind.Complete, read.Result!.Kind);
        Assert.Equal(selection, read.Result.Selection);
        Assert.Equal(selection.State, read.Result.Selection!.State);
        Assert.Equal(selection.ResolveNames(), read.Result.Selection.ResolveNames());
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void The_receipt_digest_is_stable_across_the_round_trip(OutcomeSelection selection)
    {
        var request = Request();
        var result = Complete(selection);

        var read = JsonSerializer.Deserialize<BackgroundActivityWorkTerminalResult>(JsonSerializer.Serialize(result, StoreOptions), StoreOptions)!;

        Assert.Equal(BackgroundActivityWorkReceipt.ComputeDigest(request, result), BackgroundActivityWorkReceipt.ComputeDigest(request, read));
        Assert.Equal(BackgroundActivityWorkReceipt.ComputeDigest(request, result), BackgroundActivityWorkReceipt.ComputeDigest(request, Complete(selection)));
    }

    [Fact]
    public void The_receipt_digest_tells_every_selection_apart()
    {
        var request = Request();

        var digests = new[]
        {
            OutcomeSelection.Unspecified, OutcomeSelection.Empty, OutcomeSelection.Named("A"), OutcomeSelection.Named("A", "B"), OutcomeSelection.Named("B", "A")
        }.Select(selection => BackgroundActivityWorkReceipt.ComputeDigest(request, Complete(selection))).ToArray();

        Assert.Equal(digests.Length, digests.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_completed_result_without_a_selection_is_unspecified_and_only_a_completion_carries_one()
    {
        Assert.Equal(OutcomeSelection.Unspecified, new BackgroundActivityWorkTerminalResult(BackgroundActivityWorkResultKind.Complete, resultJson: "1").Selection);
        Assert.Throws<ArgumentException>(() => new BackgroundActivityWorkTerminalResult(
            BackgroundActivityWorkResultKind.Cancel, OutcomeSelection.Empty, cancellationReason: "stop"));
    }

    private static BackgroundActivityWorkTerminalResult Complete(OutcomeSelection selection) =>
        new(BackgroundActivityWorkResultKind.Complete, selection, resultJson: "{\"value\":1}");

    private static BackgroundActivityWorkItem Item(BackgroundActivityWorkTerminalResult result)
    {
        var request = Request();
        return new BackgroundActivityWorkItem(request, BackgroundActivityWorkStatus.ResultReady, 1, 2, Now, Now,
            Result: result, ReceiptDigest: BackgroundActivityWorkReceipt.ComputeDigest(request, result));
    }

    private static BackgroundActivityWorkRequest Request()
    {
        var attempt = new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, Now);
        return new BackgroundActivityWorkRequest(
            "work-1", "workflow-1", "activity-1", attempt,
            new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"),
            "node", "contract-fingerprint",
            new ActivityInputSnapshot(attempt.InvocationId, "contract-fingerprint", "binding-fingerprint", new Dictionary<string, ValueEnvelope>(), Now),
            new RuntimeSchedulerWorkItem("coordinator-1", "workflow-1", "command-1", WorkflowExecutionCommandKind.ScheduleActivity,
                "envelope-1", "idempotency-1", Now, Now, 1));
    }
}
