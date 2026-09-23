using System.Reflection;
using CShells.Lifecycle;
using Elsa.Modularity.Core.Contracts;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Modularity.EntityFramework.Extensions;

public static class ModularityEntityFrameworkServiceCollectionExtensions
{
    /// <summary>Enrolls this host in EF resource preparation before CShells or feature management runs.</summary>
    public static IServiceCollection AddEfPersistenceResources(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(hostAssembly);

        var declarations = hostAssembly.GetCustomAttributes<EfToolingShellDefaultsAttribute>().ToArray();
        if (declarations.Length != 1)
            throw new InvalidOperationException(
                "EF persistence resources require exactly one shell-default composer declaration on the explicit host assembly.");
        var composerType = declarations[0].ComposerType;
        if (!(composerType.IsPublic || composerType.IsNestedPublic) ||
            !typeof(IEfToolingShellDefaults).IsAssignableFrom(composerType) ||
            composerType.IsAbstract ||
            composerType.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException(
                "The declared EF shell-default composer must be a concrete implementation with a public parameterless constructor.");

        var shellPreparers = services.Count(x => x.ServiceType == typeof(IShellSettingsPreparer));
        if (shellPreparers != 0)
            throw new InvalidOperationException(
                "EF persistence resources require exactly one CShells settings preparer; another preparer is already registered.");
        if (services.Any(x => x.ServiceType == typeof(IEfToolingShellDefaults)))
            throw new InvalidOperationException(
                "EF persistence resources require exactly one host shell-default composer; another composer is already registered.");

        var managementPreparers = services.Where(x => x.ServiceType == typeof(IFeatureActivationContextPreparer)).ToArray();
        if (managementPreparers.Length > 1 ||
            (managementPreparers.Length == 1 &&
             managementPreparers[0].ImplementationType?.IsDefined(
                 typeof(DefaultFeatureActivationContextPreparerAttribute), inherit: false) != true))
            throw new InvalidOperationException(
                "EF persistence resources require one replaceable default feature-activation preparer; conflicting registrations were found.");

        IEfToolingShellDefaults composer;
        try
        {
            composer = (IEfToolingShellDefaults)Activator.CreateInstance(composerType)!;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("The declared EF shell-default composer could not be constructed.");
        }
        foreach (var old in managementPreparers)
            services.Remove(old);
        services.AddSingleton(composer);
        services.AddSingleton<IShellSettingsPreparer>(new EfPersistenceShellSettingsPreparer(configuration));
        services.AddScoped<IFeatureActivationContextPreparer>(provider =>
            new EfPersistenceActivationContextPreparer(
                configuration,
                provider.GetRequiredService<CShells.Features.IRuntimeFeatureCatalog>(),
                provider.GetRequiredService<IEfToolingShellDefaults>()));
        return services;
    }

    /// <summary>
    /// Composes the EF activation guard into the container that owns feature management — the host's, not
    /// a shell's (ADR 0076 D9). It has to be registered before any EF feature is enabled, which is why an
    /// EF feature cannot bring it: the first one enabled on a shell that has none would find no guard at all.
    /// </summary>
    public static IServiceCollection AddEfPendingMigrationActivationGuard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IEfModuleAssemblySource, LoadedEfModuleAssemblySource>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureActivationGuard, EfPendingMigrationActivationGuard>());
        return services;
    }
}
