namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableSourceReferenceStore
{
    // Owner-specific tuple inspection only. No source lifecycle/base/model/comparer changes.
    internal bool UsesModelPublicationClock(TimeProvider selected) => ReferenceEquals(timeProvider, selected);
}
