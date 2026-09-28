namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// A change to a finalization record that its rules forbid, whatever else has happened to the record. Unlike a lost
/// compare-and-set, which <see cref="SchemaFinalizationWrite.Applied"/> reports so the writer can decide again, a
/// refusal is final for the record as the writer read it, and nothing was written.
/// </summary>
public sealed class SchemaFinalizationRefusedException(string family, SchemaFinalizationRefusal refusal, string message)
    : InvalidOperationException($"Schema family '{family}': {message}")
{
    public string Family { get; } = family;

    public SchemaFinalizationRefusal Refusal { get; } = refusal;
}

/// <summary>Why a finalization record refused a change.</summary>
public enum SchemaFinalizationRefusal
{
    /// <summary>The family has no record in this database yet.</summary>
    NoRecord,

    /// <summary>A version the change names, or the one the record holds, is not in the writer's chain.</summary>
    UnknownVersion,

    /// <summary>The change would not move forward along the chain. A finalized version is never lowered or repeated.</summary>
    NotForward,

    /// <summary>A hold on an already finalized version: the rollback boundary has been crossed (spec 181, FR-019).</summary>
    RollbackBoundaryCrossed,

    /// <summary>A hold applies to the version the change would finalize.</summary>
    Held,

    /// <summary>An intent is already in flight; resolve it before recording another.</summary>
    IntentInFlight,

    /// <summary>No intent is in flight to commit or abandon.</summary>
    NoIntent,

    /// <summary>A hold with the same scope is already in place.</summary>
    HoldAlreadyPlaced,

    /// <summary>No hold with that scope is in place to release.</summary>
    NoHold,

    /// <summary>A completion version later than the finalized version (spec 186, FR-014).</summary>
    CompletionBeyondFinalized,

    /// <summary>No completion stands to withdraw.</summary>
    NoCompletion
}
