using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Persistence.Core.Services;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Elsa.Tasks.Core;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;

public static class WorkflowsDesignEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddWorkflowsDesignEntityFrameworkCore(this IServiceCollection services, WorkflowsDesignEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var existingBackend = DesignPersistenceBackend.Find(services);
        if (existingBackend is not null)
        {
            if (existingBackend.Name != DesignPersistenceBackend.EntityFramework)
                throw new InvalidOperationException($"Workflow-design persistence backend '{existingBackend.Name}' is already selected; use an explicit replacement API to switch backends.");
            existingBackend.RemoveOwnedDescriptors(services);
        }
        else if (HasOwnedSurfaceRegistration(services))
            throw new InvalidOperationException("An explicit workflow-design persistence registration is already present; EF Core refuses to replace it implicitly.");
        // Persistence core is shared infrastructure, not an EF-owned descriptor. Register it
        // before capturing this backend so a later backend replacement cannot remove the default
        // access-context accessor (and a caller's TryAdd custom accessor still wins).
        services.AddPersistenceCore();
        services.TryAddSingleton<IServiceCollection>(services);
        // Publishing contributes this provider-neutral fallback before a persistence backend is selected.
        // It is replaceable, while any arbitrary layout registration remains an explicit composition error.
        foreach (var descriptor in services.Where(descriptor =>
                     descriptor.ServiceType == typeof(IWorkflowDefinitionVersionLayoutStore) && IsFallbackDescriptor(descriptor)).ToArray())
            services.Remove(descriptor);
        var registrationStart = services.Count;
        services.AddSingleton(options);
        switch (provider)
        {
            case "sqlite": AddContext<WorkflowsDesignSqliteDbContext>(services, options, EfRelationalProviderBinding.UseSqlite); break;
            case "sqlserver": AddContext<WorkflowsDesignSqlServerDbContext>(services, options, EfRelationalProviderBinding.UseSqlServer); break;
            case "postgresql": AddContext<WorkflowsDesignPostgreSqlDbContext>(services, options, EfRelationalProviderBinding.UseNpgsql); break;
            case "mysql": AddContext<WorkflowsDesignMySqlDbContext>(services, options, EfRelationalProviderBinding.UseMySql); break;
            default: throw new ArgumentException($"Unknown Workflows Design EF provider '{options.Provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.", nameof(options));
        }
        services.TryAddScoped<IDesignAtomicWriter, EfDesignAtomicWriter>();
        services.TryAddScoped<IWorkflowDefinitionFactory, WorkflowDefinitionFactory>();
        services.TryAddScoped<IWorkflowDefinitionDraftFactory, WorkflowDefinitionDraftFactory>();
        services.TryAddScoped<IWorkflowDefinitionVersionFactory, WorkflowDefinitionVersionFactory>();
        services.TryAddScoped<IWorkflowDefinitionLookup, WorkflowDefinitionLookup>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateDesignPersistenceReplacementContractsStartupTask>());
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
        // Capture the exact descriptors emitted by this registration, including the provider's
        // closed DbContextOptions descriptor. Tracking by service type would leave stale provider
        // options behind when an application intentionally switches EF providers.
        DesignPersistenceBackend.Register(services, new DesignPersistenceBackend(
            DesignPersistenceBackend.EntityFramework,
            services.Skip(registrationStart)
                // Startup tasks are additive host services. The validator is idempotent, but the
                // selected persistence backend must never claim exclusive ownership of the type.
                .Where(descriptor => descriptor.ServiceType != typeof(IStartupTask))
                .ToArray()));
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

    private static bool HasOwnedSurfaceRegistration(IServiceCollection services) => services.Any(descriptor =>
        OwnedServiceTypes.Contains(descriptor.ServiceType) &&
        descriptor.ServiceType != typeof(IDesignAtomicWriter) &&
        !IsFallbackDescriptor(descriptor));

    private static bool IsFallbackDescriptor(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType is { } implementationType &&
        typeof(IDesignPersistenceFallback).IsAssignableFrom(implementationType);

    private static readonly Type[] OwnedServiceTypes =
    [
        typeof(WorkflowsDesignEntityFrameworkCoreOptions),
        typeof(WorkflowsDesignDbContext),
        typeof(DbContextOptions<WorkflowsDesignSqliteDbContext>),
        typeof(DbContextOptions<WorkflowsDesignSqlServerDbContext>),
        typeof(DbContextOptions<WorkflowsDesignPostgreSqlDbContext>),
        typeof(DbContextOptions<WorkflowsDesignMySqlDbContext>),
        typeof(WorkflowsDesignSqliteDbContext),
        typeof(WorkflowsDesignSqlServerDbContext),
        typeof(WorkflowsDesignPostgreSqlDbContext),
        typeof(WorkflowsDesignMySqlDbContext),
        typeof(IDesignAtomicWriter),
        typeof(IWorkflowDefinitionLookup),
        typeof(EfWorkflowDefinitionStore),
        typeof(EfWorkflowDefinitionVersionStore),
        typeof(EfWorkflowDefinitionDraftStore),
        typeof(EfWorkflowDefinitionVersionLayoutStore),
        typeof(EfWorkflowDefinitionListProjectionStore),
        typeof(IWorkflowDefinitionStore),
        typeof(IWorkflowDefinitionVersionStore),
        typeof(IWorkflowDefinitionDraftStore),
        typeof(IWorkflowDefinitionVersionLayoutStore),
        typeof(IWorkflowDefinitionListProjectionStore),
        typeof(IAddWorkflowDefinitionCommand),
        typeof(IAddWorkflowDefinitionVersionCommand),
        typeof(ICreateDraftCommand),
        typeof(ICloneDraftFromVersionCommand),
        typeof(IDeleteWorkflowDefinitionPermanentlyCommand),
        typeof(IDiscardDraftCommand),
        typeof(IMaterializeWorkflowDefinitionCommand),
        typeof(IMaterializeWorkflowDefinitionVersionCommand),
        typeof(IPromoteDraftToVersionCommand),
        typeof(ISaveWorkflowDefinitionCommand),
        typeof(ISubmitWorkflowDefinitionCommand),
        typeof(IUpdateDraftCommand)
    ];
}

public sealed class WorkflowsDesignEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
