namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The persistence mode a run executes under: the mode it recorded at start, the mode its workflow chose, or the host
/// default when it chose none. Suspension, bookmark, fault and cancellation checkpoints are committed whatever the mode
/// says.
/// </summary>
/// <param name="Mode">The effective mode.</param>
/// <param name="RunHasRecord">
/// Whether the durable store already holds the run's workflow row when the drain begins. An Ephemeral run that has a
/// record always gets its completion committed, so a stored run is never left looking unfinished.
/// </param>
public sealed record ResolvedPersistenceMode(PersistenceMode Mode, bool RunHasRecord);
