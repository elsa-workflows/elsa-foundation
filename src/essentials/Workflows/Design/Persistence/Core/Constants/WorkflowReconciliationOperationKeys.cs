using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Constants;

/// <summary>
/// The operation keys the workflow version reconciler dispatches its catalog writes under. They live beside the
/// persistence contracts because a permanent delete retires the materialization markers they name: a source that
/// still lists the definition would otherwise replay those markers on re-import without writing a row (#2187).
/// The definition and version formats are persisted in existing databases and must not change.
/// </summary>
public static class WorkflowReconciliationOperationKeys
{
    /// <summary>Materializes the definition row the first time a source contributes the definition.</summary>
    public static DesignOperationKey Definition(string definitionId) => Create("definition", definitionId);

    /// <summary>Materializes one immutable version, identified by its semantic-version sort key.</summary>
    public static DesignOperationKey Version(string definitionId, string semVerSortKey) =>
        Create("version", definitionId, semVerSortKey);

    /// <summary>
    /// Returns a key no earlier write used, for one write of a definition's mutable metadata. The reconciler writes
    /// only when the stored metadata differs from the source's, so that comparison, not the key, is what makes a
    /// repeated pass write nothing. A key derived from the version or from the desired metadata would meet a
    /// permanent marker on the next different change, or replay an earlier write on a change back, and
    /// reconciliation would stop converging. Markers of the earlier per-version scheme
    /// (<c>definition-metadata</c>) stay in existing databases; nothing looks them up again.
    /// </summary>
    public static DesignOperationKey DefinitionMetadataWrite(string definitionId) =>
        Create("definition-metadata-write", definitionId, Guid.NewGuid().ToString("N"));

    private static DesignOperationKey Create(string kind, params string[] identityParts)
    {
        var framedIdentity = string.Concat(identityParts.Select(part => $"{part.Length}:{part}"));
        return new DesignOperationKey($"workflow-reconciliation:{kind}:{framedIdentity}");
    }
}
