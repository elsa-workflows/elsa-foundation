using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>
/// Commits a source-owned catalog version and its Runtime execution material in the ADR 0066 order: the
/// template and source reference in the Runtime context first, then the design rows in the Activities Design
/// context, which is the linearization point. A source-owned publication has no receipt, so there is no third
/// phase and nothing to converge on after the design commit.
/// </summary>
public sealed class EfSourceActivityPublicationCommand : ICommitSourceActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference>
{
    private readonly EfActivityPublicationRuntimeCommit runtime;
    private readonly EfActivityPublicationDesignCommit design;

    public EfSourceActivityPublicationCommand(
        IExecutableActivityTemplateStore templates,
        IWorkflowExecutableSourceReferenceStore sourceReferences,
        IActivityDefinitionVersionPublicationStore publications,
        IPersistenceAccessContextAccessor accessContextAccessor)
    {
        runtime = new EfActivityPublicationRuntimeCommit(templates, sourceReferences);
        design = new EfActivityPublicationDesignCommit(publications, accessContextAccessor);
    }

    public async Task ExecuteAsync(
        SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        Validate(commit);
        await design.EnsureSourcePublicationAdmissibleAsync(commit, cancellationToken);
        await runtime.CommitAsync(commit.ExecutableTemplate, commit.SourceReference, cancellationToken);
        await design.CommitSourcePublicationAsync(commit, cancellationToken);
    }

    private static void Validate(SourceActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference> commit)
    {
        if (commit.AuthoringState.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource ||
            !StringComparer.Ordinal.Equals(commit.Definition.Id, commit.CatalogVersion.DefinitionId) ||
            !StringComparer.Ordinal.Equals(commit.Definition.Id, commit.Publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(commit.CatalogVersion.Id, commit.Publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(commit.Publication.TemplateId, commit.ExecutableTemplate.TemplateId) ||
            !StringComparer.Ordinal.Equals(commit.Publication.TemplateHash, commit.ExecutableTemplate.TemplateHash) ||
            !StringComparer.Ordinal.Equals(commit.Publication.SourceReferenceId, commit.SourceReference.SourceReferenceId) ||
            !StringComparer.Ordinal.Equals(commit.SourceReference.ArtifactId, commit.ExecutableTemplate.TemplateId))
            throw new ArgumentException("Source activity publication identities do not align.", nameof(commit));
    }
}
