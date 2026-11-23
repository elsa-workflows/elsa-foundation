using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 1208 FR-025: every outcome selection state round-trips through the runtime store's serializer at the current
/// value-flow document version. A version 2 document, as the contributor preview wrote it, reads its single outcome key
/// as a named selection of that key; an older document is still refused.
/// </summary>
public sealed class ActivityCompletionSelectionRoundTripTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [MemberData(nameof(Selections))]
    public void Every_selection_state_round_trips_at_the_current_version(OutcomeSelection selection)
    {
        var state = State(selection);

        var read = Read(RuntimeArtifactJson.Serialize(state));

        Assert.Equal(selection, read.Completion!.Selection);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Done")]
    public void A_version_2_outcome_key_reads_as_a_named_selection_of_that_key(string outcomeKey)
    {
        var read = Read(Version2Content(outcomeKey));

        Assert.Equal(ActivityExecutionValueFlowDocumentVersions.Current, read.DocumentVersion);
        Assert.Equal(OutcomeSelection.Named(outcomeKey), read.Completion!.Selection);
        read.EnsureValueFlowCompatible();
        Assert.DoesNotContain("outcomeKey", RuntimeArtifactJson.Serialize(read), StringComparison.Ordinal);
    }

    [Fact]
    public void A_completion_with_both_an_outcome_key_and_a_selection_is_refused()
    {
        var node = JsonNode.Parse(RuntimeArtifactJson.Serialize(State(OutcomeSelection.Named("A"))))!.AsObject();
        node["completion"]!.AsObject()["outcomeKey"] = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("A");

        Assert.ThrowsAny<Exception>(() => Read(node.ToJsonString()));
    }

    [Fact]
    public void A_version_1_document_is_still_refused()
    {
        var node = JsonNode.Parse(RuntimeArtifactJson.Serialize(State(OutcomeSelection.Named("A"))))!.AsObject();
        node["documentVersion"] = ActivityExecutionValueFlowDocumentVersions.Legacy;

        var read = Read(node.ToJsonString());

        Assert.Throws<InvalidOperationException>(read.EnsureValueFlowCompatible);
    }

    /// <summary>The version 2 shape: <c>documentVersion: 2</c> and <c>completion.outcomeKey</c>, with no selection.</summary>
    private static string Version2Content(string outcomeKey)
    {
        var node = JsonNode.Parse(RuntimeArtifactJson.Serialize(State(OutcomeSelection.Named(outcomeKey))))!.AsObject();
        node["documentVersion"] = ActivityExecutionValueFlowDocumentVersions.SingleOutcomeKey;
        var completion = node["completion"]!.AsObject();
        completion.Remove("selection");
        completion["outcomeKey"] = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(outcomeKey);
        return node.ToJsonString();
    }

    public static TheoryData<OutcomeSelection> Selections() =>
        [OutcomeSelection.Unspecified, OutcomeSelection.Empty, OutcomeSelection.Named("A"), OutcomeSelection.Named("B", "A")];

    private static ActivityExecutionState Read(string content) => RuntimeArtifactJson.Deserialize<ActivityExecutionState>(content);

    private static ActivityExecutionState State(OutcomeSelection? selection) => new(
        new ActivityExecution("activity-1", "workflow-1", "node-1", "authored-1", "Test.Activity", "1"),
        ActivityExecutionStatus.Completed,
        null,
        1,
        At,
        At,
        At,
        null,
        null,
        null,
        null,
        ActivitySchedulingProvenance.From("workflow-1", null, null, null, null, null, null, "test"),
        null,
        [],
        [],
        0,
        0,
        new Dictionary<string, string>(),
        Completion: selection is null
            ? null
            : new ActivityCompletion(
                "activity-1",
                "attempt-1",
                ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("result"), ValueProtectionPolicy.InstanceInline),
                selection,
                At,
                "sha256:test"));
}
