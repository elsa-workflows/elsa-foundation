using Elsa.Activities.Design.Reconciliation.Models;

namespace Elsa.Activities.Design.Reconciliation.Contracts;

public interface IActivityReconciliationSource
{
    ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken);

    string SourceId { get; }

    string SourceKind { get; }
}
