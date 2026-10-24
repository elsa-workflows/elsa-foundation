namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A queued item and the instant at which its current claim or retry delay stops hiding it.</summary>
public sealed record RuntimeSchedulerWorkVisibility(RuntimeSchedulerWorkItem Item, DateTimeOffset? VisibleAfter);
