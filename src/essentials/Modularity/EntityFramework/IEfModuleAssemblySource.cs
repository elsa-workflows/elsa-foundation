using System.Reflection;
using System.Runtime.Loader;

namespace Elsa.Modularity.EntityFramework;

/// <summary>
/// The assemblies the activation guard reads <c>[EfModule]</c> and <c>[UsesEfModule]</c> declarations
/// from. A seam rather than a fixed call, because what "this host's modules" means differs between a
/// plain host and one whose packages arrive through Nuplane.
/// </summary>
public interface IEfModuleAssemblySource
{
    IReadOnlyList<Assembly> GetAssemblies();
}

/// <summary>
/// Every assembly loaded into this process, across every <see cref="AssemblyLoadContext"/> — the same set
/// the persistence tooling discovers modules from, and for the same reason: a Nuplane
/// <c>HostIntegrated</c> package graph loads into a context of its own rather than the default one, so
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c> would silently miss exactly the modules a package
/// installed later, which is the case the guard exists for.
/// </summary>
/// <remarks>
/// Read on every evaluation rather than cached: a reconcile can load a module's assembly between two
/// apply calls, and a cache would keep answering from before it did.
/// </remarks>
public sealed class LoadedEfModuleAssemblySource : IEfModuleAssemblySource
{
    public IReadOnlyList<Assembly> GetAssemblies() =>
        [.. AssemblyLoadContext.All.SelectMany(context => context.Assemblies).Distinct()];
}
