using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Composition;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Opt-in backend registration and explicit switch for Activities Design persistence.</summary>
public static class ActivitiesDesignEntityFrameworkCoreRegistration
{
    public const string StoreBackendName = "entity-framework";
    private static readonly EfModuleBinding Binding = new(
        "Activities Design",
        ActivitiesDesignEfModule.HistoryTableName,
        typeof(ActivitiesDesignDbContext).Assembly.GetName().Name,
        ActivitiesDesignEfModule.DefaultConnectionName,
        ActivitiesDesignEfModule.DefaultSqliteConnectionString);

    public static IServiceCollection AddActivitiesDesignEntityFrameworkCore(this IServiceCollection services, ActivitiesDesignEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var addContext = Binding.Select<Action<IServiceCollection, ActivitiesDesignEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<ActivitiesDesignSqliteDbContext>,
            AddContext<ActivitiesDesignSqlServerDbContext>,
            AddContext<ActivitiesDesignPostgreSqlDbContext>,
            AddContext<ActivitiesDesignMySqlDbContext>);
        var configuration = ActivitiesDesignPersistenceBackend.Fingerprint(provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling.ToString());
        var snapshot = services.ToArray();
        // A replaced backend may keep declarations outside the service collection, such as a shared
        // storage catalog. Snapshotting them through the neutral seam keeps a failed switch all-or-nothing.
        var registrationSnapshots = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRuntimePersistenceRegistrationState>()
            .Select(state => state.CaptureSnapshot())
            .ToArray();

        try
        {
            var existingBackend = ActivitiesDesignPersistenceBackend.Find(services);
            if (existingBackend is not null)
            {
                existingBackend.EnsureOwnsRegisteredDescriptors(services);
                EnsureAtomicWriterComposition(services);

                if (existingBackend.Name == ActivitiesDesignPersistenceBackend.EntityFramework)
                {
                    EnsureEfArtifactsAreOwned(existingBackend, services);
                    if (!existingBackend.HasConfiguration(ActivitiesDesignPersistenceBackend.EntityFramework, configuration))
                        throw new InvalidOperationException("Activities Design EF persistence is already registered with different provider options.");
                    return services;
                }

                existingBackend.RemoveOwnedDescriptors(services);
                EnsureNoUnownedActivitiesDesignRegistrations(services);
            }
            else
            {
                EnsureNoUnownedActivitiesDesignRegistrations(services);
            }

            ActivitiesDesignPersistenceBackend.RemoveFrameworkDefaults(services);
            services.AddPersistenceCore();
            // Identity generation is a host-wide seam shared with workflow design, so it is supplied
            // only as a default and never counted among this backend's owned registrations.
            services.TryAddScoped<IIdentityGenerator, ShortIdentityGenerator>();
            var configured = new ActivitiesDesignEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling
            };
            var registrationStart = services.Count;
            services.AddSingleton(configured);
            addContext(services, configured);

            services.TryAddScoped<EfActivityDesignStores>();
            services.TryAddScoped<EfActivityManagementProjectionWriter>(provider => new EfActivityManagementProjectionWriter(
                provider.GetRequiredService<ActivitiesDesignDbContext>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
            services.TryAddScoped<EfActivityManagementProjectionRetention>(provider => new EfActivityManagementProjectionRetention(
                provider.GetRequiredService<ActivitiesDesignDbContext>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>()));
            // This is a replacement contract: one deliberate host implementation wins; otherwise
            // the EF backend supplies its default. A host implementation is intentionally not part
            // of the owned descriptor set, so switching backends never removes it.
            services.TryAddScoped<IDesignAtomicWriter, EfDesignAtomicWrite>();
            Alias<IActivityDefinitionStore>(services);
            Alias<IActivityDefinitionVersionStore>(services);
            Alias<IActivityAvailabilitySettingsStore>(services);
            Alias<IActivityDefinitionManagementProjectionStore>(services);
            Alias<IActivityDefinitionAuthoringStore>(services);
            Alias<IActivityDefinitionDraftStore>(services);
            Alias<IActivityDefinitionVersionPublicationStore>(services);
            Alias<IActivityDefinitionLayoutStore>(services);
            Alias<IActivityDraftValidationStore>(services);
            Alias<IActivityForkStore>(services);
            Alias<IActivityDirectDependencyStore>(services);
            Alias<IActivityDependencyProjectionStore>(services);
            Alias<IActivityUpgradePlanStore>(services);
            Alias<IActivityUpgradeApplyReceiptStore>(services);
            Alias<IActivityDependencyProjectionRebuilder>(services);
            Alias<IAddActivityDefinitionCommand>(services);
            Alias<IAddActivityDefinitionVersionCommand>(services);
            Alias<ICreateActivityDefinitionCommand>(services);
            Alias<ICreateActivityDraftCommand>(services);
            Alias<IUpdateActivityDefinitionPresentationCommand>(services);
            Alias<IUpdateActivityDraftPresentationCommand>(services);
            Alias<IStoreActivityDraftValidationCommand>(services);
            Alias<IChangeActivityVersionLifecycleCommand>(services);
            Alias<ISetActivityDefinitionRecommendationCommand>(services);
            Alias<ISaveActivityForkCandidateCommand>(services);
            Alias<IPruneActivityForkCandidatesCommand>(services);
            Alias<IApplyActivityForkCandidateCommand>(services);
            Alias<ICreateActivityDraftConflictCopyCommand>(services);
            Alias<IReplaceActivityDraftCommand>(services);
            Alias<IApplyActivityContractProposalCommand>(services);
            Alias<IDiscardActivityDraftCommand>(services);
            Alias<IRecommendedActivityDefinitionPickerStore>(services);
            services.AddScoped<IActivityDefinitionLookup, ActivityDefinitionLookup>();
            services.AddScoped<IActivityDefinitionHasher, DefaultActivityDefinitionHasher>();
            services.AddScoped<IActivityDefinitionFactory, ActivityDefinitionFactory>();
            services.AddScoped<IActivityDefinitionVersionFactory, ActivityDefinitionVersionFactory>();

            ActivitiesDesignPersistenceBackend.Register(services, new ActivitiesDesignPersistenceBackend(
                ActivitiesDesignPersistenceBackend.EntityFramework,
                configuration,
                services.Skip(registrationStart).ToArray()));
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            foreach (var registrationSnapshot in registrationSnapshots)
                registrationSnapshot.Rollback();
            throw;
        }
    }

    private static void Alias<T>(IServiceCollection services) where T : class =>
        services.AddScoped<T>(provider => (T)(object)provider.GetRequiredService<EfActivityDesignStores>());

    private static void EnsureNoUnownedActivitiesDesignRegistrations(IServiceCollection services)
    {
        if (ActivitiesDesignPersistenceBackend.HasUnownedReplacementContract(services))
            throw new InvalidOperationException("Activities Design already has a persistence contract registered; remove it explicitly before selecting Entity Framework.");

        EnsureAtomicWriterComposition(services);
        if (services.Any(IsEfArtifactDescriptor))
            throw new InvalidOperationException("Activities Design already has an Entity Framework persistence artifact registered; select it through its backend registration.");
    }

    private static void EnsureAtomicWriterComposition(IServiceCollection services)
    {
        var marked = services
            .Where(descriptor => descriptor.ServiceType.IsDefined(typeof(ActivityDesignPersistenceReplacementContractAttribute), inherit: false))
            .ToArray();
        if (marked.Any(descriptor => !IsAtomicWriterServiceType(descriptor.ServiceType)))
            throw new InvalidOperationException("Activities Design already has a conflicting persistence replacement contract registered; select exactly one backend.");
        if (marked.Length > 1)
            throw new InvalidOperationException("Activities Design EF atomic persistence has conflicting replacement-contract registrations; select exactly one implementation.");
    }

    private static bool IsAtomicWriterServiceType(Type serviceType) =>
        serviceType.Name == "IDesignAtomicWriter" &&
        (serviceType.Namespace ?? string.Empty).EndsWith(".EntityFrameworkCore", StringComparison.Ordinal);

    private static void EnsureEfArtifactsAreOwned(ActivitiesDesignPersistenceBackend backend, IServiceCollection services)
    {
        var artifacts = services.Where(IsEfArtifactDescriptor).ToArray();
        if (artifacts.Any(descriptor => !backend.Owns(descriptor)))
            throw new InvalidOperationException("Activities Design EF persistence contains an unowned or stale provider artifact.");
    }

    private static bool IsEfArtifactDescriptor(ServiceDescriptor descriptor)
    {
        var serviceType = descriptor.ServiceType;
        if (serviceType is var type &&
            (type == typeof(ActivitiesDesignEntityFrameworkCoreOptions) ||
             type == typeof(ActivitiesDesignDbContext) ||
             type == typeof(ActivitiesDesignSqliteDbContext) ||
             type == typeof(ActivitiesDesignSqlServerDbContext) ||
             type == typeof(ActivitiesDesignPostgreSqlDbContext) ||
             type == typeof(ActivitiesDesignMySqlDbContext) ||
             type == typeof(EfActivityDesignStores) ||
             type == typeof(EfActivityManagementProjectionWriter) ||
             type == typeof(EfActivityManagementProjectionRetention)))
            return true;

        return type.IsGenericType &&
               type.GetGenericTypeDefinition() == typeof(DbContextOptions<>) &&
               typeof(ActivitiesDesignDbContext).IsAssignableFrom(type.GetGenericArguments()[0]);
    }

    private static void AddContext<TContext>(IServiceCollection services, ActivitiesDesignEntityFrameworkCoreOptions options)
        where TContext : ActivitiesDesignDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<ActivitiesDesignDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class ActivitiesDesignEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Optional database schema for this module's tables and its own migrations history table. Falls back to
    /// <see cref="EfSchema.ConfigurationKey"/>, then to the provider's own default. Ignored on SQLite and refused
    /// on MySQL, where a schema is a database.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per scope.</summary>
    public bool Pooling { get; set; }
}
