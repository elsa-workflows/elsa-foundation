using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Reconciliation;

/// <summary>
/// The reconciliation-state sibling for refused source-owned versions: the diagnostics of the latest pass that refused
/// each version, keyed by its definition version id. A pass that publishes the version clears its entry. Replaceable;
/// the default keeps entries in memory on the node that ran the pass.
/// </summary>
public interface IActivityReconciliationDiagnosticStore
{
    void Record(string definitionVersionId, IReadOnlyList<ActivityDiagnostic> diagnostics);

    void Clear(string definitionVersionId);

    IReadOnlyList<ActivityDiagnostic> Find(string definitionVersionId);

    IReadOnlyDictionary<string, IReadOnlyList<ActivityDiagnostic>> List();
}
