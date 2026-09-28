using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// One <c>[EfSchemaFamily]</c> declaration to emit. A <see langword="null"/> <see cref="Module"/> emits the
/// two-argument constructor - a family shared by no single EF module - rather than naming one.
/// </summary>
internal sealed record SyntheticFamily(string Name, string? Module, string CurrentVersion);

/// <summary>
/// Throwaway assemblies carrying real <c>[EfModule]</c> and <c>[EfSchemaFamily]</c> metadata, for the declarations no
/// first-party module has: malformed ones, shared ones, and ones loaded where the host's own copy of the attribute
/// type is not.
/// </summary>
internal static class SyntheticSchemaFamilies
{
    public static byte[] Image(string assemblyName, string[] modules, params SyntheticFamily[] families)
    {
        var moduleConstructor = typeof(EfModuleAttribute).GetConstructor([typeof(string), typeof(Type)])!;
        var ownedFamilyConstructor = typeof(EfSchemaFamilyAttribute).GetConstructor([typeof(string), typeof(string), typeof(string)])!;
        var sharedFamilyConstructor = typeof(EfSchemaFamilyAttribute).GetConstructor([typeof(string), typeof(string)])!;
        var declarations = modules
            .Select(module => new CustomAttributeBuilder(moduleConstructor, [module, typeof(object)]))
            .Concat(families.Select(family => family.Module is null
                ? new CustomAttributeBuilder(sharedFamilyConstructor, [family.Name, family.CurrentVersion])
                : new CustomAttributeBuilder(ownedFamilyConstructor, [family.Name, family.Module, family.CurrentVersion])));

        var builder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly, declarations);
        builder.DefineDynamicModule(assemblyName);
        using var image = new MemoryStream();
        builder.Save(image);
        return image.ToArray();
    }
}

/// <summary>
/// Loads images for inspection only, so no code of theirs can run and nothing they declare joins the process's load
/// contexts, where every other test's readability source would read it.
/// </summary>
internal sealed class MetadataOnlyAssemblies : IDisposable
{
    private readonly MetadataLoadContext _context = new(new PathAssemblyResolver(
        Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll").Append(typeof(EfSchemaFamilyAttribute).Assembly.Location)));

    public Assembly Load(byte[] image) => _context.LoadFromByteArray(image);

    public void Dispose() => _context.Dispose();
}

/// <summary>
/// A load context of the kind Nuplane gives a package: it carries its own copy of <c>Elsa.Persistence.EntityFramework</c>,
/// so the attribute types its assemblies are declared with are not the host's.
/// </summary>
internal sealed class PackageLoadContext() : AssemblyLoadContext($"readability-package-{Guid.NewGuid():N}", isCollectible: true)
{
    private static readonly Assembly HostPersistence = typeof(EfSchemaFamilyAttribute).Assembly;

    public Assembly Load(byte[] image) => LoadFromStream(new MemoryStream(image));

    protected override Assembly? Load(AssemblyName assemblyName) =>
        assemblyName.Name == HostPersistence.GetName().Name ? LoadFromAssemblyPath(HostPersistence.Location) : null;
}
