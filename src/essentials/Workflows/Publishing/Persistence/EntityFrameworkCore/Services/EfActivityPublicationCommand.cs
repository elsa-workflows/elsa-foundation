using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// Commits a reusable-activity publication across the Runtime, Activities Design and Publishing contexts as an
/// ordered, forward-converging sequence (ADR 0066) rather than one cross-context transaction.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>
/// Runtime first: the template and its source reference commit together in the Runtime context. Written
/// without the rest they are unreachable, so the partial state is inert; a retry adopts them.
/// </item>
/// <item>
/// Activities Design second, and this is the linearization point: the publication is done when that
/// transaction commits. The draft and authoring state move under their own optimistic concurrency tokens.
/// </item>
/// <item>
/// The Publishing receipt last. It is create-only and derived from the caller's own commit, so an identical
/// receipt already present is success, and a retry of the same commit resumes here instead of redoing the
/// publication.
/// </item>
/// </list>
/// Each phase runs in its own context's transaction; nothing shares a connection across contexts. The
/// sequence is not folded back into one commit when the contexts happen to share a database: every partial
/// state is already inert and resumable, so the fold-back would add a second code path without adding a
/// guarantee the ordering lacks.
/// </remarks>
public sealed class EfActivityPublicationCommand : ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>
{
    private readonly EfActivityPublicationReceiptStore receipts;
    private readonly EfActivityPublicationRuntimeCommit runtime;
    private readonly EfActivityPublicationDesignCommit design;

    public EfActivityPublicationCommand(
        EfActivityPublicationReceiptStore receipts,
        IExecutableActivityTemplateStore templates,
        IWorkflowExecutableSourceReferenceStore sourceReferences,
        IActivityDefinitionVersionPublicationStore publications,
        IPersistenceAccessContextAccessor accessContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        this.receipts = receipts;
        runtime = new EfActivityPublicationRuntimeCommit(templates, sourceReferences);
        design = new EfActivityPublicationDesignCommit(publications, accessContextAccessor);
    }

    public async Task<ActivityPublicationResult> ExecuteAsync(
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        ValidateCommit(commit);
        var receipt = commit.Receipt;

        // A durable receipt means every phase committed; this command records publications, it does not
        // replay them. The publisher answers a repeated idempotency key from the receipt store instead.
        if (await receipts.FindAsync(receipt.TenantId, receipt.IdempotencyKey, cancellationToken) is not null)
            throw new InvalidOperationException(
                $"Activity publication receipt '{receipt.IdempotencyKey}' already exists; the publication is already recorded.");

        // Interrupted between the design commit and the receipt: the publication is done, so finish it.
        if (await design.IsDraftPublicationCommittedAsync(commit.Design, cancellationToken))
            return await WriteReceiptAsync(commit, cancellationToken);

        try
        {
            // Refuse what cannot commit before writing Runtime material that would then be stranded.
            await design.EnsureDraftPublicationAdmissibleAsync(commit.Design, cancellationToken);
            await runtime.CommitAsync(commit.ExecutableTemplate, commit.SourceReference, cancellationToken);
            await design.CommitDraftPublicationAsync(commit.Design, cancellationToken);
        }
        catch (InvalidOperationException) when (!cancellationToken.IsCancellationRequested)
        {
            // A racing attempt of this same publication may have committed the design first — the draft is then
            // no longer active, or the design commit loses its concurrency race — or the provider may have
            // reported a failure after committing. Only an exact match continues; anything else is the conflict
            // it looks like.
            if (!await IsCommittedAfterFailureAsync(commit.Design, cancellationToken))
                throw;
        }

        return await WriteReceiptAsync(commit, cancellationToken);
    }

    private async Task<bool> IsCommittedAfterFailureAsync(ActivityPublicationDesignMutation mutation, CancellationToken cancellationToken)
    {
        try
        {
            return await design.IsDraftPublicationCommittedAsync(mutation, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The original failure is the truthful outcome when the design state cannot even be read back.
            return false;
        }
    }

    private async Task<ActivityPublicationResult> WriteReceiptAsync(
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit,
        CancellationToken cancellationToken)
    {
        var receipt = commit.Receipt;
        bool created;
        try
        {
            created = await receipts.TryCreateAsync(receipt, cancellationToken);
            if (!created)
            {
                var stored = await receipts.FindAsync(receipt.TenantId, receipt.IdempotencyKey, cancellationToken);
                if (stored is null || !PublishingEfJson.SameMaterial(stored, receipt))
                    throw new InvalidOperationException(
                        $"Activity publication receipt '{receipt.IdempotencyKey}' already exists with different content. Two publications are using one idempotency key.");
            }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or InvalidOperationException))
        {
            throw new ActivityPublicationReceiptPendingException(commit.Design.Publication.DefinitionVersionId, receipt.IdempotencyKey, exception);
        }

        return new(
            commit.Design.DefinitionId,
            commit.Design.Publication.DefinitionVersionId,
            commit.Design.DraftId,
            commit.ExecutableTemplate.TemplateId,
            commit.SourceReference.SourceReferenceId,
            commit.Design.Publication.PublishedAt);
    }

    /// <summary>
    /// Authoritative-material checks: the receipt must describe exactly the publication, template and
    /// source reference being committed.
    /// </summary>
    private static void ValidateCommit(ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit)
    {
        var design = commit.Design;
        var publication = design.Publication;
        var template = commit.ExecutableTemplate;
        var source = commit.SourceReference;
        var receipt = commit.Receipt;
        var expectedRequestFingerprint = ActivityPublicationRequestFingerprint.Compute(
            receipt.DraftId,
            receipt.ExpectedDraftRevision,
            receipt.ExpectedDefinitionHeadVersionId,
            receipt.RequestedVersion,
            receipt.ReviewToken);
        if (!StringComparer.Ordinal.Equals(design.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(design.CatalogVersion.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(design.CatalogVersion.Id, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(design.Layout.DefinitionVersionId, publication.DefinitionVersionId))
            throw new ArgumentException("Publication definition/version identities do not align.", nameof(commit));
        if (receipt.Status != ActivityPublicationReceiptStatus.Applied ||
            receipt.Outcome is null ||
            string.IsNullOrWhiteSpace(receipt.IdempotencyKey) ||
            !StringComparer.Ordinal.Equals(receipt.RequestFingerprint, expectedRequestFingerprint) ||
            string.IsNullOrWhiteSpace(receipt.ReviewToken) ||
            !StringComparer.Ordinal.Equals(receipt.TenantId, commit.OperationTenantId) ||
            !StringComparer.Ordinal.Equals(receipt.DraftId, design.DraftId) ||
            receipt.ExpectedDraftRevision != design.ExpectedDraftRevision ||
            !StringComparer.Ordinal.Equals(receipt.ExpectedDefinitionHeadVersionId, design.ExpectedDefinitionHeadVersionId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DefinitionVersionId, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(receipt.RequestedVersion, publication.Version) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.Version, publication.Version) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DraftId, design.DraftId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.TemplateId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.TemplateHash, template.TemplateHash) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.SourceReferenceId, source.SourceReferenceId) ||
            receipt.Outcome.PublishedAt != publication.PublishedAt ||
            receipt.UpdatedAt != publication.PublishedAt)
            throw new ArgumentException("Publication receipt does not match authoritative publication material.", nameof(commit));
        if (!StringComparer.Ordinal.Equals(publication.TemplateId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(publication.TemplateHash, template.TemplateHash) ||
            !StringComparer.Ordinal.Equals(source.ArtifactId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(source.SourceReferenceId, publication.SourceReferenceId) ||
            !StringComparer.Ordinal.Equals(source.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(source.DefinitionVersionId, publication.DefinitionVersionId))
            throw new ArgumentException("Publication, template, and Source Reference identities do not align.", nameof(commit));
        if (publication.DirectDependencyCount != design.DirectDependencies.Count ||
            template.DirectDependencies.Count != design.DirectDependencies.Count ||
            publication.ClosedTemplateCount != template.ClosedTemplates.Count ||
            publication.ResumeTargetCount != template.ResumeTargets.Count)
            throw new ArgumentException("Publication dependency summary does not match authoritative material.", nameof(commit));
        if (design.DirectDependencies.Select(x => x.OccurrenceId).Distinct(StringComparer.Ordinal).Count() != design.DirectDependencies.Count)
            throw new ArgumentException("Direct dependency occurrence ids must be unique.", nameof(commit));

        var declaredRequirements = publication.RuntimeRequirements
            .Select(x => (x.ConsumerKey, x.SchemaVersion))
            .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
        var actualRequirements = template.RuntimeRequirements
            .Select(x => (x.ConsumerKey, x.SchemaVersion))
            .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
        if (!declaredRequirements.SequenceEqual(actualRequirements))
            throw new ArgumentException("Publication Runtime requirements do not match the template.", nameof(commit));

        foreach (var edge in design.DirectDependencies)
        {
            var dependency = template.DirectDependencies.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.OccurrenceId, edge.OccurrenceId));
            if (dependency is null ||
                !StringComparer.Ordinal.Equals(dependency.DefinitionVersionId, edge.DependencyVersionId) ||
                !StringComparer.Ordinal.Equals(dependency.TemplateHash, edge.DependencyTemplateHash) ||
                !StringComparer.Ordinal.Equals(edge.OwnerVersionId, publication.DefinitionVersionId) ||
                !StringComparer.Ordinal.Equals(edge.OwnerTemplateHash, publication.TemplateHash))
                throw new ArgumentException("A dependency edge does not match the template.", nameof(commit));
        }

        if (!SemVer.TryParse(publication.Version, out var publicationVersion) ||
            !SemVer.TryParse(design.CatalogVersion.Version, out var catalogVersion) ||
            publicationVersion != catalogVersion ||
            !StringComparer.Ordinal.Equals(source.ArtifactVersion, publication.Version))
            throw new ArgumentException("Publication version labels do not align.", nameof(commit));
        if (!StringComparer.Ordinal.Equals(publication.TenantId, design.CatalogVersion.TenantId) ||
            !StringComparer.Ordinal.Equals(publication.TenantId, design.Layout.TenantId) ||
            design.DirectDependencies.Any(x => !StringComparer.Ordinal.Equals(x.TenantId, publication.TenantId)))
            throw new ArgumentException("Publication Design document tenants do not align.", nameof(commit));
    }
}

/// <summary>
/// The publication committed its design phase — it is done and observable — but its receipt could not be
/// written. Replaying the same commit resumes at the receipt. This is an <see cref="InvalidOperationException"/>
/// so a caller that maps conflicts to an unknown outcome does not record the publication as failed.
/// </summary>
public sealed class ActivityPublicationReceiptPendingException(string definitionVersionId, string idempotencyKey, Exception innerException)
    : InvalidOperationException(
        $"Activity version '{definitionVersionId}' is published, but its receipt for idempotency key '{idempotencyKey}' could not be written; replay the same commit to record it.",
        innerException)
{
    public string DefinitionVersionId { get; } = definitionVersionId;

    public string IdempotencyKey { get; } = idempotencyKey;
}
