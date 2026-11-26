using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Design.Reconciliation.Services;

/// <summary>
/// The id a reconciled workflow version is materialized under. It is derived from the definition id and the version's
/// sort key, the pair the catalog already treats as one version's identity, so every node reconciling the same source
/// sends the atomic writer the same materialization request under the same operation key. The node that commits second
/// then replays the first node's write instead of conflicting with it (#2189).
/// </summary>
/// <remarks>
/// The persistence scope is not an input. Every store keeps and looks up a version under its scope and its id together,
/// as it does a definition id, which comes straight from the source and already repeats across scopes. Inside one scope
/// the length-framed parts keep every definition and version apart. A version materialized before #2189 keeps the
/// generated id it was stored under, because the reconciler finds an existing version by definition and sort key and
/// never materializes it again. Nodes on different releases must derive the same id, so the format must not change.
/// </remarks>
public static class WorkflowReconciliationVersionIds
{
    private const string Prefix = "wfver-";

    public static string For(string definitionId, string semVerSortKey)
    {
        var material = $"workflow-reconciliation:version-id:{definitionId.Length}:{definitionId}{semVerSortKey.Length}:{semVerSortKey}";
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}
