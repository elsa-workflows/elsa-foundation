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
    public ValueTask<bool> CanReadAsync(CancellationToken cancellationToken = default) => GuardAsync(() => AuthorizeAsync(cancellationToken), cancellationToken);

    public ValueTask<ActivityAuthoringCatalogResult> ListAsync(CancellationToken cancellationToken = default) => GuardAsync(async () =>
    {
        if (!await AuthorizeAsync(cancellationToken)) return ActivityAuthoringCatalogResult.Denied;
        var view = await reader.ListAsync(new ListActivityAuthoringCatalog(ActivityCatalogAvailability.All), cancellationToken);
        return new ActivityAuthoringCatalogResult(true, view.Activities.Select(ToEntry).ToArray());
    }, cancellationToken);

    private async ValueTask<bool> AuthorizeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = accessor.HttpContext;
        if (context is null || !principals.TryGetNormalizedPrincipal(context.User, out var principal)) return false;
        var tenant = principal.FindFirst(IdentityClaimTypes.TenantId)?.Value ?? principal.FindFirst("tenant_id")?.Value;
        // The resource is the current request, as it is for the catalog route's gate: the same principal and tenant. During a
        // Weaver turn that request is the agent's own (for example its messages request), so a handler keyed on route or
        // path sees that request, not GET /design/activities/catalog.
        return (await permissions.AuthorizeAsync(new(principal, ActivityDesignPermissions.Read, tenant, context), cancellationToken)).Succeeded;
    }

    private static async ValueTask<T> GuardAsync<T>(Func<ValueTask<T>> operation, CancellationToken cancellationToken)
    {
        try { return await operation(); }
        // Only the caller's own cancellation propagates; any other cancellation is an owner failure like the rest.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (ActivityAuthoringCatalogUnavailableException) { throw; }
        catch (Exception error) { throw new ActivityAuthoringCatalogUnavailableException(error); }
    }

    private static ActivityAuthoringCatalogEntry ToEntry(ActivityAuthoringDescriptorView view) => new(
        view.ActivityVersionId, view.ActivityTypeKey, view.Version, view.DisplayName, view.Category, view.Description,
        view.Available, view.AvailabilityReason,
        view.Inputs.Select(input => new ActivityAuthoringCatalogInput(input.ReferenceKey, input.Name, input.Type, input.DisplayName,
            input.IsRequired, input.IsSensitive, input.IsCredential) { UiHint = input.UiHint }).ToArray());
}

/// <summary>Rejects an ambiguous activity authoring catalog before the shell accepts requests.</summary>
/// <remarks>Captures only the configuring collection's metadata; a copied child shell rebinds it to its own collection.</remarks>
internal sealed class ActivityAuthoringCatalogStartupTask(IServiceCollection registrations) : Elsa.Tasks.Core.IStartupTask
{
    internal bool Owns(IServiceCollection collection) => ReferenceEquals(registrations, collection);

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate(registrations);
        return Task.CompletedTask;
    }

    /// <summary>Throws when the collection holds more than one catalog registration.</summary>
    internal static void Validate(IServiceCollection registrations)
    {
        if (registrations.Count(item => item.ServiceType == typeof(IActivityAuthoringCatalog)) > 1)
            throw new InvalidOperationException("IActivityAuthoringCatalog has multiple registrations; select one explicit activity authoring catalog.");
    }
}
