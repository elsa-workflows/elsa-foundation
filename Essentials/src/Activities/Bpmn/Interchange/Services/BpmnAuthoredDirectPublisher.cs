using System.Security.Claims;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Process = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Interchange.Services;

internal sealed class BpmnAuthoredDirectPublisher(IWorkflowDefinitionVersionStore versions,
    IWorkflowDefinitionStore definitions, IWorkflowDefinitionVersionLayoutStore layouts, IActivityDefinitionLookup catalog,
    NormalizedPrincipalValidator validator, IPermissionAuthorizationService authorization, IBpmnModelRuntime runtime,
    IWorkflowExecutableSourceReferenceReader sources, TimeProvider clock)
{
    internal const string PublishPermission = "bpmn:publish";
    // Existing workflow publication permission; the domain bridge does not depend on its API assembly.
    internal const string WorkflowPublishPermission = "workflows/publishing:manage";
    internal const string WorkflowReadPermission = "workflows/runtime:read";

    public async Task<BpmnPublishedModel> PublishAsync(string versionId, string requestId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!validator.TryGetNormalizedPrincipal(user, out var principal)) throw new BpmnModelResourceMissingException();
        var subjects = principal.Claims.Where(claim => claim.Type is ClaimTypes.NameIdentifier or "sub")
            .Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
        var tenants = principal.FindAll(IdentityClaimTypes.TenantId).ToArray();
        if (subjects is not [{ Length: > 0 }] || tenants.Length > 1 || tenants.Any(claim => string.IsNullOrWhiteSpace(claim.Value)))
            throw new BpmnModelResourceMissingException();
        var caller = new BpmnModelCaller(subjects[0], tenants.SingleOrDefault()?.Value);
        var version = await versions.FindByIdAsync(versionId, cancellationToken);
        if (version is null || version.Id != versionId || version.TenantId != caller.TenantId) throw new BpmnModelResourceMissingException();
        var definition = await definitions.FindByIdAsync(version.DefinitionId, cancellationToken);
        if (definition is null || definition.Id != version.DefinitionId || definition.TenantId != caller.TenantId || definition.DeletedAt is not null)
            throw new BpmnModelResourceMissingException();
        foreach (var permission in new[] { PublishPermission, WorkflowPublishPermission })
            if (!(await authorization.AuthorizeAsync(new(principal, permission, caller.TenantId, version), cancellationToken)).Succeeded)
                throw new BpmnModelResourceMissingException();
        if (definition.IsSourceOwned)
            throw new BpmnModelValidationException("This source-owned definition cannot be directly published through the catalog authoring action.");
        var root = BpmnAuthoredDirectProjection.ReadState(version)?.RootActivity;
        var activity = root is null ? null : await catalog.FindVersion(root.ActivityVersionId, cancellationToken);
        if (activity is null ||
            !SemVer.TryParse(activity.Version, out var activityVersion) || activityVersion.Major != 1 || activityVersion.Minor != 0 ||
            activityVersion.Patch != 0 || activityVersion.Prerelease is not null ||
            !(await catalog.ListDefinitions(id: activity.DefinitionId, cancellationToken: cancellationToken))
                .Any(candidate => candidate.Id == activity.DefinitionId && candidate.ActivityTypeKey == typeof(Process).FullName))
            throw new BpmnModelValidationException("The immutable version root does not name a visible compatible BpmnProcess activity.");
        var layout = await layouts.FindByVersionIdAsync(version.Id, cancellationToken);
        if (layout is not null && (layout.WorkflowDefinitionVersionId != version.Id || layout.TenantId != caller.TenantId))
            throw new BpmnModelResourceMissingException();
        var projected = BpmnAuthoredDirectProjection.Project(version, layout);
        foreach (var service in projected.Services)
        {
            var source = await sources.FindAsync(service.SourceReferenceId, cancellationToken);
            var pin = service.Binding.Executable;
            if (source is null || source.SourceReferenceId != service.SourceReferenceId || source.TenantId != caller.TenantId ||
                source.Scope != WorkflowExecutableReferenceScope.Published || !source.IsLive(clock.GetUtcNow()) ||
                source.ArtifactId != pin.ArtifactId || source.DefinitionId != pin.DefinitionId ||
                source.DefinitionVersionId != pin.DefinitionVersionId || source.ArtifactVersion != pin.ArtifactVersion ||
                !(await authorization.AuthorizeAsync(new(principal, WorkflowReadPermission, caller.TenantId, source), cancellationToken)).Succeeded)
                throw new BpmnModelResourceMissingException();
        }
        return await runtime.PublishFromSourceAsync(projected.Definition, projected.Source, requestId, caller, cancellationToken);
    }
}
