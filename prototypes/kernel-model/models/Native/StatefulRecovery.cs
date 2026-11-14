using System.Text.Json.Serialization;

namespace Kernel.Native;

public sealed partial class Frame
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal bool RecoveryStateful { get; set; }
}
internal static class NativeStatefulRecovery
{
    internal static bool Present(NativeState s) => s.Frames.Values.Any(f => f.RecoveryStateful) || (s.Retries?.Values.Any(r => r.Source.RecoveryStateful) ?? false);
    internal static void Validate(NativeState s, Frame f)
    {
        if (!f.RecoveryStateful) return;
        if (NativeInterventions.Policy(s) < 3 || f.RecoveryInputs is null || f.RecoveryMaterial is null || string.IsNullOrEmpty(f.OperationKey) || f.Attempt != 0)
            throw new InvalidOperationException("Invalid native stateful recovery ownership.");
        foreach (var w in s.Agenda.Where(w => w.Resume && (w.Frame == f.Id || w.Wait is not null && s.Waits.TryGetValue(w.Wait, out var owner) && owner.Frame == f.Id)))
            if (w.Wait is null || !s.Waits.TryGetValue(w.Wait, out var wait) || w.Frame != wait.Frame || wait.Frame != f.Id)
                throw new InvalidOperationException("Invalid native qualified resume ownership.");
    }
    internal static bool SuspendedRoot(NativeState s, Frame f) => NativeInterventions.Policy(s) >= 3 && f.RecoveryStateful && f.Intervention is null &&
        NativeInterventions.RootDescriptor(f) && s.Frames.Count == 1 && s.Waits.Values.Any(w => w.Frame == f.Id) &&
        s.Agenda.All(w => w.Resume && w.Frame == f.Id && w.Wait is not null && s.Waits.TryGetValue(w.Wait, out var wait) && wait.Frame == f.Id &&
            w.Node is null && w.Parent is null && w.Tag is null && w.Scope is null && w.LexicalScope is null && w.RetrySource is null &&
            w.RecoveryInputs is null && w.RecoveryMaterial is null && w.OperationKey is null && w.Attempt == 0);
}
internal sealed partial class Burst
{
    private bool RecoverableKind(Node node) => typeof(ILeaf).IsAssignableFrom(node.Type) ||
        NativeInterventions.Policy(_s) >= 3 && !node.External && typeof(IStateful).IsAssignableFrom(node.Type);
}
