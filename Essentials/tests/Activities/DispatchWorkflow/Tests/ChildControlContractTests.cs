using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Wire and data-boundary tests; these do not qualify dispatch authority or host delivery.</summary>
public sealed class ChildControlContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly WorkflowDispatchIdentity Identity = new("caller-workflow", "call-invocation");
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");

    [Fact]
    public void Terminal_control_round_trips_without_reclassifying_safe_diagnostics()
    {
        var diagnostics = FaultDiagnostics();
        var result = new DispatchWorkflowResult(Identity.ChildWorkflowExecutionId, WorkflowDispatchStatus.Faulted,
            [], diagnostics, Transport(Batch(terminal: true, failure: "AuthoredError")));

        var restored = JsonSerializer.Deserialize<DispatchWorkflowResult>(JsonSerializer.Serialize(result, Web), Web)!;

        var restoredControl = Assert.IsType<RuntimeChildControlTransport>(restored.Control);
        Assert.Equal("AuthoredError", restoredControl.Batch.AuthoredFailureCode);
        Assert.Equal(DispatchWorkflowDiagnostics.FaultedCode, restored.DiagnosticMetadata[DispatchWorkflowDiagnostics.CodeKey]);
        Assert.Equal(JsonSerializer.Serialize(diagnostics), JsonSerializer.Serialize(restored.DiagnosticMetadata));
        Assert.Equal(result.Control!.SourceOutboxItemId, restoredControl.SourceOutboxItemId);
        Assert.Equal("root-invocation", restoredControl.Batch.RootActivityExecutionId);
        Assert.Equal(3, restoredControl.Batch.Ordinal);
        Assert.Equal(["first", "second"], restoredControl.Batch.Notifications.Select(item => item.Code));

        diagnostics["authoredFailureCode"] = "AuthoredError";
        Assert.Throws<ArgumentException>(() => new DispatchWorkflowResult(Identity.ChildWorkflowExecutionId,
            WorkflowDispatchStatus.Faulted, [], diagnostics));
    }

    [Fact]
    public void Existing_constructor_and_legacy_payload_keep_control_absent()
    {
        var result = new DispatchWorkflowResult("old-child", WorkflowDispatchStatus.Faulted, [], FaultDiagnostics());
        var oldWire = JsonSerializer.Serialize(result, Web);
        Assert.False(JsonDocument.Parse(oldWire).RootElement.TryGetProperty("control", out _));

        var restored = JsonSerializer.Deserialize<DispatchWorkflowResult>(oldWire, Web)!;

        Assert.Null(restored.Control);
        Assert.Equal(WorkflowDispatchStatus.Faulted, restored.Status);
        Assert.Equal("old-child", restored.ChildWorkflowExecutionId);
        Assert.Equal(DispatchWorkflowDiagnostics.FaultedCode, restored.DiagnosticMetadata["code"]);
    }

    [Fact]
    public void Empty_terminal_barrier_is_distinct_from_an_empty_nonterminal_emission()
    {
        var terminal = new RuntimeChildControlBatch(1, "consumer", 1, Identity.ChildWorkflowExecutionId,
            "root", "source", 0, true);
        var wire = JsonSerializer.Serialize(terminal, Web);
        var restored = JsonSerializer.Deserialize<RuntimeChildControlBatch>(wire, Web)!;
        Assert.True(restored.IsTerminal);
        Assert.Empty(restored.Notifications);
        Assert.Null(restored.AuthoredFailureCode);
        Assert.Throws<ArgumentException>(() => new RuntimeChildControlBatch(1, "consumer", 1,
            Identity.ChildWorkflowExecutionId, "root", "source", 0, false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Unsupported_batch_versions_refuse_deserialization(int version)
    {
        var wire = JsonSerializer.Serialize(Batch(), Web).Replace("\"version\":1", $"\"version\":{version}", StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => JsonSerializer.Deserialize<RuntimeChildControlBatch>(wire, Web));
    }

    [Theory]
    [InlineData("ordinal")]
    [InlineData("isTerminal")]
    public void A_present_batch_cannot_default_truncated_stream_identity(string field)
    {
        var wire = JsonSerializer.SerializeToNode(Batch(), Web)!.AsObject();
        Assert.True(wire.Remove(field));
        Assert.Throws<JsonException>(() => wire.Deserialize<RuntimeChildControlBatch>(Web));
        wire[field] = null;
        Assert.Throws<JsonException>(() => wire.Deserialize<RuntimeChildControlBatch>(Web));
    }

    [Theory]
    [InlineData("artifactId")]
    [InlineData("definitionId")]
    [InlineData("definitionVersionId")]
    [InlineData("artifactVersion")]
    [InlineData("artifactHash")]
    public void Carried_pin_fields_are_nonblank_and_bounded_on_the_wire(string field)
    {
        foreach (var badValue in new string?[] { null, " ", new('x', 4097) })
        {
            var wire = JsonSerializer.SerializeToNode(Transport(Batch()), Web)!.AsObject();
            wire["parentExecutable"]![field] = badValue;
            Assert.ThrowsAny<ArgumentException>(() => wire.Deserialize<RuntimeChildControlTransport>(Web));
        }
    }

    [Fact]
    public void Payload_is_independent_of_disposed_json_and_mutated_input_list()
    {
        RuntimeChildControlBatch batch;
        using (var document = JsonDocument.Parse("{\"amount\":17}"))
        {
            var notifications = new List<RuntimeChildControlNotification> { new("code", document.RootElement) };
            batch = new RuntimeChildControlBatch(1, "consumer", 1, Identity.ChildWorkflowExecutionId,
                "root", "source", 0, false, notifications);
            notifications.Clear();
        }
        Assert.Equal(17, Assert.Single(batch.Notifications).Payload!.Value.GetProperty("amount").GetInt32());
    }

    [Fact]
    public void Notification_count_and_utf8_content_are_bounded()
    {
        Assert.Throws<ArgumentException>(() => Batch(notifications: Enumerable.Range(0, 33).Select(_ => new RuntimeChildControlNotification("code")).ToArray()));
        Assert.Throws<ArgumentException>(() => new RuntimeChildControlNotification("code", JsonSerializer.SerializeToElement(new string('界', 6000))));
        var payload = JsonSerializer.SerializeToElement(new string('x', 16000));
        Assert.Throws<ArgumentException>(() => Batch(notifications: Enumerable.Range(0, 5).Select(_ => new RuntimeChildControlNotification("code", payload)).ToArray()));
        Assert.Throws<ArgumentException>(() => new RuntimeChildControlNotification(new string('界', 400)));
        Assert.Throws<ArgumentException>(() => new RuntimeChildControlNotification("code", default(JsonElement)));
        Assert.Throws<ArgumentException>(() => Batch(notifications: [null!]));
    }

    [Fact]
    public void Classification_is_terminal_and_result_child_identity_is_exact()
    {
        Assert.Throws<ArgumentException>(() => Batch(failure: "authored"));
        Assert.Throws<ArgumentException>(() => new DispatchWorkflowResult("different-child", WorkflowDispatchStatus.Completed,
            [], null, Transport(Batch(terminal: true))));
        Assert.Throws<ArgumentException>(() => new DispatchWorkflowResult(Identity.ChildWorkflowExecutionId, WorkflowDispatchStatus.DispatchFailed,
            [], null, Transport(Batch(terminal: true, failure: "authored"))));
        Assert.Throws<ArgumentException>(() => new DispatchWorkflowResult(Identity.ChildWorkflowExecutionId, WorkflowDispatchStatus.Completed,
            [], null, Transport(Batch())));
    }

    [Fact]
    public void Source_and_dispatch_identity_cannot_be_substituted()
    {
        Assert.Throws<ArgumentException>(() => Transport(Batch(), sourceOutbox: "unrelated-outbox"));
        Assert.Throws<ArgumentException>(() => Transport(Batch(), dispatchId: "unrelated-dispatch"));
        Assert.Throws<ArgumentException>(() => Transport(new RuntimeChildControlBatch(1, "consumer", 1, "foreign-workflow",
            "root", "source", 0, true)));
        Assert.Throws<ArgumentException>(() => Transport(Batch(), sourceCommit: " "));
    }

    [Fact]
    public void Control_and_private_state_compose_in_either_order_without_changing_fault()
    {
        var fault = new ActivityFault("runtime-fault", "Original safe fault.");
        var control = Batch(terminal: true, failure: "authored");
        var state = ValueEnvelope.Inline(new ValueTypeDescriptor("test.state"), JsonSerializer.SerializeToElement(new { cursor = 1 }), ValueProtectionPolicy.InstanceInline);
        var first = RuntimeStructuralContinuation.Faulted(fault).WithControl(control).WithState(state);
        var second = RuntimeStructuralContinuation.Faulted(fault).WithState(state).WithControl(control);
        Assert.Same(fault, first.Fault);
        Assert.Same(fault, second.Fault);
        Assert.Same(control, first.Control);
        Assert.Same(control, second.Control);
        Assert.Equal(first.StateUpdate, second.StateUpdate);
        Assert.Null(RuntimeStructuralContinuation.Defer.Control);
        Assert.Throws<ArgumentException>(() => RuntimeStructuralContinuation.Defer.WithControl(control));
        Assert.Throws<ArgumentException>(() => RuntimeStructuralContinuation.Complete().WithControl(Batch()));
        Assert.Throws<ArgumentException>(() => RuntimeStructuralContinuation.Complete().WithControl(control));
    }

    [Fact]
    public void Legacy_activity_state_omits_new_control_fields_and_reads_them_as_absent()
    {
        var state = new ActivityExecutionState(new ActivityExecution("root", Identity.ChildWorkflowExecutionId,
            "root-node", "authored-root", "activity", "1.0.0"), ActivityExecutionStatus.Running, null,
            DateTimeOffset.UnixEpoch, null, null, null, null, null, null, null, [], [], 0, 0,
            new Dictionary<string, string>());
        var oldWire = JsonSerializer.Serialize(state, Web);
        using var document = JsonDocument.Parse(oldWire);
        Assert.False(document.RootElement.TryGetProperty("producedControl", out _));
        Assert.False(document.RootElement.TryGetProperty("childControl", out _));
        Assert.False(document.RootElement.TryGetProperty("producedControlSourceCommitId", out _));
        var restored = JsonSerializer.Deserialize<ActivityExecutionState>(oldWire, Web)!;
        Assert.Null(restored.ProducedControl);
        Assert.Null(restored.ChildControl);
        Assert.Null(restored.ProducedControlSourceCommitId);
        var projected = state with { ProducedControl = Batch(terminal: true), ChildControl = Transport(Batch(terminal: true)) };
        var current = JsonSerializer.Deserialize<ActivityExecutionState>(JsonSerializer.Serialize(projected, Web), Web)!;
        Assert.Equal(3, current.ProducedControl!.Ordinal);
        Assert.Equal(Identity.DispatchId, current.ChildControl!.DispatchId);
    }

    [Fact]
    public void Invalid_identity_version_and_ordinal_refuse_before_becoming_wire_data()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeChildControlBatch(1, "consumer", 0,
            Identity.ChildWorkflowExecutionId, "root", "source", 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeChildControlBatch(1, "consumer", 1,
            Identity.ChildWorkflowExecutionId, "root", "source", -1, true));
        Assert.Throws<ArgumentException>(() => new RuntimeChildControlBatch(1, "consumer", 1,
            Identity.ChildWorkflowExecutionId, " ", "source", 0, true));
        Assert.Throws<ArgumentException>(() => new RuntimeChildControlBatch(1, "consumer", 1,
            Identity.ChildWorkflowExecutionId, "root", new string('x', 4097), 0, true));
        Assert.Throws<ArgumentException>(() => new RuntimeChildControlNotification("bad\ud800"));
    }

    private static RuntimeChildControlBatch Batch(bool terminal = false, string? failure = null,
        IReadOnlyList<RuntimeChildControlNotification>? notifications = null) => new(1, "consumer", 1,
        Identity.ChildWorkflowExecutionId, "root-invocation", "source-work", 3, terminal,
        notifications ?? [new("first"), new("second")], failure);

    private static RuntimeChildControlTransport Transport(RuntimeChildControlBatch batch, string? sourceOutbox = null,
        string? dispatchId = null, string sourceCommit = "source-commit") => new(dispatchId ?? Identity.DispatchId,
        "caller-workflow", "call-invocation", "consumer-parent", Pin,
        sourceCommit, "source-intent", sourceOutbox ?? RuntimePostCommitOutboxIdentity.CreateLogicalValue(sourceCommit, "source-intent"), batch);

    private static Dictionary<string, string> FaultDiagnostics() => new()
    {
        ["code"] = DispatchWorkflowDiagnostics.FaultedCode,
        ["category"] = DispatchWorkflowDiagnostics.Category,
        ["summary"] = DispatchWorkflowDiagnostics.FaultedSummary,
        ["incidentCount"] = "0",
        ["incidentIdsTruncated"] = "false"
    };
}
