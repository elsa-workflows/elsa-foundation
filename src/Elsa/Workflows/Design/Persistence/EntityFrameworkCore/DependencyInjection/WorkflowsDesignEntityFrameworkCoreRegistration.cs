using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;

public static class WorkflowsDesignEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddWorkflowsDesignEntityFrameworkCore(this IServiceCollection services, WorkflowsDesignEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        services.RemoveAll<WorkflowsDesignEntityFrameworkCoreOptions>();
        services.AddSingleton(options);
        services.RemoveAll<WorkflowsDesignDbContext>();
        services.RemoveAll<WorkflowsDesignSqliteDbContext>(); services.RemoveAll<WorkflowsDesignSqlServerDbContext>();
        services.RemoveAll<WorkflowsDesignPostgreSqlDbContext>(); services.RemoveAll<WorkflowsDesignMySqlDbContext>();
        services.RemoveAll<IDesignAtomicWriter>();
        services.RemoveAll<IWorkflowDefinitionStore>(); services.RemoveAll<IWorkflowDefinitionVersionStore>();
        services.RemoveAll<IWorkflowDefinitionDraftStore>(); services.RemoveAll<IWorkflowDefinitionVersionLayoutStore>();
        services.RemoveAll<IWorkflowDefinitionListProjectionStore>();
        services.RemoveAll<EfWorkflowDefinitionStore>(); services.RemoveAll<EfWorkflowDefinitionVersionStore>();
        services.RemoveAll<EfWorkflowDefinitionDraftStore>(); services.RemoveAll<EfWorkflowDefinitionVersionLayoutStore>();
        services.RemoveAll<EfWorkflowDefinitionListProjectionStore>();
        services.RemoveAll<IAddWorkflowDefinitionCommand>(); services.RemoveAll<IAddWorkflowDefinitionVersionCommand>();
        services.RemoveAll<ICreateDraftCommand>(); services.RemoveAll<ICloneDraftFromVersionCommand>();
        services.RemoveAll<IDeleteWorkflowDefinitionPermanentlyCommand>(); services.RemoveAll<IDiscardDraftCommand>();
        services.RemoveAll<IMaterializeWorkflowDefinitionCommand>(); services.RemoveAll<IMaterializeWorkflowDefinitionVersionCommand>();
        services.RemoveAll<IPromoteDraftToVersionCommand>(); services.RemoveAll<ISaveWorkflowDefinitionCommand>();
        services.RemoveAll<ISubmitWorkflowDefinitionCommand>(); services.RemoveAll<IUpdateDraftCommand>();
        switch (provider)
        {
            case "sqlite": AddContext<WorkflowsDesignSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite); break;
            case "sqlserver": AddContext<WorkflowsDesignSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer); break;
            case "postgresql": AddContext<WorkflowsDesignPostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql); break;
            case "mysql": AddContext<WorkflowsDesignMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql); break;
            default: throw new ArgumentException($"Unknown Workflows Design EF provider '{options.Provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.", nameof(options));
        }
        services.AddScoped<IDesignAtomicWriter, EfDesignAtomicWriter>();
        services.AddScoped<EfWorkflowDefinitionStore>(); services.AddScoped<IWorkflowDefinitionStore>(sp => sp.GetRequiredService<EfWorkflowDefinitionStore>());
        services.AddScoped<EfWorkflowDefinitionVersionStore>(); services.AddScoped<IWorkflowDefinitionVersionStore>(sp => sp.GetRequiredService<EfWorkflowDefinitionVersionStore>());
        services.AddScoped<EfWorkflowDefinitionDraftStore>(); services.AddScoped<IWorkflowDefinitionDraftStore>(sp => sp.GetRequiredService<EfWorkflowDefinitionDraftStore>());
        services.AddScoped<EfWorkflowDefinitionVersionLayoutStore>(); services.AddScoped<IWorkflowDefinitionVersionLayoutStore>(sp => sp.GetRequiredService<EfWorkflowDefinitionVersionLayoutStore>());
        services.AddScoped<EfWorkflowDefinitionListProjectionStore>(); services.AddScoped<IWorkflowDefinitionListProjectionStore>(sp => sp.GetRequiredService<EfWorkflowDefinitionListProjectionStore>());
        services.AddScoped<IAddWorkflowDefinitionCommand, EfAddWorkflowDefinitionCommand>();
        services.AddScoped<IAddWorkflowDefinitionVersionCommand, EfAddWorkflowDefinitionVersionCommand>();
        services.AddScoped<ICreateDraftCommand, EfCreateDraftCommand>(); services.AddScoped<ICloneDraftFromVersionCommand, EfCloneDraftFromVersionCommand>();
        services.AddScoped<IDeleteWorkflowDefinitionPermanentlyCommand, EfDeleteWorkflowDefinitionPermanentlyCommand>(); services.AddScoped<IDiscardDraftCommand, EfDiscardDraftCommand>();
        services.AddScoped<IMaterializeWorkflowDefinitionCommand, EfMaterializeWorkflowDefinitionCommand>(); services.AddScoped<IMaterializeWorkflowDefinitionVersionCommand, EfMaterializeWorkflowDefinitionVersionCommand>();
        services.AddScoped<IPromoteDraftToVersionCommand, EfPromoteDraftToVersionCommand>(); services.AddScoped<ISaveWorkflowDefinitionCommand, EfSaveWorkflowDefinitionCommand>(); services.AddScoped<ISubmitWorkflowDefinitionCommand, EfSubmitWorkflowDefinitionCommand>(); services.AddScoped<IUpdateDraftCommand, EfUpdateDraftCommand>();
        return services;
    }

    private static void AddContext<T>(IServiceCollection services, WorkflowsDesignEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind) where T : WorkflowsDesignDbContext
    {
        services.AddDbContext<T>((sp, builder) => bind(builder, ResolveConnectionString(sp, options), WorkflowsDesignEfModule.HistoryTableName, typeof(WorkflowsDesignDbContext).Assembly.GetName().Name));
        services.AddScoped<WorkflowsDesignDbContext>(sp => sp.GetRequiredService<T>());
    }
    internal static string ResolveConnectionString(IServiceProvider services, WorkflowsDesignEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString)) return options.ConnectionString;
        var configuration = services.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName)) return configuration?.GetConnectionString(options.ConnectionName) ?? throw new InvalidOperationException($"Workflows Design EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
        return configuration?.GetConnectionString(WorkflowsDesignEfModule.DefaultConnectionName) ?? (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite" ? WorkflowsDesignEfModule.DefaultSqliteConnectionString : throw new InvalidOperationException("Workflows Design EF requires ConnectionString or ConnectionName for a non-Sqlite provider."));
    }
}

public sealed class WorkflowsDesignEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
