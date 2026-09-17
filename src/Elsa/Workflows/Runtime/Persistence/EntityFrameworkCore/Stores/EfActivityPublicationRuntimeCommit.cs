using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// The Runtime half of a reusable-activity publication: the executable activity template and the source
/// reference that retains it, committed together in one Runtime-context transaction (ADR 0066, phase one).
/// </summary>
/// <remarks>
/// The two are idempotent in different ways, and the difference is what keeps an interrupted publication
/// finishable. The template is content-addressed, so an identical one already present needs nothing. The
/// source reference is create-only, so a naive repeat would conflict on it; a live reference carrying this
/// publication's own artifact is therefore recognised as phase one already done, while one bound to a different
/// artifact remains a conflict. Both writes go through the Runtime artifact stores' own staging, so the rows are
/// exactly the ones those stores read back.
/// </remarks>
public sealed class EfActivityPublicationRuntimeCommit
{
    // A racing publication committed first. The next attempt re-reads the winner: the same material is adopted,
    // anything else surfaces as a conflict.
    private static readonly EfWriteRetry Commits = new(
        EfWriteRetry.DefaultMaxAttempts,
        exception => exception is DbUpdateException && EfRelationalExceptionClassifier.IsWriteConflict(exception, EfWriteConflict.UniqueKey | EfWriteConflict.Transient));
    private readonly BookmarkStateDbContext context;
    private readonly EfExecutableActivityTemplateStore templates;
    private readonly EfWorkflowExecutableSourceReferenceStore sourceReferences;

    public EfActivityPublicationRuntimeCommit(
        IExecutableActivityTemplateStore templates,
        IWorkflowExecutableSourceReferenceStore sourceReferences)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(sourceReferences);
        // Writing template rows into this context while reads resolve another backend would misfile the
        // publication silently, so a composition that is not EF for both artifact kinds is refused here.
        this.templates = templates as EfExecutableActivityTemplateStore ?? throw new InvalidOperationException(
            $"EF activity publication requires the EF Runtime executable-template store, but '{templates.GetType().FullName}' is registered.");
        this.sourceReferences = sourceReferences as EfWorkflowExecutableSourceReferenceStore ?? throw new InvalidOperationException(
            $"EF activity publication requires the EF Runtime source-reference store, but '{sourceReferences.GetType().FullName}' is registered.");
        if (!ReferenceEquals(this.templates.Context, this.sourceReferences.Context))
            throw new InvalidOperationException("The Runtime template and source-reference stores must share one context to commit together.");
        context = this.templates.Context;
    }

    /// <summary>
    /// Commits <paramref name="template"/> and <paramref name="reference"/> atomically.
    /// </summary>
    /// <returns><c>true</c> when this call wrote anything; <c>false</c> when phase one was already done.</returns>
    /// <exception cref="InvalidOperationException">
    /// The template id or hash is bound to different content, or the source reference id is bound to different
    /// material.
    /// </exception>
    public async ValueTask<bool> CommitAsync(
        ExecutableActivityTemplate template,
        WorkflowExecutableSourceReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(reference.ArtifactId, template.TemplateId))
            throw new ArgumentException("A publication's source reference must retain the template it is published with.", nameof(reference));

        return await Commits.RunAsync(context, async () =>
        {
            context.ChangeTracker.Clear();
            try
            {
                await using var transaction = await RuntimeArtifactEfPersistenceBoundary.QueryAsync(
                    context, "publishing", template.TemplateId, () => context.Database.BeginTransactionAsync(cancellationToken));
                var createsTemplate = await templates.StageCreateAsync(template, cancellationToken);
                var createsReference = await sourceReferences.StageCreateOrAdoptAsync(reference, cancellationToken);
                if (createsTemplate || createsReference)
                    await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                        context, "publishing", template.TemplateId, () => context.SaveChangesAsync(cancellationToken));
                await RuntimeArtifactEfPersistenceBoundary.ExecuteAsync(
                    context, "publishing", template.TemplateId, () => transaction.CommitAsync(cancellationToken));
                return createsTemplate || createsReference;
            }
            catch (Exception exception) when (exception is DbUpdateException or DbException)
            {
                if (Commits.ShouldRetry(context, exception))
                    throw;
                throw new RuntimeArtifactEntityFrameworkPersistenceException(
                    "publishing",
                    template.TemplateId,
                    $"The EF runtime artifact store failed while publishing activity template '{template.TemplateId}'.",
                    exception);
            }
            finally
            {
                context.ChangeTracker.Clear();
            }
        }, lastConflict => throw new InvalidOperationException(
            $"The Runtime material of activity template '{template.TemplateId}' changed concurrently and did not settle after {Commits.MaxAttempts} attempts.",
            lastConflict), cancellationToken);
    }
}
