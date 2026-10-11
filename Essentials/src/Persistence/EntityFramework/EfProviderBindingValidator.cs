using CShells.Lifecycle;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The provider each EF module context is configured to bind through, collected while features register so a host
/// can be told at startup what it must reference. One entry per context type, replaced on re-registration, which
/// mirrors <see cref="EfModuleMigration{TContext}"/>: several features share one context and the last provider
/// configured for it is the one its migrator applies.
/// </summary>
public sealed class EfProviderBindingRequirements
{
    private readonly Dictionary<string, string> providersByModule = new(StringComparer.Ordinal);

    public void Set(string module, string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        providersByModule[module] = provider;
    }

    /// <summary>The configured provider per module context name, ordered so a report reads the same way every run.</summary>
    public IEnumerable<KeyValuePair<string, string>> All => providersByModule.OrderBy(entry => entry.Key, StringComparer.Ordinal);
}

/// <summary>
/// Fails a host closed at startup when a configured module's provider engine is absent or no longer exposes what the
/// reflection binding calls, instead of letting it surface when that module's context is first configured. It runs in
/// the CShells <see cref="LifecyclePhase.Prepare"/> phase ahead of every <see cref="EfModuleMigrator{TContext}"/>, and
/// as the first hosted service on plain hosts, so nothing has touched a store by the time it reports.
/// </summary>
public sealed class EfProviderBindingValidator(EfProviderBindingRequirements requirements) : IHostedService, IShellInitializer
{
    /// <summary>The order that puts this ahead of <see cref="EfModuleMigrator{TContext}"/>, which registers at 0.</summary>
    internal const int PrepareOrder = -100;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Validate();
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Validate();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Throws naming every module whose configured provider does not bind. Cheap and repeatable: no context is built.</summary>
    public void Validate()
    {
        var failures = requirements.All
            .Select(entry => (Module: entry.Key, Provider: entry.Value, Failure: EfRelationalProviderBinding.DescribeBindingFailure(entry.Value)))
            .Where(candidate => candidate.Failure is not null)
            .Select(candidate => $"  - {candidate.Module} (provider '{candidate.Provider}'): {candidate.Failure}")
            .ToArray();

        if (failures.Length == 0)
            return;

        throw new InvalidOperationException(
            $"{failures.Length} configured EF module{(failures.Length == 1 ? "" : "s")} cannot bind the selected provider, so no migrations ran:" +
            Environment.NewLine + string.Join(Environment.NewLine, failures));
    }
}

public static class EfProviderBindingValidationServiceCollectionExtensions
{
    /// <summary>
    /// Records that <typeparamref name="TContext"/> is configured for <paramref name="provider"/> and registers the
    /// startup validator once. Called from <c>AddEfModuleMigrations</c>, the one place every EF module passes through.
    /// </summary>
    public static IServiceCollection AddEfProviderBindingValidation<TContext>(this IServiceCollection services, string provider)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        Requirements(services).Set(typeof(TContext).Name, provider);
        return services;
    }

    private static EfProviderBindingRequirements Requirements(IServiceCollection services)
    {
        if (services.FirstOrDefault(descriptor => descriptor.ServiceType == typeof(EfProviderBindingRequirements))
                ?.ImplementationInstance is EfProviderBindingRequirements existing)
            return existing;

        var requirements = new EfProviderBindingRequirements();
        services.AddSingleton(requirements);
        services.AddShellInitializer<EfProviderBindingValidator>(LifecyclePhase.Prepare, EfProviderBindingValidator.PrepareOrder);
        // AddShellInitializer registers the initializer transiently; this last-wins registration makes the shell and
        // the hosted-service paths resolve one instance.
        services.AddSingleton<EfProviderBindingValidator>();
        // Registered before any module's migrator, so on a plain host it is the first hosted service to start.
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<EfProviderBindingValidator>());
        return requirements;
    }
}
