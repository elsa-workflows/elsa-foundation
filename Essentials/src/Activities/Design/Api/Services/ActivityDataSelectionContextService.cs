using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Runtime.Core.Serialization;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Actor-gated request-only Activity Definition analysis; it owns no mutation or event port.</summary>
internal sealed class ActivityDataSelectionContextService(
    IActivityDefinitionDraftStore drafts,
    IActivityDefinitionAuthoringStore authoring,
    IActivityAuthoringContextAsync actor,
    IEnumerable<IActivityAuthoringCatalog> catalogs,
    IEnumerable<IActivityDataSelectionDocumentProjector> projectors,
    IEnumerable<IActivityDataSelectionAnalyzer> analyzers,
    IActivityStructureService structures)
{
    internal async Task<ActivityDataSelectionContextResponse> ResolveAsync(ActivityDataSelectionContextRequest request, CancellationToken cancellationToken)
    {
        string? revision = null, snapshotHash = null;
        ActivityDataSelectionContextResponse Reply(string state, string? code, ActivityDataSelectionAnalysis? analysis = null, string? context = null) =>
            new("1", state, code, "ActivityDefinition", request.DraftId, revision, request.DocumentRevision, snapshotHash, context, analysis);

        if (request.ContractVersion != "1") return Reply("incompatible", "activity-data.contract-incompatible");
        if (string.IsNullOrWhiteSpace(request.DraftId) || request.DraftId.Length > 1024 || string.IsNullOrWhiteSpace(request.DocumentRevision) || request.DocumentRevision.Length > 256 ||
            request.Target is null || string.IsNullOrWhiteSpace(request.Target.NodeId) || request.Target.NodeId.Length > 1024 ||
            string.IsNullOrWhiteSpace(request.Target.InputKey) || request.Target.InputKey.Length > 1024 || request.SnapshotHash is { Length: > 256 } ||
            request.ExpectedBaseRevision < 0 || !Bounded(request.Query))
            return Reply("invalid", "activity-data.request-invalid");
        try
        {
            var catalogOwners = catalogs.ToArray();
            if (catalogOwners.Length != 1) return Reply("unavailable", "activity-data.catalog-unavailable");
            var catalog = catalogOwners[0];
            if (!await catalog.CanReadAsync(cancellationToken)) return Reply("denied", "activity-data.denied");
            var draft = await drafts.FindAsync(request.DraftId, cancellationToken);
            if (draft is null || draft.Id != request.DraftId || !Visible(draft.TenantId)) return Reply("unavailable", "activity-data.resource-unavailable");
            // Stores may return the same tracked object on both reads; retain lifecycle values before awaiting.
            var initialRevision = draft.Revision;
            var initialStatus = draft.Status;
            if (!await actor.CanReadProviderPayloadAsync(draft.State.Provider.ProviderKey, cancellationToken))
                return Reply("denied", "activity-data.denied");
            var ownership = await authoring.FindAsync(draft.DefinitionId, cancellationToken);
            if (ownership is null || ownership.DefinitionId != draft.DefinitionId || !Visible(ownership.TenantId)) return Reply("unavailable", "activity-data.resource-unavailable");
            revision = initialRevision.ToString(CultureInfo.InvariantCulture);
            if (initialRevision != request.ExpectedBaseRevision) return Reply("stale", "activity-data.base-stale");
            var state = request.CurrentSnapshot is { } proposed ? ActivityDataSelectionSnapshot.Read(proposed) : draft.State;
            if (!StringComparer.Ordinal.Equals(state.Provider.ProviderKey, draft.State.Provider.ProviderKey) ||
                !StringComparer.Ordinal.Equals(state.Provider.SchemaVersion, draft.State.Provider.SchemaVersion) ||
                !StringComparer.Ordinal.Equals(state.Contract.ContractSchemaVersion, draft.State.Contract.ContractSchemaVersion))
                return Reply("incompatible", "activity-data.provider-incompatible");
            snapshotHash = ActivityDataSelectionSnapshot.Hash(state);
            var savedHash = ActivityDataSelectionSnapshot.Hash(draft.State);
            if (request.CurrentSnapshot is not null && snapshotHash != savedHash &&
                (initialStatus != ActivityDefinitionDraftStatus.Active || ownership.ContentAuthority.Kind != ActivityContentAuthorityKind.Design ||
                 !await actor.CanAuthorProviderAsync(state.Provider.ProviderKey, cancellationToken) ||
                 !await actor.CanManageActivityDefinitionsAsync(cancellationToken)))
            {
                snapshotHash = null;
                return Reply("denied", "activity-data.read-only");
            }
            if (request.SnapshotHash is not null && !StringComparer.Ordinal.Equals(request.SnapshotHash, snapshotHash))
                return Reply("stale", "activity-data.snapshot-stale");
            var catalogSnapshot = await catalog.ListAsync(cancellationToken);
            if (!catalogSnapshot.Allowed) { snapshotHash = null; revision = null; return Reply("denied", "activity-data.denied"); }
            var catalogRevision = ActivityDataSelectionSnapshot.Hash(catalogSnapshot);
            var authorities = analyzers.ToArray();
            if (authorities.Length != 1) return Reply("unavailable", "activity-data.analysis-unavailable");
            ActivityDataSelectionDocument? document = null;
            foreach (var projector in projectors)
            {
                var candidate = await projector.TryProjectAsync(state.Provider, state.Contract, draft.Id, snapshotHash, cancellationToken);
                if (candidate is null) continue;
                if (document is not null) return Reply("unavailable", "activity-data.provider-ambiguous");
                if (candidate.ResourceId != draft.Id || candidate.ContractRevision != snapshotHash)
                    return Reply("unavailable", "activity-data.provider-invalid");
                document = candidate;
            }
            if (document is null) return Reply("unavailable", "activity-data.provider-unavailable");
            var visibleVersions = catalogSnapshot.Entries.Select(x => x.ActivityVersionId).ToHashSet(StringComparer.Ordinal);
            if (!CatalogContains(document.RootActivity, visibleVersions, cancellationToken))
                return Reply("denied", "activity-data.catalog-denied");
            var analysis = await authorities[0].AnalyzeAsync(document, request.Target, request.Query, cancellationToken);
            if (analysis.ContractRevision != snapshotHash) return Reply("unavailable", "activity-data.analysis-invalid");
            // Recheck after asynchronous metadata work; browsing never refreshes or writes the draft.
            var latest = await drafts.FindAsync(draft.Id, cancellationToken);
            if (latest is null || latest.Id != draft.Id || !Visible(latest.TenantId) || latest.Revision != initialRevision ||
                latest.Status != initialStatus || ActivityDataSelectionSnapshot.Hash(latest.State) != savedHash)
                return Reply("stale", "activity-data.base-stale");
            if (!await actor.CanReadProviderPayloadAsync(state.Provider.ProviderKey, cancellationToken))
            { snapshotHash = null; revision = null; return Reply("denied", "activity-data.denied"); }
            var latestCatalog = await catalog.ListAsync(cancellationToken);
            if (!latestCatalog.Allowed)
            { snapshotHash = null; revision = null; return Reply("denied", "activity-data.denied"); }
            if (ActivityDataSelectionSnapshot.Hash(latestCatalog) != catalogRevision)
                return Reply("stale", "activity-data.catalog-stale");
            var context = ActivityDataSelectionSnapshot.Hash(new ActivityDataSelectionContextIdentity(
                draft.Id, initialRevision, request.DocumentRevision, snapshotHash, request.Target, request.Query,
                actor.ActorId, actor.TenantId, await actor.GetAuthorizationProfileAsync(cancellationToken),
                catalogSnapshot, ActivityValueJsonProfile.Revision, analysis) { DraftStatus = initialStatus });
            var invalid = analysis.Findings.Any(x => x.Severity == ValidationSeverities.Error);
            return Reply(invalid ? "invalid" : analysis.Sources.Length == 0 ? "empty" : "supported",
                invalid ? "activity-data.selection-invalid" : null, analysis, context);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ActivityDataSelectionRejectedException rejected)
        {
            return Reply("invalid", "activity-data.document-invalid", snapshotHash is null ? null :
                new ActivityDataSelectionAnalysis(snapshotHash, null, [], rejected.Findings));
        }
        catch (JsonException) { return Reply("invalid", "activity-data.snapshot-invalid"); }
        catch (ArgumentException) { return Reply("invalid", "activity-data.request-invalid"); }
        catch (Exception) { return Reply("unavailable", "activity-data.analysis-unavailable"); }
    }

    private bool Visible(string? tenant) => tenant is null || StringComparer.Ordinal.Equals(tenant, actor.TenantId);

    private bool CatalogContains(ActivityNode? root, IReadOnlySet<string> visibleVersions, CancellationToken cancellationToken)
    {
        if (root is null) return true;
        var pending = new Stack<(ActivityNode Node, int Depth)>();
        pending.Push((root, 0));
        var count = 0;
        while (pending.TryPop(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > 10000 || item.Depth > 100) return false;
            if (item.Node.Intrinsic is null && !visibleVersions.Contains(item.Node.ActivityVersionId)) return false;
            // Only the installed structure owner can expose children; opaque JSON is never walked.
            if (item.Node.Structure is { } structure && !structures.HasHandler(structure)) return false;
            foreach (var child in structures.ProjectChildren(item.Node).SelectMany(x => x.Activities))
                pending.Push((child, item.Depth + 1));
        }
        return true;
    }

    private static bool Bounded(ActivityDataSelectionQuery? query) => query is null ||
        query.Skip >= 0 && query.Take is >= 1 and <= 200 && (query.Search is null || query.Search.Length <= 256) &&
        !query.MemberPath.IsDefault && query.MemberPath.Length <= 128 && query.MemberPath.All(x => x is not null && x.Length <= 1024) &&
        query.ShapeLimits is { MaximumDepth: >= 0 and <= 32, MaximumMembers: >= 0 and <= 4096 };
}
