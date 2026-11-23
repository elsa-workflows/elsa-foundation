using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;

namespace Elsa.Studio.Dashboard.Management.Query;

internal sealed class DashboardDefinitionValidator(IDashboardQueryService queries) : IDashboardDefinitionValidator
{
    public async ValueTask ValidateAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default)
    {
        DashboardQueryValidation validation;
        try { validation = await queries.ValidateAsync(new(actor.TenantId, actor.UserId, "studio"), definition, cancellationToken); }
        catch (DashboardQueryException exception)
        { throw new DashboardResourceException("unavailable", "The question's source could not complete current validation. Retry shortly.", exception); }
        if (!validation.IsValid)
            throw new DashboardResourceException("validation", validation.Problems.FirstOrDefault()?.Message ?? "The question cannot be validated in the current scope.");
    }

    public async ValueTask<bool> CanDiscloseAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default)
    {
        try { return (await queries.ValidateAsync(new(actor.TenantId, actor.UserId, "studio"), definition, cancellationToken)).IsValid; }
        catch (DashboardQueryException exception)
        { throw new DashboardResourceException("unavailable", "Current definition disclosure could not be established. Retry shortly.", exception); }
    }
}
