namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Copied direct-entry provenance on the actual protected authored model queue command.</summary>
public sealed record AuthoredModelEntry(int FormatVersion, WorkflowActivationSlot Slot, WorkflowExecutableSourceReference Source);
