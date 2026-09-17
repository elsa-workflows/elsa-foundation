using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa3.Activities.Design.Import.Composition;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers, repeats, or explicitly switches the Elsa 3 import persistence to EF Core.</summary>
public static class Elsa3ImportEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = new(
        "Elsa 3 import",
        Elsa3ImportEfModule.HistoryTableName,
        typeof(Elsa3ImportDbContext).Assembly.GetName().Name,
        Elsa3ImportEfModule.DefaultConnectionName,
        Elsa3ImportEfModule.DefaultSqliteConnectionString);

    public static IServiceCollection AddElsa3ImportEntityFrameworkCore(
        this IServiceCollection services,
        Elsa3ImportEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var addContext = Binding.Select<Action<IServiceCollection, Elsa3ImportEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<Elsa3ImportSqliteDbContext>,
            AddContext<Elsa3ImportSqlServerDbContext>,
            AddContext<Elsa3ImportPostgreSqlDbContext>,
            AddContext<Elsa3ImportMySqlDbContext>);
        var configuration = Elsa3ImportPersistenceBackend.Fingerprint(provider, options.ConnectionString, options.ConnectionName);
        var snapshot = services.ToArray();
        // A replaced backend may keep declarations in a shared catalog outside the service
        // collection; snapshotting it through the neutral seam keeps a failed switch all-or-nothing.
        var registrationSnapshots = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRuntimePersistenceRegistrationState>()
            .Select(state => state.CaptureSnapshot())
            .ToArray();

        try
        {
            var existingBackend = Elsa3ImportPersistenceBackend.Find(services);
            if (existingBackend is not null)
            {
                existingBackend.EnsureOwnsRegisteredDescriptors(services);
                if (existingBackend.Name == Elsa3ImportPersistenceBackend.EntityFramework)
                {
                    if (services.Any(descriptor => IsEfArtifactDescriptor(descriptor) && !existingBackend.Owns(descriptor)))
                        throw new InvalidOperationException("Elsa 3 import EF persistence contains an unowned or stale provider artifact.");
                    if (!existingBackend.HasConfiguration(Elsa3ImportPersistenceBackend.EntityFramework, configuration))
                        throw new InvalidOperationException("Elsa 3 import EF persistence is already registered with different provider options.");
                    return services;
                }

                existingBackend.RemoveOwnedDescriptors(services);
            }

            if (Elsa3ImportPersistenceBackend.HasUnownedReplacementContract(services))
                throw new InvalidOperationException("An Elsa 3 import store or command is already registered; remove it explicitly before selecting Entity Framework.");
            if (services.Any(IsEfArtifactDescriptor))
                throw new InvalidOperationException("An Elsa 3 import Entity Framework artifact is already registered; select it through its backend registration.");

            // Persistence core is shared host infrastructure, never an owned descriptor.
            services.AddPersistenceCore();
            var configured = new Elsa3ImportEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName
            };
            var registrationStart = services.Count;
            services.AddSingleton(configured);
            addContext(services, configured);

            services.AddScoped<IReusableActivityImportOperationStore, EfReusableActivityImportOperationStore>();
            services.AddScoped<IReusableActivityImportCommand, EfReusableActivityImportCommand>();
            Elsa3ImportPersistenceBackend.Register(services, new Elsa3ImportPersistenceBackend(
                Elsa3ImportPersistenceBackend.EntityFramework,
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

    private static bool IsEfArtifactDescriptor(ServiceDescriptor descriptor)
    {
        var type = descriptor.ServiceType;
        if (type == typeof(Elsa3ImportEntityFrameworkCoreOptions) || typeof(Elsa3ImportDbContext).IsAssignableFrom(type))
            return true;
        return type.IsGenericType && type.GetGenericArguments().Any(argument => typeof(Elsa3ImportDbContext).IsAssignableFrom(argument));
    }

    private static void AddContext<TContext>(
        IServiceCollection services,
        Elsa3ImportEntityFrameworkCoreOptions options)
        where TContext : Elsa3ImportDbContext
    {
        services.AddDbContext<TContext>((provider, builder) => Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName));
        services.AddScoped<Elsa3ImportDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

/// <summary>
/// Provider and connection of the Elsa 3 import ledger. They must name the same database as the Activities
/// and Workflows Design EF lanes, because one import commits across all three in a single transaction.
/// </summary>
public sealed class Elsa3ImportEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
