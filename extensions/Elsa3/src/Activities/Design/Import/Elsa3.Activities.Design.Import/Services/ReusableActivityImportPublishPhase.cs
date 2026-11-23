using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Microsoft.Extensions.Logging;

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
    IReusableActivityImportPublisher publisher,
    ILogger<ReusableActivityImportPublishPhase>? logger = null)
{
    /// <summary>The host's publish failure code when the publisher fails for a reason other than a refusal.</summary>
    public const string PublicationFailed = "activity.publication.failed";

    /// <summary>The publisher's code for a version below the definition's reviewed minimum.</summary>
    private const string BumpInsufficient = "activity.version.bump-insufficient";

    /// <summary>
    /// How to resume: a completed apply deletes its upload, so the engineer re-uploads the export and applies it under a
    /// new idempotency key; the commit reuses the imported rows and only the missing versions publish (FR-009).
    /// </summary>
    private const string ApplyAgain = "upload the same export again and apply it under a new idempotency key";

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
            return Collision(version, $"Elsa 3 version '{version.SourceVersionId}' now converts to other content than the earlier import of it (draft '{version.DraftId}').");

        if (await publications.FindAsync(version.Id, cancellationToken) is { } publication)
        {
            return draft is not null &&
                   StringComparer.Ordinal.Equals(publication.SourceDraftId, version.DraftId) &&
                   StringComparer.Ordinal.Equals(publication.Version, version.Version)
                ? new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.AlreadyPublished)
                : Collision(version, $"Version id '{version.Id}' is published from other content than this import's.");
        }

        if (version.DependsOn.FirstOrDefault(dependency => !published.Contains(dependency)) is { } blockedBy)
            return new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.NotPublishedDependency,
                ReusableActivityImportDiagnosticCodes.DependencyNotPublished,
                [
                    new(ReusableActivityImportDiagnosticCodes.DependencyNotPublished, ActivityDiagnosticSeverity.Error,
                        $"Version '{version.Id}' was not published because the version it uses, '{blockedBy}', was not published.",
                        Subject(version),
                        Remediation: $"Resolve why '{blockedBy}' was not published (its own state gives the reason), then {ApplyAgain}; this version is published then.")
                ],
                blockedBy);

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
                : new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.Refused, outcome.ErrorCode, ForImport(outcome.Diagnostics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One version's failure is that version's state; the versions that do not use it still publish (FR-011). The
            // cause goes to the log, not into the response.
            logger?.LogError(exception, "Publishing imported activity version {VersionId} failed unexpectedly", version.Id);
            return new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.Refused, PublicationFailed,
            [
                new(PublicationFailed, ActivityDiagnosticSeverity.Error,
                    $"Publishing the imported activity version '{version.Id}' failed unexpectedly.",
                    Subject(version),
                    Remediation: $"To retry, {ApplyAgain}. If it fails again, the host log has the cause.")
            ]);
        }
    }

    private static bool SameContent(ActivityDefinitionDraft draft, ImportedReusableActivityVersion version) =>
        StringComparer.Ordinal.Equals(draft.DefinitionId, version.DefinitionId) &&
        StringComparer.Ordinal.Equals(draft.State.Provider.ProviderKey, ReusableActivityImportDrafts.GraphProviderKey) &&
        StringComparer.Ordinal.Equals(draft.State.Provider.SchemaVersion, ReusableActivityImportDrafts.GraphProviderSchemaVersion) &&
        JsonNode.DeepEquals(JsonNode.Parse(draft.State.Provider.Payload.GetRawText()), JsonNode.Parse(version.GraphManifest.GetRawText())) &&
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(draft.State.Contract, Json), JsonSerializer.SerializeToNode(version.Contract, Json));

    /// <summary>
    /// The publisher's remediation speaks to a Studio author who picks the version. In an import the Elsa 3 version picks
    /// it, so a version below the definition's minimum gets advice the engineer can follow.
    /// </summary>
    private static IReadOnlyList<ActivityDiagnostic> ForImport(IReadOnlyList<ActivityDiagnostic> diagnostics) =>
        diagnostics.Select(diagnostic => StringComparer.Ordinal.Equals(diagnostic.Code, BumpInsufficient)
                ? diagnostic with
                {
                    Remediation = "A newer version of this activity is already published in Elsa 4, and Elsa 4 publishes a workflow's versions oldest first. " +
                                  "An older Elsa 3 version can be imported only before any newer version of the same workflow is published: " +
                                  "select it together with the newer versions in one apply, or leave it out."
                }
                : diagnostic)
            .ToArray();

    private static ReusableActivityPublicationState Collision(ImportedReusableActivityVersion version, string message) =>
        new(version.Id, version.SourceVersionId, ReusableActivityPublicationStatus.Refused, ReusableActivityImportDiagnosticCodes.IdentityCollision,
        [
            new(ReusableActivityImportDiagnosticCodes.IdentityCollision, ActivityDiagnosticSeverity.Error, message, Subject(version),
                Remediation: "This usually means the Elsa 3 workflow changed but kept its version number, and an imported Elsa 4 version cannot be replaced. " +
                             "Publish the changed workflow as a new version in Elsa 3, re-export, and import that version.")
        ]);

    private static ActivityDiagnosticSubject Subject(ImportedReusableActivityVersion version) =>
        new("ActivityDefinitionVersion", version.Id, version.DefinitionId, version.Id);
}
