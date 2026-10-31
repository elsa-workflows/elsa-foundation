using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Endpoints.Definitions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Uses the owning API's native definition and folder decisions without reading authored workflow state.</summary>
/// <remarks>The built-in details reader authorizes organization metadata only. A replacement reader's
/// compatibility implementation of AuthorizeReadAsync may perform its complete read.</remarks>
internal sealed class HttpWorkflowDefinitionAuthorizer(
    IHttpContextAccessor accessor,
    NormalizedPrincipalValidator principals,
    IPermissionAuthorizationService permissions,
    IWorkflowDefinitionStore definitions,
    IEnumerable<IWorkflowDefinitionDetailsReader> readers) : IWorkflowDefinitionAuthorizer
{
    public ValueTask<bool> CanReadAsync(string definitionId, CancellationToken cancellationToken = default)
        => AuthorizeAsync(definitionId, false, cancellationToken);

    public ValueTask<bool> CanManageAsync(string definitionId, CancellationToken cancellationToken = default)
        => AuthorizeAsync(definitionId, true, cancellationToken);

    private async ValueTask<bool> AuthorizeAsync(string definitionId, bool manage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = accessor.HttpContext;
        if (string.IsNullOrWhiteSpace(definitionId) || context is null
            || !principals.TryGetNormalizedPrincipal(context.User, out var principal)) return false;
        var tenant = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value;
        if (string.IsNullOrWhiteSpace(tenant)) return false;
        var resource = new WorkflowLibraryResource(DefinitionId: definitionId);
        if (!(await permissions.AuthorizeAsync(new(principal, WorkflowDesignPermissions.Read, tenant, resource), cancellationToken)).Succeeded)
            return false;
        // The selected tenant-scoped store must confirm existence. Never substitute a persisted
        // workflow body or revision for a browser's unsaved working-state snapshot.
        var definition = await definitions.FindByIdAsync(definitionId, cancellationToken);
        if (definition is null || !string.Equals(definition.Id, definitionId, StringComparison.Ordinal)
            || !string.Equals(definition.TenantId, tenant, StringComparison.Ordinal)) return false;
        var currentReaders = readers.ToArray();
        if (currentReaders.Length != 1) return false;
        try { await currentReaders[0].AuthorizeReadAsync(definitionId, cancellationToken); }
        catch (WorkflowLibraryAuthorizationException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        return !manage || (await permissions.AuthorizeAsync(new(principal, WorkflowDesignPermissions.Manage, tenant, resource), cancellationToken)).Succeeded;
    }
}

/// <summary>Rejects ambiguous owner authority before the shell accepts requests.</summary>
internal sealed class WorkflowDefinitionAuthorizationStartupTask(IServiceCollection registrations) : Elsa.Tasks.Core.IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (registrations.Count(item => item.ServiceType == typeof(IWorkflowDefinitionAuthorizer)) > 1)
            throw new InvalidOperationException("IWorkflowDefinitionAuthorizer has multiple registrations; select one explicit workflow definition authorizer.");
        return Task.CompletedTask;
    }
}
