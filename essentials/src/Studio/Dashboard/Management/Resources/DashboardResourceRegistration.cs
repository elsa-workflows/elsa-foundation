using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Studio.Dashboard.Management.Resources;

public static class DashboardResourceRegistration
{
    public static IServiceCollection AddDashboardResources(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IDashboardResourceService, DashboardResourceService>();
        services.TryAddScoped<IDashboardDefinitionValidator, UnavailableDashboardDefinitionValidator>();
        return services;
    }
}

internal sealed class UnavailableDashboardDefinitionValidator : IDashboardDefinitionValidator
{
    public ValueTask ValidateAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new DashboardResourceException("unavailable", "Question validation is not configured."));
    public ValueTask<bool> CanDiscloseAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
}
