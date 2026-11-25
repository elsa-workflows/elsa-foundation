using System.Text.Json.Nodes;

namespace Kernel.Native;

internal sealed partial class Burst
{
    /// <summary>The workflow intrinsics besides Set Variable; false for any other activity.</summary>
    private bool Intrinsic(Frame frame, ActivityBase activity)
    {
        switch (activity)
        {
            case SetOutput output:
                Output(frame, output.Name, output.Value);
                return true;
            case SetCorrelationId correlation:
                Label(frame, NativeLabels.CorrelationId, correlation.Value);
                return true;
            case SetInstanceName name:
                Label(frame, NativeLabels.InstanceName, name.Value);
                return true;
            case Kernel.Native.Finish finish:
                turn.Record($"c:{frame.Id}:{frame.Node}");
                turn.Record($"finish:{frame.Node}:{finish.Outcome}");
                Finish();
                return true;
            default:
                return false;
        }
    }

    // A workflow output is a field of the workflow's output object; inside a GraphActivity's body, an output of the boundary.
    private void Output(Frame frame, string name, JsonNode? value)
    {
        if (Boundary(frame) is { } boundary)
        {
            var graph = _wf.Nodes[boundary.Node].Graph!;
            if (graph.Outputs.FirstOrDefault(o => o.Name == name) is not { Value: null })
            {
                Fail(frame, new ActivityFault("activity.graph.output-unassignable", $"Set Output '{frame.Node}' names '{name}', which GraphActivity '{boundary.Node}' does not declare as an output to assign."));
                return;
            }
            var state = Values.To<GraphState>(boundary.State)!;
            var assigned = new Dictionary<string, JsonNode?>(state.Assigned ?? [], StringComparer.Ordinal) { [name] = value?.DeepClone() };
            boundary.State = Values.From(state with { Assigned = assigned });
        }
        else
        {
            var output = _s.Variables.GetValueOrDefault("output") ?? new JsonObject();
            if (output is not JsonObject fields)
            {
                Fail(frame, new ActivityFault("workflow.output.conflict", $"Set Output '{frame.Node}' names a field of the workflow's output, which holds a value that is not an object."));
                return;
            }
            fields[name] = value?.DeepClone();
            _s.Variables["output"] = fields;
        }
        Complete(frame, "Done", null);
    }

    private void Label(Frame frame, string label, JsonNode? value)
    {
        string? text = null;
        if (value is not null && !(value is JsonValue scalar && scalar.GetValueKind() == System.Text.Json.JsonValueKind.String && scalar.TryGetValue(out text)))
        {
            Fail(frame, new ActivityFault("workflow.label.invalid", $"'{frame.Node}' sets {label} to a value that is not a string."));
            return;
        }
        // A blank value clears the label, as the old core reads a blank correlation id or name as none.
        try { turn.SetLabel(label, string.IsNullOrWhiteSpace(text) ? null : text); }
        catch (Exception refused) when (refused is ArgumentException or NotSupportedException)
        {
            Fail(frame, new ActivityFault("workflow.label.refused", refused.Message));
            return;
        }
        Complete(frame, "Done", null);
    }

    /// <summary>The nearest GraphActivity whose body this frame is in.</summary>
    private Frame? Boundary(Frame frame)
    {
        for (var parent = ParentOf(frame); parent is not null; parent = ParentOf(parent))
            if (_wf.Nodes[parent.Node].Type == typeof(GraphActivity)) return parent;
        return null;
    }
}
