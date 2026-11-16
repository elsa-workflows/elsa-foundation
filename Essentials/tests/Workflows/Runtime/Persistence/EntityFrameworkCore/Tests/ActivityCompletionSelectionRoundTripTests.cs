using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Spec 1208: every outcome selection state round-trips through the runtime store's serializer at the current
/// value-flow document version. No stored data predates that version, so an older document is refused, not upcast.
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

    [Fact]
    public void An_older_document_version_is_refused()
    {
        var node = JsonNode.Parse(RuntimeArtifactJson.Serialize(State(OutcomeSelection.Named("A"))))!.AsObject();
        node["documentVersion"] = ActivityExecutionValueFlowDocumentVersions.Current - 1;

        var read = Read(node.ToJsonString());

        Assert.Equal(ActivityExecutionValueFlowDocumentVersions.Current - 1, read.DocumentVersion);
        Assert.Throws<InvalidOperationException>(read.EnsureValueFlowCompatible);
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
