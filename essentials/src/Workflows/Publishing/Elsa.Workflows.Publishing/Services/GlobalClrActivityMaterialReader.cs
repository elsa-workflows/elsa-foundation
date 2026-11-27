using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>Reads the exact Runtime material for a visible, reconciled global CLR activity.</summary>
internal sealed class GlobalClrActivityMaterialReader(
    IActivityDefinitionVersionPublicationStore publications,
    IPersistenceAccessContextAccessor access,
    IServiceScopeFactory scopeFactory,
    TimeProvider? timeProvider = null) : IGlobalClrActivityMaterialReader
{
    private const string ClrProvider = "elsa.clr-activity";
    private static readonly RuntimeSourceReadLimits ReadLimits = new();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>Resolves a published CLR version through the shell's reconciliation scope without changing either partition.</summary>
    public async ValueTask<(ExecutableActivityTemplate Template, WorkflowExecutableSourceReference Reference)?> FindAsync(
        ActivityDefinitionVersionPublication publication, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publication);
        cancellationToken.ThrowIfCancellationRequested();
        if (access.Current.Scope is not { } caller || !IsGlobalClrLeaf(publication))
            return null;

        var current = await publications.FindAsync(publication.DefinitionVersionId, cancellationToken);
        if (current is null || !IsGlobalClrLeaf(current) || current.Lifecycle != publication.Lifecycle ||
            current.DefinitionVersionId != publication.DefinitionVersionId || current.DefinitionId != publication.DefinitionId ||
            current.Version != publication.Version || current.TemplateId != publication.TemplateId ||
            current.TemplateHash != publication.TemplateHash || current.SourceReferenceId != publication.SourceReferenceId)
            return null;

        // Startup reconciliation uses the shell's unbound ordinary persistence scope. Use that same
        // service scope so custom shell defaults and historical material are both respected.
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var owner = services.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
        if (owner.AccessPolicy != PersistenceAccessPolicy.Ordinary || owner.Scope is not { } partition || partition == caller)
            return null;

        try
        {
            var template = await services.GetRequiredService<IExecutableActivityTemplateReader>()
                .FindBoundedAsync(current.TemplateId, partition, ReadLimits, cancellationToken);
            if (template is null || template.Scope != partition || template.Template.TemplateId != current.TemplateId ||
                template.Template.TemplateHash != current.TemplateHash || template.Template.DirectDependencies.Count != 0 ||
                template.Template.ClosedTemplates.Count != 0)
                return null;

            var source = await services.GetRequiredService<IWorkflowExecutableSourceReferenceReader>()
                .FindBoundedAsync(current.SourceReferenceId, partition, ReadLimits, cancellationToken);
            if (source is null || source.Scope != partition || source.Reference is not
                { TenantId: null, Scope: WorkflowExecutableReferenceScope.Published, SourceKind: "CLR" } reference ||
                reference.SourceReferenceId != current.SourceReferenceId || reference.ArtifactId != current.TemplateId ||
                reference.DefinitionId != current.DefinitionId || reference.DefinitionVersionId != current.DefinitionVersionId ||
                reference.SourceVersion != current.Version || reference.ArtifactVersion != current.Version ||
                current.Lifecycle == ActivityDefinitionVersionLifecycle.Active && !reference.IsLive(clock.GetUtcNow()))
                return null;

            return (template.Template, reference);
        }
        catch (RuntimeSourceReadException)
        {
            // A bounded reader refuses missing, incompatible or invalid material. The caller keeps
            // its existing exact dependency error at the publication or placement boundary.
            return null;
        }
    }

    private static bool IsGlobalClrLeaf(ActivityDefinitionVersionPublication publication) =>
        publication is { TenantId: null, SourceDraftId: null, SourceVersionId: null, DirectDependencyCount: 0, ClosedTemplateCount: 0 }
        && publication.Provider.ProviderKey == ClrProvider
        && publication.ResolveWorkflowResolutionKind() == ActivityDefinitionVersionResolutionKind.AuthorableActivity
        && publication.Lifecycle is ActivityDefinitionVersionLifecycle.Active or ActivityDefinitionVersionLifecycle.Retired;
}
