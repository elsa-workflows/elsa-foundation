namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// What spec 181's schema finalization gate says about the Runtime EF module of this shell, for the parts of the runtime
/// that act on it without depending on persistence: placement's runnability entry names the database (spec 184,
/// FR-008), and a member whose writes to a Runtime family are refused claims and renews nothing (spec 184, FR-012).
/// </summary>
/// <remarks>
/// The Runtime EF module registers it. A shell without one, such as one whose runtime keeps its state in memory, has no
/// finalization record: its entry names no database, which applies to every one, and nothing refuses its writes.
/// </remarks>
public interface IRuntimeSchemaFinalization
{
    /// <summary>
    /// The per-database identity of the finalization record this shell's Runtime EF module last read (spec 181,
    /// FR-001), or <see langword="null"/> before it has read one.
    /// </summary>
    string? DatabaseIdentity { get; }

    /// <summary>
    /// Why every write to a family of the Runtime EF module is refused (spec 181, FR-012), or <see langword="null"/> when
    /// none is.
    /// </summary>
    string? WritesRefusedReason { get; }
}
