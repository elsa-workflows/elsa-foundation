using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Constants;

/// <summary>
/// The operation keys the workflow version reconciler dispatches its catalog writes under. They live beside the
/// persistence contracts because a permanent delete retires every marker they name for the deleted definition: a
/// source that still lists the definition would otherwise replay its materialization markers on re-import without
/// writing a row (#2187). The formats are persisted in existing databases and must not change.
/// </summary>
public static class WorkflowReconciliationOperationKeys
{
    private const string DefinitionMetadataWriteKind = "definition-metadata-write";

    /// <summary>Materializes the definition row the first time a source contributes the definition.</summary>
    public static DesignOperationKey Definition(string definitionId) => new(Compose("definition", definitionId));

    /// <summary>Materializes one immutable version, identified by its semantic-version sort key.</summary>
    public static DesignOperationKey Version(string definitionId, string semVerSortKey) =>
        new(Compose("version", definitionId, semVerSortKey));

    /// <summary>
    /// Returns a key no earlier write used, for one write of a definition's mutable metadata. The reconciler writes
    /// only when the stored metadata differs from the source's, so that comparison, not the key, is what makes a
    /// repeated pass write nothing. A key derived from the version or from the desired metadata would meet a
    /// permanent marker on the next different change, or replay an earlier write on a change back, and
    /// reconciliation would stop converging.
    /// </summary>
    public static DesignOperationKey DefinitionMetadataWrite(string definitionId) =>
        new(Compose(DefinitionMetadataWriteKind, definitionId, Guid.NewGuid().ToString("N")));

    /// <summary>
    /// The start every <see cref="DefinitionMetadataWrite"/> key of the definition shares. The identity is framed
    /// with its length, so no other definition's keys start the same way.
    /// </summary>
    public static string DefinitionMetadataWritePrefix(string definitionId) =>
        Compose(DefinitionMetadataWriteKind, definitionId);

    /// <summary>
    /// The key metadata writes used before #2187, one per definition and latest version. Nothing writes under it any
    /// more; a permanent delete still retires the markers existing databases hold.
    /// </summary>
    public static DesignOperationKey LegacyDefinitionMetadata(string definitionId, string semVerSortKey) =>
        new(Compose("definition-metadata", definitionId, semVerSortKey));

    private static string Compose(string kind, params string[] identityParts) =>
        $"workflow-reconciliation:{kind}:{string.Concat(identityParts.Select(part => $"{part.Length}:{part}"))}";
}
