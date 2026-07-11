namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// Selects when replayable runtime checkpoints become durable within a shell.
/// </summary>
public enum CheckpointPersistenceMode
{
    /// <summary>Persist every checkpoint selected by the runtime policy as soon as it is decided.</summary>
    Immediate,

    /// <summary>Fold replayable checkpoints within a bounded drain segment and flush at a durable boundary.</summary>
    Coalesced
}
