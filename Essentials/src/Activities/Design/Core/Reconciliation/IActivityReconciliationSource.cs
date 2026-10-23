using Elsa.Activities.Design.Core.Reconciliation.Models;

namespace Elsa.Activities.Design.Core.Reconciliation;

public interface IActivityReconciliationSource
{
    ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken);

    string SourceId { get; }

    string SourceKind { get; }
}
