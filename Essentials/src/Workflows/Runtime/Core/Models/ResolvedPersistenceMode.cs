namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The persistence mode a run executes under: the mode its workflow chose, or the host default when it chose none.
/// Suspension, bookmark, fault, cancellation and completion checkpoints are committed whatever the mode says.
/// </summary>
/// <param name="Mode">The effective mode.</param>
public sealed record ResolvedPersistenceMode(PersistenceMode Mode);
