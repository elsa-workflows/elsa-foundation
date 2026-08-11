using Elsa.Events.Core.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Enums;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Services;

/// <summary>
/// Workflow-side reconciler. Each pass publishes <see cref="WorkflowVersionsReconciling"/>
/// to gather candidate versions from source modules, then upserts the catalog. Mirrors the
/// Activities-side reconciler pattern. Full Model X hash enforcement waits on FR-016a's persisted
/// provenance/hash fields (Unit D's allocation); until they land, duplicate detection falls back to
/// the configured <see cref="DuplicateHandling"/> mode, and a same-<c>(id, version)</c>-different-content
/// case is <em>surfaced</em> by recomputing and comparing the canonical serialization of the incoming
/// and stored state (logged now; a dedicated hash-mismatch throw arrives with the persisted hash).
/// </summary>
public sealed class WorkflowsVersionReconciler(
    ILogger<WorkflowsVersionReconciler> logger,
    IInlineEventPublisher eventPublisher,
    IOptions<WorkflowVersionReconcilerOptions> options,
    IWorkflowDefinitionStore definitionStore,
    IWorkflowDefinitionVersionStore versionStore,
    IMaterializeWorkflowDefinitionCommand materializeDefinitionCommand,
    IMaterializeWorkflowDefinitionVersionCommand materializeVersionCommand,
    ISaveWorkflowDefinitionCommand saveDefinitionCommand,
    IPayloadSerializer payloadSerializer
)
    : IWorkflowVersionReconciler
{
    public async Task Reconcile(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var @event = new WorkflowVersionsReconciling();
        await eventPublisher.Publish(@event, cancellationToken);

        var reconciled = new HashSet<(string DefinitionId, string SemVerSortKey)>();

        foreach (var version in @event.Versions)
        {
            if (await ReconcileVersion(version, cancellationToken))
                reconciled.Add((version.DefinitionId, SemVer.ToSortKey(version.Version)));
        }

        // FR-008a corollary: a version skipped as outdated was never reconciled, so its claim must not
        // travel on. Publish-on-reconcile picks the latest opted-in claim per definition and publishes
        // that version row; carrying a skipped v1 while the catalog already holds v2 would move the
        // active slot backward onto the older executable. Claims pair with their version by
        // (definition, sort key) — the same identity the reconciler and the version store compare on.
        var reconciledClaims = @event.Claims
            .Where(claim => reconciled.Contains((claim.DefinitionId, claim.SemVerSortKey)))
            .ToArray();

        // Pass completed without error ⇒ notify independent subscribers (Sequential, so e.g. the
        // publish-on-reconcile step finishes inside this startup task, before readiness turns ready).
        // A failed pass throws out of the loop above and this is never published.
        await eventPublisher.Publish(new WorkflowVersionsReconciled(reconciledClaims), cancellationToken);
    }

    /// <summary>
    /// Reconciles one contributed version. Returns <c>true</c> when the version is authoritative for its
    /// definition — materialized here, or already present and verified as the current version — and
    /// <c>false</c> when it was skipped as outdated (FR-008a). The caller uses that to decide whether the
    /// version's provenance claim reaches <see cref="WorkflowVersionsReconciled"/>.
    /// </summary>
    private async Task<bool> ReconcileVersion(IWorkflowDefinitionVersion version, CancellationToken cancellationToken)
    {
        var definitionId = version.DefinitionId;
        var candidateSortKey = SemVer.ToSortKey(version.Version);

        // FR-008a: gate every catalog mutation — including the definition-metadata apply — behind the
        // outdated-version skip. Passing this check guarantees `latest is null || candidate >= latest`,
        // which IS the "apply metadata only from the newest version" gate (ADR 0034 D5 latest-wins). A
        // stale/older git entry therefore reaches nothing below and cannot overwrite current metadata.
        var latestVersion = await versionStore.FindLatestVersionAsync(definitionId, cancellationToken);
        if (latestVersion is not null && string.CompareOrdinal(candidateSortKey, latestVersion.SemVerSortKey) < 0)
        {
            LogSkipOutdated(definitionId, version.Version);
            return false;
        }

        var definition = await FindDefinition(definitionId, cancellationToken);
        if (definition is null)
        {
            // First materialized here, from a source ⇒ the source owns the definition's lifecycle
            // metadata, including the DeletedAt soft-delete flag (see UpdateDefinitionMetadata).
            var sourceOwned = WorkflowDefinition.From(version.Definition);
            sourceOwned.IsSourceOwned = true;
            await materializeDefinitionCommand.Execute(
                ReconciliationKey("definition", definitionId),
                sourceOwned,
                cancellationToken);
        }
        else
        {
            await UpdateDefinitionMetadata(
                definition,
                version.Definition,
                candidateSortKey,
                cancellationToken);
        }

        var versionExists = await VersionExists(definitionId, candidateSortKey, cancellationToken);
        if (!versionExists)
        {
            await materializeVersionCommand.Execute(
                ReconciliationKey("version", definitionId, candidateSortKey),
                WorkflowDefinitionVersion.From(version),
                cancellationToken);
            return true;
        }

        await SurfaceContentMismatch(definitionId, candidateSortKey, version, cancellationToken);
        HandleDuplicate(definitionId, version.Version);
        // Already present and not outdated ⇒ authoritative. Its claim must still travel, or an
        // unchanged source would stop republishing across restarts (publish-on-reconcile idempotency
        // relies on the claim arriving and the handler's already-published pre-check skipping it).
        return true;
    }

    /// <summary>
    /// Applies the incoming source's mutable definition-level metadata (name, description, soft-delete) to
    /// an already-persisted definition. Idempotent — writes only when a value actually changed — and never
    /// touches any <see cref="WorkflowDefinitionVersion"/>: versions are immutable and
    /// retention-authoritative, whereas name/description/<c>DeletedAt</c> are latest-wins per ADR 0034 (D5).
    /// Latest-wins soft-delete is scoped to <see cref="WorkflowDefinition.IsSourceOwned"/> definitions:
    /// a source can never flip <c>DeletedAt</c> on a catalog-authored (Studio) definition.
    /// Runs for every <see cref="Contracts.IWorkflowReconciliationSource"/>, not only git, and only for the
    /// authoritative (newest) version thanks to the caller's outdated-version gate (FR-008a).
    /// </summary>
    private async Task UpdateDefinitionMetadata(
        WorkflowDefinition persisted,
        IWorkflowDefinition incoming,
        string sourceRevision,
        CancellationToken cancellationToken)
    {
        // Reconcile soft-delete as a latest-wins flag: set when the source marks it deleted and it is
        // currently live; clear (un-delete) when the source reports it live and it is currently deleted.
        // A version row is never deleted (retention authority) — only this definition-level flag moves,
        // and only on definitions the source materialized itself (IsSourceOwned): a catalog-authored
        // definition's lifecycle belongs to the catalog, so a source's delete intent is ignored on it.
        var incomingDeleted = incoming.DeletedAt is not null;
        var persistedDeleted = persisted.DeletedAt is not null;
        var deletedChanged = incomingDeleted != persistedDeleted;

        if (deletedChanged && !persisted.IsSourceOwned)
        {
            LogIgnoredSoftDeleteOnUnownedDefinition(persisted.Id, incomingDeleted);
            deletedChanged = false;
        }

        if (persisted.Name == incoming.Name && persisted.Description == incoming.Description && !deletedChanged)
            return;

        persisted.Name = incoming.Name;
        persisted.Description = incoming.Description;
        if (deletedChanged)
            persisted.DeletedAt = incomingDeleted ? incoming.DeletedAt ?? DateTimeOffset.UtcNow : null;

        await saveDefinitionCommand.Execute(
            ReconciliationKey("definition-metadata", persisted.Id, sourceRevision),
            persisted,
            cancellationToken);
        LogMetadataUpdated(persisted.Id);
    }

    /// <summary>
    /// Model X safety net (ADR 0034 D7 tripwire, spec 085 FR-006): a same-<c>(id, version)</c> whose
    /// incoming canonical state differs from the stored one is a broken source. Until FR-016a persists a
    /// content hash to compare against, this recomputes the canonical serialization of both the incoming
    /// and stored state and <em>logs</em> a warning on divergence (a throw arrives with the persisted
    /// hash). Best-effort: a stored version whose state can't be loaded is skipped silently.
    /// </summary>
    private async Task SurfaceContentMismatch(string definitionId, string candidateSortKey, IWorkflowDefinitionVersion incoming, CancellationToken cancellationToken)
    {
        var stored = (await versionStore.ListByDefinitionAsync(definitionId, cancellationToken))
            .FirstOrDefault(v => v.SemVerSortKey == candidateSortKey);
        if (stored?.State is null || incoming.State is null)
            return;

        if (payloadSerializer.Serialize(incoming.State) != payloadSerializer.Serialize(stored.State))
            LogContentMismatch(definitionId, incoming.Version);
    }

    private void HandleDuplicate(string definitionId, string version)
    {
        switch (options.Value.DuplicateHandling)
        {
            case DuplicateHandling.Throw:
                throw new InvalidOperationException($"Workflow definition version '{definitionId}' v{version} already exists");

            case DuplicateHandling.Skip:
                LogSkipDuplicate(definitionId, version);
                break;

            default:
                break;
        }
    }

    private void LogSkipDuplicate(string definitionId, string version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Skipping duplicate workflow definition '{def}' v{v}", definitionId, version);
    }

    private void LogSkipOutdated(string definitionId, string version)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Skipping outdated workflow definition '{def}' v{v}", definitionId, version);
    }

    private void LogIgnoredSoftDeleteOnUnownedDefinition(string definitionId, bool incomingDeleted)
    {
        logger.LogWarning(
            "A reconciliation source reported workflow definition '{def}' as {state}, but the definition is not source-owned (catalog-authored). The soft-delete flag was left unchanged.",
            definitionId, incomingDeleted ? "deleted" : "live");
    }

    private void LogMetadataUpdated(string definitionId)
    {
        if (logger.IsEnabled(LogLevel.Information))
            logger.LogInformation("Updated metadata for workflow definition '{def}'", definitionId);
    }

    private void LogContentMismatch(string definitionId, string version)
    {
        // Warning, not throw: FR-016a has not yet given the version a persisted hash to enforce against.
        logger.LogWarning(
            "Workflow definition '{def}' v{v} exists with different content than the incoming source entry — a broken source (same logical identity, different content). Reconciliation left the stored version unchanged.",
            definitionId, version);
    }

    private async Task<bool> VersionExists(string definitionId, string sortKey, CancellationToken cancellationToken)
    {
        return await versionStore.ExistsAsync(definitionId, sortKey, cancellationToken);
    }

    private async Task<WorkflowDefinition?> FindDefinition(string definitionId, CancellationToken cancellationToken)
    {
        return await definitionStore.FindByIdAsync(definitionId, cancellationToken);
    }

    private static DesignOperationKey ReconciliationKey(string kind, params string[] identityParts)
    {
        var framedIdentity = string.Concat(
            identityParts.Select(part => $"{part.Length}:{part}"));
        return new DesignOperationKey($"workflow-reconciliation:{kind}:{framedIdentity}");
    }
}
