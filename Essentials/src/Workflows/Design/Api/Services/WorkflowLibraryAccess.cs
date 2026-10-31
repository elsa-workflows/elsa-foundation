using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Api.Services;

internal static class WorkflowLibraryAccess
{
    public static IWorkflowLibraryStore Require(IWorkflowLibraryStore? store) =>
        store ?? throw new WorkflowLibraryUnavailableException();

    public static async ValueTask<string?> SubjectAsync(HttpContext context, bool required, CancellationToken cancellationToken)
    {
        var session = await context.RequestServices.GetRequiredService<IAuthSessionService>()
            .GetAsync(context.User, cancellationToken);
        if (!string.Equals(session.Status, "authenticated", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(session.TenantId) || (required && string.IsNullOrWhiteSpace(session.Subject)))
            throw new WorkflowLibraryAuthorizationException("An authenticated subject and tenant are required for this operation.");
        return session.Subject;
    }

    public static async ValueTask<bool> AllowsAsync(HttpContext context, string permission, object? resource, CancellationToken cancellationToken)
    {
        var authorization = context.RequestServices.GetRequiredService<IPermissionAuthorizationService>();
        var result = await authorization.AuthorizeAsync(new(context.User, permission, Resource: resource ?? context), cancellationToken);
        return result.Succeeded;
    }

    public static async ValueTask RequireAsync(HttpContext context, string permission, object? resource, CancellationToken cancellationToken)
    {
        if (!await AllowsAsync(context, permission, resource, cancellationToken))
            throw new WorkflowLibraryAuthorizationException("The requested workflow library operation is not permitted.");
    }

    public static async ValueTask<WorkflowLibraryPermissions> PermissionsAsync(HttpContext context, object? resource, CancellationToken cancellationToken) =>
        new(await AllowsAsync(context, WorkflowDesignPermissions.AssignTags, resource, cancellationToken),
            await AllowsAsync(context, WorkflowDesignPermissions.Manage, resource, cancellationToken),
            await AllowsAsync(context, WorkflowDesignPermissions.ManageTags, resource, cancellationToken),
            await AllowsAsync(context, WorkflowDesignPermissions.ShareViews, resource, cancellationToken));

    public static async ValueTask<WorkflowLibraryReadScope> ReadScopeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ValidateWorkflowLibraryVisibilityPolicyStartupTask.Validate(context.RequestServices.GetRequiredService<IServiceCollection>());
        var policy = context.RequestServices.GetService<IWorkflowLibraryVisibilityPolicy>();
        if (policy is null)
        {
            // A handler may hide a definition on a different page. Authorizing only returned rows
            // would still reveal that hidden inventory through counts and continuation tokens.
            if (context.RequestServices.GetServices<IPermissionResourceHandler>().Any())
                throw new WorkflowLibraryAuthorizationException("This host must configure workflow library query visibility before browsing.");
            return new("tenant-action-v1");
        }
        var scope = await policy.GetScopeAsync(context.User, cancellationToken);
        static bool InvalidIdentities(IReadOnlyList<string>? ids) => ids is not null &&
            (ids.Count > 10_000 || ids.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128));
        if (scope is null || string.IsNullOrWhiteSpace(scope.Revision) || scope.Revision.Length > 256 ||
            scope.Revision.Any(char.IsControl) || InvalidIdentities(scope.AllowedDefinitionIds) || InvalidIdentities(scope.DeniedDefinitionIds))
            throw new WorkflowLibraryAuthorizationException("The host did not provide a valid bounded workflow library visibility scope.");
        // Snapshot a host-supplied mutable collection before it reaches cursor binding and SQL.
        return scope with { AllowedDefinitionIds = scope.AllowedDefinitionIds?.ToArray(), DeniedDefinitionIds = scope.DeniedDefinitionIds?.ToArray() };
    }

    public static async ValueTask RequireOrganizationReadAsync(HttpContext context, WorkflowOrganization organization, CancellationToken cancellationToken)
    {
        await RequireAsync(context, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(DefinitionId: organization.DefinitionId), cancellationToken);
        await RequireFolderReadAsync(context, organization.FolderBreadcrumb.Select(folder => folder.Id)
            .Concat(organization.FolderId is { } folderId ? [folderId] : []), cancellationToken);
    }

    public static async ValueTask RequireFolderReadAsync(HttpContext context, IEnumerable<string> folderIds, CancellationToken cancellationToken)
    {
        foreach (var folderId in folderIds.Distinct(StringComparer.Ordinal))
            await RequireAsync(context, WorkflowDesignPermissions.Read, new WorkflowLibraryResource(FolderId: folderId), cancellationToken);
    }

    public static void ValidateTargets(IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count is < 1 or > 100 || ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new ArgumentException("Select between 1 and 100 distinct workflow definitions.");
    }
}

internal sealed class WorkflowLibraryUnavailableException() : Exception("Workflow library organization is not supported by this backend.");
internal sealed class WorkflowLibraryAuthorizationException(string message) : Exception(message);
