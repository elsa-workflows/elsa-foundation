using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;

namespace Elsa3.Activities.Design.Import.Services;

/// <summary>
/// Publishes the imported reusable versions after the import commit (spec 246 D-1, R-5): in the mutation's order, so
/// dependencies come first and the versions of one definition ascend, each from its draft through the host's publish
/// path, under the plan's version id. Every version gets a state; a refused version stops only the versions that use it.
/// Running it again over the same versions publishes only what is still missing.
/// </summary>
public sealed class ReusableActivityImportPublishPhase(
    IActivityDefinitionVersionPublicationStore publications,
    IActivityDefinitionAuthoringStore authoring,
    IActivityDefinitionDraftStore drafts,
    ICreateActivityDraftCommand createDraft,
    IReusableActivityImportPublisher publisher)
{
    /// <summary>The host's publish failure code when the publisher fails for a reason other than a refusal.</summary>
    public const string PublicationFailed = "activity.publication.failed";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="operationKey">Names this import operation in the publisher's idempotency keys: the receipt id.</param>
    public async ValueTask<IReadOnlyList<ReusableActivityPublicationState>> PublishAsync(
        IReadOnlyList<ImportedReusableActivity> activities,
        string operationKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activities);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        var states = new List<ReusableActivityPublicationState>(activities.Count);
        var published = new HashSet<string>(StringComparer.Ordinal);
        foreach (var version in activities.Select(activity => activity.Version))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = await PublishVersionAsync(version, published, operationKey, cancellationToken);
            if (state.Status is ReusableActivityPublicationStatus.Published or ReusableActivityPublicationStatus.AlreadyPublished)
                published.Add(version.Id);
            states.Add(state);
        }

        return states;
    }

    private async Task<ReusableActivityPublicationState> PublishVersionAsync(
        ImportedReusableActivityVersion version,
        IReadOnlySet<string> published,
        string operationKey,
        CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(version.DraftId, cancellationToken);
        if (draft is not null && !SameContent(draft, version))
            return Collision(version, $"Draft '{version.DraftId}' holds other content than the Elsa 3 source version '{version.SourceVersionId}' converts to.");

        if (await publications.FindAsync(version.Id, cancellationToken) is { } publication)
        {
            return draft is not null &&
                   StringComparer.Ordinal.Equals(publication.SourceDraftId, version.DraftId) &&
                   StringComparer.Ordinal.Equals(publication.Version, version.Version)
                ? new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.AlreadyPublished)
                : Collision(version, $"Version id '{version.Id}' is published from other content than this import's.");
        }

        if (version.DependsOn.FirstOrDefault(dependency => !published.Contains(dependency)) is { } blockedBy)
            return new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.NotPublishedDependency, BlockedBy: blockedBy);

        try
        {
            var head = (await authoring.FindAsync(version.DefinitionId, cancellationToken))?.HeadVersionId;
            if (draft is null)
            {
                draft = ReusableActivityImportDrafts.Draft(version, head);
                await createDraft.ExecuteAsync(new(draft, ReusableActivityImportDrafts.Layout(version), head), cancellationToken);
            }

            var outcome = await publisher.PublishAsync(
                new(draft.Id, draft.Revision, head, version.Version, version.Id, $"elsa3-import:{operationKey}:{version.Id}", version.TenantId),
                cancellationToken);
            return outcome.Published
                ? new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.Published)
                : new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.Refused, outcome.ErrorCode, outcome.Diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One version's failure is that version's state; the versions that do not use it still publish (FR-011).
            return new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.Refused, PublicationFailed,
            [
                new(PublicationFailed, ActivityDiagnosticSeverity.Error,
                    $"Publishing the imported activity version failed: {exception.Message}",
                    Subject(version))
            ]);
        }
    }

    private static bool SameContent(ActivityDefinitionDraft draft, ImportedReusableActivityVersion version) =>
        StringComparer.Ordinal.Equals(draft.DefinitionId, version.DefinitionId) &&
        StringComparer.Ordinal.Equals(draft.State.Provider.ProviderKey, ReusableActivityImportDrafts.GraphProviderKey) &&
        StringComparer.Ordinal.Equals(draft.State.Provider.SchemaVersion, ReusableActivityImportDrafts.GraphProviderSchemaVersion) &&
        JsonNode.DeepEquals(JsonNode.Parse(draft.State.Provider.Payload.GetRawText()), JsonNode.Parse(version.GraphManifest.GetRawText())) &&
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(draft.State.Contract, Json), JsonSerializer.SerializeToNode(version.Contract, Json));

    private static ReusableActivityPublicationState Collision(ImportedReusableActivityVersion version, string message) =>
        new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.Refused, ReusableActivityImportDiagnosticCodes.IdentityCollision,
        [
            new(ReusableActivityImportDiagnosticCodes.IdentityCollision, ActivityDiagnosticSeverity.Error, message, Subject(version),
                Remediation: "Import the changed Elsa 3 workflow as a new version, or remove the conflicting Elsa 4 version first.")
        ]);

    private static ActivityDiagnosticSubject Subject(ImportedReusableActivityVersion version) =>
        new("ActivityDefinitionVersion", version.Id, version.DefinitionId, version.Id);
}
