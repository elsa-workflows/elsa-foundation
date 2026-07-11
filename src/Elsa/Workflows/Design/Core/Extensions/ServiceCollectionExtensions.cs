using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Design.Core.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddScopedVariableAuthoring(this IServiceCollection services)
    {
        services.TryAddScoped<IActivityStructureService, DefaultActivityStructureService>();
        services.TryAddScoped<ScopedVariableResolver>();
        services.TryAddScoped<ScopedVariablePicker>();
        services.TryAddScoped<ScopedVariableAuthoringContract>();
        return services;
    }
}
