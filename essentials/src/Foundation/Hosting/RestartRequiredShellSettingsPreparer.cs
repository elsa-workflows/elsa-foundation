using CShells.Lifecycle;
using Elsa.Modularity.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.Hosting;

/// <summary>Checks the outgoing generation before CShells configures any of its replacement's features.</summary>
internal sealed class RestartRequiredShellSettingsPreparer(IServiceProvider host,
    ILogger<RestartRequiredShellSettingsPreparer> logger, IShellSettingsPreparer? inner) : IShellSettingsPreparer
{
    internal static void Register(IServiceCollection services)
    {
        var existing = services.SingleOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IShellSettingsPreparer) && !descriptor.IsKeyedService);
        var key = new object();
        if (existing is not null)
        {
            services.Remove(existing);
            // Keep the application's preparer at its original lifetime while CShells sees one unkeyed registration.
            var keyed = existing.ImplementationInstance is { } instance
                ? ServiceDescriptor.KeyedSingleton(typeof(IShellSettingsPreparer), key, instance)
                : existing.ImplementationFactory is { } factory
                    ? ServiceDescriptor.DescribeKeyed(typeof(IShellSettingsPreparer), key, (provider, _) => factory(provider), existing.Lifetime)
                    : ServiceDescriptor.DescribeKeyed(typeof(IShellSettingsPreparer), key, existing.ImplementationType!, existing.Lifetime);
            services.Add(keyed);
        }
        services.Add(ServiceDescriptor.Describe(typeof(IShellSettingsPreparer), provider => new RestartRequiredShellSettingsPreparer(
            provider, provider.GetRequiredService<ILogger<RestartRequiredShellSettingsPreparer>>(),
            existing is null ? null : provider.GetRequiredKeyedService<IShellSettingsPreparer>(key)),
            existing?.Lifetime ?? ServiceLifetime.Singleton));
    }

    public Task<ShellSettingsPreparationResult> PrepareAsync(ShellSettingsPreparationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var registry = host.GetRequiredService<IShellRegistry>();
        if (registry.GetActive(context.ShellId.Name) is { } active &&
            active.ServiceProvider.GetService<ShellRestartRequirement>() is { } requirement)
        {
            logger.LogWarning("{Feature} requires a host restart to replace the active shell {ShellName}.",
                requirement.FeatureName, context.ShellId.Name);
            throw new InvalidOperationException("The active shell requires a host restart before replacement.");
        }

        return inner?.PrepareAsync(context, cancellationToken) ??
            Task.FromResult(ShellSettingsPreparationResult.Unchanged(context));
    }
}
