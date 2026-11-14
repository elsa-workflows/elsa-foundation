using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kernel.Native;

// Private opt-in recovery profile; no product incident strategy or operator API.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class InterventionParentAttribute(int version) : Attribute
{
    internal int Version { get; } = version;
}
internal interface IInterventionParent
{
    JsonNode? PrepareRetry(Node definition, JsonNode? state, RetryChild child);
}
internal sealed record NativeIntervention(string Id, ActivityFault Fault, string Outcome = "WaitForIntervention", int Version = 1);

public sealed partial class NativeState
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal bool InterventionEnabled { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal int InterventionPolicy { get; set; } // absent/zero is the v5/v6 child-only policy, never latest-host capability
}
public sealed partial class Frame
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal NativeIntervention? Intervention { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal JsonObject? RecoveryInputs { get; set; }
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    internal string? OperationKey { get; set; }
}

internal static class NativeInterventions
{
    internal static int Policy(NativeState state) => state.InterventionEnabled ? state.InterventionPolicy == 0 ? 1 : state.InterventionPolicy : 0;
    internal static bool Present(NativeState state) => state.InterventionEnabled || state.InterventionPolicy != 0 || NativeRetries.Present(state) || state.Frames.Values.Any(f =>
        f.Intervention is not null || f.RecoveryInputs is not null || f.OperationKey is not null);
    internal static bool RootShape(NativeState state, Frame frame) => Policy(state) >= 2 && frame.Parent is null && frame.Tag is null && frame.Scope is null && frame.LexicalScope is null &&
        !state.Frames.Values.Any(f => f.Id != frame.Id) && state.Agenda.Count == 0;
    internal static bool Root(NativeState state, Frame frame) => RootShape(state, frame) && frame.RecoveryMaterial?["root"] is JsonValue value &&
        value.TryGetValue<string>(out var node) && node == frame.Node;

    internal static void Validate(NativeState state, bool cancelled = false)
    {
        if (!state.InterventionEnabled)
        {
            if (Present(state)) throw new InvalidOperationException("Native recovery data requires its opted-in profile.");
            return;
        }
        if (!state.Started || state.Mode is not (Modes.Balanced or Modes.Durable))
            throw new InvalidOperationException("Invalid native intervention profile state.");
        if (state.InterventionPolicy is < 0 or > 2) throw new NotSupportedException("Unsupported native intervention policy.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in state.Frames.Values)
        {
            if ((f.RecoveryInputs is null) != (f.OperationKey is null) || f.OperationKey is { Length: 0 })
                throw new InvalidOperationException("Invalid native pinned-input ownership.");
            if (f.Intervention is not { } incident) continue;
            if (incident.Version != 1) throw new NotSupportedException("Unsupported native intervention version.");
            if (string.IsNullOrEmpty(incident.Id) || !ids.Add(incident.Id) || incident.Outcome != "WaitForIntervention" ||
                incident.Fault is null || string.IsNullOrEmpty(incident.Fault.Code) || incident.Fault.Message is null ||
                f.RecoveryInputs is null || (f.Parent is null ? !Root(state, f) : !state.Frames.TryGetValue(f.Parent, out var parent) || parent.Intervention is not null) ||
                state.Waits.Values.Any(w => w.Frame == f.Id) || state.Frames.Values.Any(child => child.Parent == f.Id))
                throw new InvalidOperationException("Invalid native intervention ownership.");
        }
        NativeRetries.Validate(state, cancelled);
    }
}

internal sealed partial class Burst
{
    private void InitializeIntervention()
    {
        if (!_wf.InterventionRequested) return;
        if (turn.Instance.Creation <= 0 || _s.Mode is not (Modes.Balanced or Modes.Durable))
            throw new NotSupportedException("Native intervention requires a qualified Balanced/Durable invocation.");
        _s.InterventionEnabled = true;
        _s.InterventionPolicy = 2;
    }

    private bool InterventionParent(Frame frame) => frame.Parent is { } id && _s.Frames.TryGetValue(id, out var parent) &&
        parent.Intervention is null && typeof(IInterventionParent).IsAssignableFrom(_wf.Nodes[parent.Node].Type) &&
        _wf.Nodes[parent.Node].Type.GetCustomAttribute<InterventionParentAttribute>() is { Version: >= 1 } policy && policy.Version <= NativeInterventions.Policy(_s);
    private bool InterventionBoundary(Frame frame) => frame.Parent is null
        ? NativeInterventions.RootShape(_s, frame) && frame.Node == _wf.Root.Id : InterventionParent(frame);

    private JsonObject? PinRecoveryInputs(Frame frame, Node node, IScope scope)
    {
        // Existing Snapshot/Create owns binding materialization. The bounded profile requires every input bound;
        // CLR constructor defaults and activation failures are not silently promoted to pinned recovery material.
        if (!_s.InterventionEnabled || !typeof(ILeaf).IsAssignableFrom(node.Type) || node.Background || !InterventionBoundary(frame) ||
            Hydrator.Of(node.Type).Any(p => !node.Inputs.ContainsKey(p.Name))) return null;
        var snapshot = Hydrator.Snapshot(node, b => Evaluate(b, scope));
        frame.RecoveryMaterial = NativeRecoveryMaterial.Describe(_wf);
        frame.RecoveryInputs = (JsonObject)snapshot.DeepClone();
        frame.OperationKey = CreationProtocol.Origin(turn.Instance.Ref.Id, turn.Instance.Creation) + "/activity/" + frame.Id;
        return snapshot;
    }

    private bool CaptureIntervention(Frame frame, ActivityFault fault)
    {
        if (!_s.InterventionEnabled || frame.RecoveryInputs is null || !(frame.Parent is null ? NativeInterventions.Root(_s, frame) : InterventionParent(frame))) return false;
        CloseInbox(frame.Id, "faulted");
        frame.Intervention = new(NewId("i"), fault);
        _s.Frames.Add(frame.Id, frame);
        turn.Record($"f:{frame.Id}:{frame.Node}");
        turn.Record($"intervention:{frame.Id}:{frame.Intervention.Id}");
        return true;
    }

    private bool Blocked(string? frame)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (frame is not null && _s.Frames.TryGetValue(frame, out var value))
        {
            if (!seen.Add(frame)) throw new InvalidOperationException("Native activity ancestry is cyclic.");
            if (value.Intervention is not null) return true;
            frame = value.Parent;
        }
        return false;
    }
    private bool Runnable(Work work) => !Blocked(work.Frame) && !Blocked(work.Parent);
}
