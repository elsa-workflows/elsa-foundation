using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Reconciliation;

/// <summary>
/// The reconciliation-state sibling for refused source-owned versions: the versions the latest complete pass refused, each
/// with its diagnostics, keyed by definition version id. The reconciler clears an entry when its version publishes and, at
/// the end of a complete pass, every entry that pass did not refuse. Replaceable; the default keeps entries in memory on
/// the node that ran the pass.
/// </summary>
public interface IActivityReconciliationDiagnosticStore
{
    void Record(string definitionVersionId, IReadOnlyList<ActivityDiagnostic> diagnostics);

    void Clear(string definitionVersionId);

    IReadOnlyList<ActivityDiagnostic> Find(string definitionVersionId);

    IReadOnlyDictionary<string, IReadOnlyList<ActivityDiagnostic>> List();
}
