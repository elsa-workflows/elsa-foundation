using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

/// <summary>Registers the opt-in EF Core durable timer store (R24).</summary>
public static class RuntimeDurableTimerEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeDurableTimerEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeDurableTimerEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = DurableTimerStoreBackend.Find(services);
            if (existing?.Name == DurableTimerStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContracts(services);
                var prior = services.Select(x => x.ImplementationInstance)
                    .OfType<RuntimeDurableTimerEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options))
                    throw new InvalidOperationException("Runtime durable-timer EF persistence is already registered with different provider options.");
                RuntimeEfContextRegistration.EnsureContextIsAvailable(
                    services,
                    provider,
                    "Runtime durable timers",
                    existing.Owns,
                    RuntimeOperationalStateStoreBackend.Find(services) is { } existingOperational && existingOperational.Name == RuntimeOperationalStateStoreBackend.EntityFramework
                        ? existingOperational.Owns
                        : null);
                return services;
            }


            if (existing is not null)
                existing.EnsureOwnsRegisteredContracts(services);
            else
                DurableTimerStoreBackend.EnsureNoUnownedRegistrations(services);

            RuntimeEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling);
            RuntimeEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(
                services,
                options.RecoveryContinuationSigningKey,
                "Runtime durable timers");

            var commitExistingRemoval = existing?.PrepareRemoveOwnedArtifacts(services);
            foreach (var descriptor in DurableTimerStoreBackend.CaptureTimerSurfaceRegistrations(services).ToArray())
                services.Remove(descriptor);

            var operational = RuntimeOperationalStateStoreBackend.Find(services);
            if (operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
                operational.EnsureOwnsRegisteredContracts(services);
            RuntimeEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime durable timers",
                operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operational.Owns : null);

            var configured = new RuntimeDurableTimerEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                Schema = options.Schema,
                Pooling = options.Pooling,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            };
            services.AddOptions<RuntimeRecoveryContinuationOptions>().Configure(configuredOptions =>
            {
                if (!string.IsNullOrWhiteSpace(options.RecoveryContinuationSigningKey))
                    configuredOptions.SigningKey = options.RecoveryContinuationSigningKey;
                configuredOptions.AllowEphemeralDevelopmentKey = false;
            });
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());

            var owned = new List<ServiceDescriptor>();
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            owned.Add(optionsDescriptor);
            if (RuntimeEfContextRegistration.ContextRegistrations(services, provider).Count == 0)
                owned.AddRange(RuntimeEfContextRegistration.AddContext(services, provider, configured.ConnectionString, configured.ConnectionName, configured.Schema, configured.Pooling));
            foreach (var descriptor in RuntimeEfContextRegistration.ContextRegistrations(services, provider))
                if (!owned.Contains(descriptor))
                    owned.Add(descriptor);

            services.AddScoped<EfDurableTimerStore>();
            var concrete = services.Last();
            var contract = ServiceDescriptor.Scoped<IDurableTimerStore>(serviceProvider =>
                serviceProvider.GetRequiredService<EfDurableTimerStore>());
            services.Add(contract);
            owned.Add(concrete);
            owned.Add(contract);
            DurableTimerStoreBackend.Register(
                services,
                new DurableTimerStoreBackend(
                    DurableTimerStoreBackend.EntityFramework,
                    owned,
                    collection => RemoveOwned(collection, owned)));
            commitExistingRemoval?.Invoke(services);
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }

    private static bool OptionsEqual(
        RuntimeDurableTimerEntityFrameworkCoreOptions left,
        RuntimeDurableTimerEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        StringComparer.Ordinal.Equals(left.ConnectionString, right.ConnectionString) &&
        StringComparer.Ordinal.Equals(left.ConnectionName, right.ConnectionName) &&
        StringComparer.Ordinal.Equals(left.RecoveryContinuationSigningKey, right.RecoveryContinuationSigningKey);

    private static void RemoveOwned(
        IServiceCollection services,
        IReadOnlyCollection<ServiceDescriptor> owned)
    {
        foreach (var descriptor in owned.Where(descriptor =>
                     services.Contains(descriptor) &&
                     RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) != true))
            services.Remove(descriptor);
    }
}

public sealed class RuntimeDurableTimerEntityFrameworkCoreOptions
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
    public string? RecoveryContinuationSigningKey { get; set; }
}
