using Elsa.Workflows.Dashboard;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core Dashboard workflow-portfolio reader.</summary>
public static class WorkflowPortfolioEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddWorkflowPortfolioEntityFrameworkCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var designBackend = DesignPersistenceBackend.Find(services);
        if (designBackend?.Name != DesignPersistenceBackend.EntityFramework)
            throw new InvalidOperationException(
                "Dashboard workflow portfolio EF persistence requires the EF-owned Workflows Design context. Register Workflows Design EF persistence first.");

        designBackend.EnsureOwnsRegisteredContracts(services);
        var designContext = services.SingleOrDefault(descriptor => descriptor.ServiceType == typeof(WorkflowsDesignDbContext));
        if (designContext is null || !designBackend.Owns(designContext))
            throw new InvalidOperationException(
                "Dashboard workflow portfolio EF persistence requires the Workflows Design backend to own its context.");

        var runtimeBackend = RuntimeArtifactStoreBackend.Find(services);
        if (runtimeBackend?.Name != RuntimeArtifactStoreBackend.EntityFramework)
            throw new InvalidOperationException(
                "Dashboard workflow portfolio EF persistence requires the EF-owned Runtime artifact/source-reference backend. Register Runtime artifacts EF persistence first.");

        runtimeBackend.EnsureOwnsRegisteredContracts(services);
        var runtimeContext = services.SingleOrDefault(descriptor => descriptor.ServiceType == typeof(BookmarkStateDbContext));
        if (runtimeContext is null || !runtimeBackend.Owns(runtimeContext))
            throw new InvalidOperationException(
                "Dashboard workflow portfolio EF persistence requires the Runtime artifact backend to own the shared BookmarkStateDbContext.");

        services.RemoveAll<IWorkflowPortfolioDataSource>();
        services.AddScoped<EfWorkflowPortfolioDataSource>();
        services.AddScoped<IWorkflowPortfolioDataSource>(provider =>
            provider.GetRequiredService<EfWorkflowPortfolioDataSource>());
        return services;
    }
}
