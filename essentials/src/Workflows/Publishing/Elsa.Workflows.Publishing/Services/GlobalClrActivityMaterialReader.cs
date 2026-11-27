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
    IPersistenceOperationScopeFactory operationScopeFactory,
    TimeProvider? timeProvider = null) : IGlobalClrActivityMaterialReader
{
    private const string ClrProvider = "elsa.clr-activity";
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

        // Discover the same shell default that startup reconciliation uses, then explicitly bind its
        // read operation so an ambient request cannot select either store's partition.
        await using var defaults = scopeFactory.CreateAsyncScope();
        var owner = defaults.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
        if (owner.AccessPolicy != PersistenceAccessPolicy.Ordinary || owner.Scope is not { } partition || partition == caller)
            return null;

        await using var operation = await operationScopeFactory.CreateAsync(partition, cancellationToken);
        var services = operation.ServiceProvider;
        var boundAccess = services.GetRequiredService<IPersistenceAccessContextAccessor>();
        if (boundAccess.Current != owner)
            return null;

        // The owner stores' exact point reads support the same Runtime schemas and providers as
        // ordinary compilation, including material reconciled before the bounded export profile.
        var template = await services.GetRequiredService<IExecutableActivityTemplateReader>()
            .FindAsync(current.TemplateId, cancellationToken);
        if (boundAccess.Current != owner || template is null || template.TemplateId != current.TemplateId ||
            template.TemplateHash != current.TemplateHash || template.DirectDependencies.Count != 0 ||
            template.ClosedTemplates.Count != 0)
            return null;

        var reference = await services.GetRequiredService<IWorkflowExecutableSourceReferenceReader>()
            .FindAsync(current.SourceReferenceId, cancellationToken);
        if (boundAccess.Current != owner || reference is not
            { TenantId: null, Scope: WorkflowExecutableReferenceScope.Published, SourceKind: "CLR" } ||
            reference.PublicationScope is { } physical && physical != partition ||
            reference.SourceReferenceId != current.SourceReferenceId || reference.ArtifactId != current.TemplateId ||
            reference.DefinitionId != current.DefinitionId || reference.DefinitionVersionId != current.DefinitionVersionId ||
            reference.SourceVersion != current.Version || reference.ArtifactVersion != current.Version ||
            current.Lifecycle == ActivityDefinitionVersionLifecycle.Active && !reference.IsLive(clock.GetUtcNow()))
            return null;

        return (template, reference);
    }

    private static bool IsGlobalClrLeaf(ActivityDefinitionVersionPublication publication) =>
        publication is { TenantId: null, SourceDraftId: null, SourceVersionId: null, DirectDependencyCount: 0, ClosedTemplateCount: 0 }
        && publication.Provider.ProviderKey == ClrProvider
        && publication.ResolveWorkflowResolutionKind() == ActivityDefinitionVersionResolutionKind.AuthorableActivity
        && publication.Lifecycle is ActivityDefinitionVersionLifecycle.Active or ActivityDefinitionVersionLifecycle.Retired;
}
