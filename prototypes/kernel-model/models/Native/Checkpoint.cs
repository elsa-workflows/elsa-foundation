using System.Text.Json.Serialization;

namespace Kernel.Native;

public sealed partial class NativeState
{
    [JsonInclude, JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    internal bool CheckpointControl { get; set; }
}

internal sealed partial class Burst
{
    private bool _checkpointRequested;
    internal int RecoveryPolicy => NativeInterventions.Policy(_s);

    internal void RequestCheckpoint()
    {
        if (turn.Instance.Creation <= 0 || _s.Mode is not (Modes.Balanced or Modes.Durable))
            throw new NotSupportedException("Checkpoint request requires a qualified native invocation.");
        // The normal turn return seals its row and owed Continue; the flag itself is no commit evidence.
        _s.CheckpointControl = true;
        _checkpointRequested = true;
    }
}

public sealed partial class CompositeContext
{
    internal int RecoveryPolicy => _burst.RecoveryPolicy;
    internal void RequestCheckpoint() => _burst.RequestCheckpoint();
}
