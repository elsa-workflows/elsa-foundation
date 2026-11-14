using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kernel.Native;

// Private, intervention-only profile. It does not yet expose operator retry or product incident strategy APIs.
internal interface IInterventionParent;
internal sealed record NativeIntervention(string Id, ActivityFault Fault, string Outcome = "WaitForIntervention", int Version = 1);

public sealed partial class NativeState
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal bool InterventionEnabled { get; set; }
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
    internal static bool Present(NativeState state) => state.InterventionEnabled || state.Frames.Values.Any(f =>
        f.Intervention is not null || f.RecoveryInputs is not null || f.OperationKey is not null);

    internal static void Validate(NativeState state)
    {
        if (!state.InterventionEnabled)
        {
            if (Present(state)) throw new InvalidOperationException("Native recovery data requires its opted-in profile.");
            return;
        }
        if (!state.Started || state.Mode is not (Modes.Balanced or Modes.Durable))
            throw new InvalidOperationException("Invalid native intervention profile state.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in state.Frames.Values)
        {
            if ((f.RecoveryInputs is null) != (f.OperationKey is null) || f.OperationKey is { Length: 0 })
                throw new InvalidOperationException("Invalid native pinned-input ownership.");
            if (f.Intervention is not { } incident) continue;
            if (incident.Version != 1) throw new NotSupportedException("Unsupported native intervention version.");
            if (string.IsNullOrEmpty(incident.Id) || !ids.Add(incident.Id) || incident.Outcome != "WaitForIntervention" ||
                incident.Fault is null || string.IsNullOrEmpty(incident.Fault.Code) || incident.Fault.Message is null ||
                f.RecoveryInputs is null || f.Parent is null || !state.Frames.TryGetValue(f.Parent, out var parent) || parent.Intervention is not null ||
                state.Waits.Values.Any(w => w.Frame == f.Id) || state.Frames.Values.Any(child => child.Parent == f.Id))
                throw new InvalidOperationException("Invalid native intervention ownership.");
        }
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
    }

    private bool InterventionParent(Frame frame) => frame.Parent is { } id && _s.Frames.TryGetValue(id, out var parent) &&
        parent.Intervention is null && typeof(IInterventionParent).IsAssignableFrom(_wf.Nodes[parent.Node].Type);

    private JsonObject? PinRecoveryInputs(Frame frame, Node node, IScope scope)
    {
        // Existing Snapshot/Create owns binding materialization. The bounded profile requires every input bound;
        // CLR constructor defaults and activation failures are not silently promoted to pinned recovery material.
        if (!_s.InterventionEnabled || !typeof(ILeaf).IsAssignableFrom(node.Type) || node.Background || !InterventionParent(frame) ||
            Hydrator.Of(node.Type).Any(p => !node.Inputs.ContainsKey(p.Name))) return null;
        var snapshot = Hydrator.Snapshot(node, b => Evaluate(b, scope));
        frame.RecoveryInputs = (JsonObject)snapshot.DeepClone();
        frame.OperationKey = CreationProtocol.Origin(turn.Instance.Ref.Id, turn.Instance.Creation) + "/activity/" + frame.Id;
        return snapshot;
    }

    private bool CaptureIntervention(Frame frame, ActivityFault fault)
    {
        if (!_s.InterventionEnabled || frame.RecoveryInputs is null || !InterventionParent(frame)) return false;
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
