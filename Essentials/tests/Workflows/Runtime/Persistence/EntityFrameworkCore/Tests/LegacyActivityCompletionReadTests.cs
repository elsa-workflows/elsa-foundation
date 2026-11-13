using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 1208 FR-025 / Q11: an activity execution written with value-flow document version 2 recorded one completion
/// outcome key. Read by the runtime store's serializer, it becomes a named selection of exactly that key at the current
/// document version; no unspecified or empty history is invented, and every selection state round-trips.
/// </summary>
public sealed class LegacyActivityCompletionReadTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_version_2_completion_outcome_key_reads_as_a_named_selection()
    {
        var legacy = LegacyContent(State(OutcomeSelection.Named("Approved")), "Approved");

        var upcast = Read(legacy);

        Assert.Equal(ActivityExecutionValueFlowDocumentVersions.Current, upcast.DocumentVersion);
        Assert.Equal(OutcomeSelection.Named("Approved"), upcast.Completion!.Selection);
        upcast.EnsureValueFlowCompatible();
    }

    [Fact]
    public void A_legacy_Done_key_stays_a_named_selection_of_Done()
    {
        var upcast = Read(LegacyContent(State(OutcomeSelection.Named("Done")), "Done"));

        Assert.Equal(OutcomeSelectionState.Named, upcast.Completion!.Selection.State);
        Assert.Equal(new[] { "Done" }, upcast.Completion.OutcomeNames);
    }

    [Fact]
    public void A_version_2_row_without_a_completion_only_moves_its_document_version()
    {
        var node = JsonNode.Parse(RuntimeArtifactJson.Serialize(State(selection: null)))!.AsObject();
        node["documentVersion"] = ActivityExecutionValueFlowDocumentVersions.SingleOutcomeKey;

        var upcast = Read(node.ToJsonString());

        Assert.Equal(ActivityExecutionValueFlowDocumentVersions.Current, upcast.DocumentVersion);
        Assert.Null(upcast.Completion);
    }

    [Fact]
    public void A_record_with_both_a_selection_and_an_outcome_key_is_refused()
    {
        var node = JsonNode.Parse(RuntimeArtifactJson.Serialize(State(OutcomeSelection.Named("A"))))!.AsObject();
        node["completion"]!.AsObject()["outcomeKey"] = EfRelationalIdentity.Encode("A");

        Assert.ThrowsAny<Exception>(() => RuntimeArtifactJson.Deserialize<ActivityExecutionState>(node.ToJsonString()));
    }

    [Fact]
    public void A_current_record_stores_no_outcome_key()
    {
        Assert.DoesNotContain("outcomeKey", RuntimeArtifactJson.Serialize(State(OutcomeSelection.Named("A"))), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void Every_selection_state_round_trips_at_the_current_version(OutcomeSelection selection)
    {
        var state = State(selection);

        var read = Read(RuntimeArtifactJson.Serialize(state));

        Assert.Equal(selection, read.Completion!.Selection);
    }

    public static TheoryData<OutcomeSelection> Selections() =>
        [OutcomeSelection.Unspecified, OutcomeSelection.Empty, OutcomeSelection.Named("A"), OutcomeSelection.Named("B", "A")];

    private static ActivityExecutionState Read(string content) => RuntimeArtifactJson.Deserialize<ActivityExecutionState>(content);

    /// <summary>
    /// The version 2 shape: <c>documentVersion: 2</c> and <c>completion.outcomeKey</c> holding the one recorded key,
    /// with no <c>selection</c>; every other field as the current writer stores it.
    /// </summary>
    private static string LegacyContent(ActivityExecutionState state, string outcomeKey)
    {
        var node = JsonNode.Parse(RuntimeArtifactJson.Serialize(state))!.AsObject();
        node["documentVersion"] = ActivityExecutionValueFlowDocumentVersions.SingleOutcomeKey;
        var completion = node["completion"]!.AsObject();
        completion.Remove("selection");
        completion["outcomeKey"] = EfRelationalIdentity.Encode(outcomeKey);
        return node.ToJsonString();
    }

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
