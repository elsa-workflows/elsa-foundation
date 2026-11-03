using System.Reflection;
using CShells.DependencyInjection;

namespace Elsa.Studio.Hosting;

/// <summary>Declares stable host features for initial authority selection and ordinary runtime discovery.</summary>
public static class StudioFeatureAssemblyExtensions
{
    /// <summary>Records startup feature assemblies without initializing the runtime/package catalog.</summary>
    /// <remarks>Use ordinary WithAssemblies for runtime-only or collectible package generations.</remarks>
    public static CShellsBuilder WithStudioStartupAssemblies(this CShellsBuilder shells, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(shells);
        ArgumentNullException.ThrowIfNull(assemblies);
        if (shells.Services.IsReadOnly) throw new InvalidOperationException("Startup feature declarations must precede Build.");
        if (assemblies.Any(assembly => assembly is null || assembly.IsCollectible))
            throw new ArgumentException("Startup features require stable, non-collectible host assemblies.", nameof(assemblies));
        var declaration = shells.Services.SingleOrDefault(descriptor => descriptor.ServiceType == typeof(StudioStartupFeatureAssemblies))?.ImplementationInstance as StudioStartupFeatureAssemblies;
        if (declaration is null) shells.Services.AddSingleton(declaration = new StudioStartupFeatureAssemblies());
        declaration.Assemblies.UnionWith(assemblies);
        return shells.WithAssemblies(assemblies);
    }
}

internal sealed class StudioStartupFeatureAssemblies
{
    internal HashSet<Assembly> Assemblies { get; } = [];
}
