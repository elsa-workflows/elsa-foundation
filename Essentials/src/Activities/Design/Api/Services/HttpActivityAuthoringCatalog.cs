using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>
/// The default <see cref="IActivityAuthoringCatalog"/>: the designer's catalog read (<see cref="IActivityAuthoringCatalogReader"/>
/// with every entry, addable or not) behind the designer's own gate, the actor's activity design read permission in the
/// tenant of the normalized request principal.
/// </summary>
/// <remarks>Without a request or a normalized principal the actor is denied. Infrastructure failures become
/// <see cref="ActivityAuthoringCatalogUnavailableException"/> with the original cause; cancellation propagates.</remarks>
internal sealed class HttpActivityAuthoringCatalog(
    IHttpContextAccessor accessor,
    NormalizedPrincipalValidator principals,
    IPermissionAuthorizationService permissions,
    IActivityAuthoringCatalogReader reader) : IActivityAuthoringCatalog
{
    public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => GuardAsync(() => AuthorizeAsync(cancellationToken));

    public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        if (!await AuthorizeAsync(cancellationToken)) return ActivityAuthoringCatalogResult.Denied;
        var view = await reader.ListAsync(new ListActivityAuthoringCatalog(ActivityCatalogAvailability.All), cancellationToken);
        return new ActivityAuthoringCatalogResult(true, view.Activities.Select(ToEntry).ToArray());
    });

    private async ValueTask<bool> AuthorizeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = accessor.HttpContext;
        if (context is null || !principals.TryGetNormalizedPrincipal(context.User, out var principal)) return false;
        var tenant = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? principal.FindFirst("tenant_id")?.Value;
        return (await permissions.AuthorizeAsync(new(principal, ActivityDesignPermissions.Read, tenant), cancellationToken)).Succeeded;
    }

    private static async ValueTask<T> GuardAsync<T>(Func<ValueTask<T>> operation)
    {
        try { return await operation(); }
        catch (OperationCanceledException) { throw; }
        catch (ActivityAuthoringCatalogUnavailableException) { throw; }
        catch (Exception error) { throw new ActivityAuthoringCatalogUnavailableException(error); }
    }

    private static ActivityAuthoringCatalogEntry ToEntry(ActivityAuthoringDescriptorView view) => new(
        view.ActivityVersionId, view.ActivityTypeKey, view.Version, view.DisplayName, view.Category, view.Description,
        view.Available, view.AvailabilityReason,
        view.Inputs.Select(input => new ActivityAuthoringCatalogInput(input.ReferenceKey, input.Name, input.Type, input.DisplayName,
            input.IsRequired, input.IsSensitive, input.IsCredential)).ToArray());
}

/// <summary>Rejects an ambiguous activity authoring catalog before the shell accepts requests.</summary>
/// <remarks>Captures only the configuring collection's metadata; a copied child shell rebinds it to its own collection.</remarks>
internal sealed class ActivityAuthoringCatalogStartupTask(IServiceCollection registrations) : Elsa.Tasks.Core.IStartupTask
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (registrations.Count(item => item.ServiceType == typeof(IActivityAuthoringCatalog)) > 1)
            throw new InvalidOperationException("IActivityAuthoringCatalog has multiple registrations; select one explicit activity authoring catalog.");
        return Task.CompletedTask;
    }
}
