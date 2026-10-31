using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
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

    public static void ValidateTargets(IReadOnlyList<string>? ids)
    {
        if (ids is null || ids.Count is < 1 or > 100 || ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new ArgumentException("Select between 1 and 100 distinct workflow definitions.");
    }
}

internal sealed class WorkflowLibraryUnavailableException() : Exception("Workflow library organization is not supported by this backend.");
internal sealed class WorkflowLibraryAuthorizationException(string message) : Exception(message);
