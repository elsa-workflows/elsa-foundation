namespace Elsa.Activities.Design.Core.Models;

public enum ActivityAvailabilityDiagnosticState
{
    Available,
    BlockedByHostBaseline,
    HiddenByManagementSettings,
    RemovedFromCatalog,
    UnresolvedReference
}
