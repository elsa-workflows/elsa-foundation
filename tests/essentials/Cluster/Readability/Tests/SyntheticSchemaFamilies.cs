using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// One <c>[EfSchemaFamily]</c> declaration to emit. A <see langword="null"/> <see cref="Module"/> emits the
/// two-argument constructor - a family shared by no single EF module - rather than naming one. <see cref="Upcasters"/>,
/// oldest first, become the declaration's chain.
/// </summary>
internal sealed record SyntheticFamily(string Name, string? Module, string CurrentVersion, IReadOnlyList<SyntheticUpcaster>? Upcasters = null);

/// <summary>
/// An upcaster type to emit: a concrete <see cref="IEfSchemaUpcaster"/> that returns its input, carrying
/// <c>[EfSchemaUpcaster(From, To)]</c> unless both are <see langword="null"/>.
/// </summary>
internal sealed record SyntheticUpcaster(string TypeName, string? From, string? To);

/// <summary>
/// One <c>[EfSchemaContent]</c> column, or with a <see cref="Reason"/> one <c>[EfSchemaIntegrity]</c> column, to emit for
/// <see cref="Family"/>, on an entity type the image defines as <see cref="Entity"/>.
/// </summary>
internal sealed record SyntheticColumn(string Family, string Entity, string Column, string? Reason = null);

/// <summary>
/// Throwaway assemblies carrying real <c>[EfModule]</c>, <c>[EfSchemaFamily]</c> and upcaster metadata, for the
/// declarations no first-party module has: malformed ones, shared ones, chained ones, and ones loaded where the host's
/// own copy of the attribute type is not.
/// </summary>
internal static class SyntheticSchemaFamilies
{
    public static byte[] Image(string assemblyName, string[] modules, params SyntheticFamily[] families) =>
        Image(assemblyName, modules, [], families);

    public static byte[] Image(string assemblyName, string[] modules, IReadOnlyList<SyntheticColumn> columns, params SyntheticFamily[] families)
    {
        var moduleConstructor = typeof(EfModuleAttribute).GetConstructor([typeof(string), typeof(Type)])!;
        var builder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly,
            modules.Select(module => new CustomAttributeBuilder(moduleConstructor, [module, typeof(object)])));
        var module = builder.DefineDynamicModule(assemblyName);
        foreach (var family in families)
            builder.SetCustomAttribute(Declaration(family, (family.Upcasters ?? []).Select(upcaster => Emit(module, upcaster)).ToArray()));
        var entities = columns.Select(column => column.Entity).Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => module.DefineType(name, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class).CreateType(), StringComparer.Ordinal);
        foreach (var column in columns)
            builder.SetCustomAttribute(column.Reason is null
                ? new CustomAttributeBuilder(typeof(EfSchemaContentAttribute).GetConstructors().Single(), [column.Family, entities[column.Entity], new[] { column.Column }])
                : new CustomAttributeBuilder(typeof(EfSchemaIntegrityAttribute).GetConstructors().Single(), [column.Family, entities[column.Entity], column.Column, column.Reason]));

        using var image = new MemoryStream();
        builder.Save(image);
        return image.ToArray();
    }

    private static CustomAttributeBuilder Declaration(SyntheticFamily family, Type[] upcasters)
    {
        var constructor = family.Module is null
            ? typeof(EfSchemaFamilyAttribute).GetConstructor([typeof(string), typeof(string)])!
            : typeof(EfSchemaFamilyAttribute).GetConstructor([typeof(string), typeof(string), typeof(string)])!;
        object[] arguments = family.Module is null ? [family.Name, family.CurrentVersion] : [family.Name, family.Module, family.CurrentVersion];
        return upcasters.Length == 0
            ? new CustomAttributeBuilder(constructor, arguments)
            : new CustomAttributeBuilder(constructor, arguments, [typeof(EfSchemaFamilyAttribute).GetProperty(nameof(EfSchemaFamilyAttribute.Upcasters))!], [upcasters]);
    }

    private static Type Emit(ModuleBuilder module, SyntheticUpcaster upcaster)
    {
        var type = module.DefineType(upcaster.TypeName, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(object), [typeof(IEfSchemaUpcaster)]);
        if (upcaster.From is not null || upcaster.To is not null)
            type.SetCustomAttribute(new CustomAttributeBuilder(typeof(EfSchemaUpcasterAttribute).GetConstructor([typeof(string), typeof(string)])!, [upcaster.From, upcaster.To]));
        type.DefineDefaultConstructor(MethodAttributes.Public);

        var contract = typeof(IEfSchemaUpcaster).GetMethod(nameof(IEfSchemaUpcaster.Upcast))!;
        var method = type.DefineMethod(
            contract.Name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(EfSchemaRowContent),
            [typeof(EfSchemaRowContent)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, contract);
        return type.CreateType();
    }
}

/// <summary>
/// Loads images for inspection only, so no code of theirs can run and nothing they declare joins the process's load
/// contexts, where every other test's readability source would read it.
/// </summary>
internal sealed class MetadataOnlyAssemblies : IDisposable
{
    private readonly MetadataLoadContext _context = new(new LoadedFirstResolver(new PathAssemblyResolver(
        Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll").Append(typeof(EfSchemaFamilyAttribute).Assembly.Location))));

    public Assembly Load(byte[] image) => _context.LoadFromByteArray(image);

    public void Dispose() => _context.Dispose();

    /// <summary>
    /// Binds a reference to an image this context already loaded before it looks on disk: a declaration's chain names its
    /// upcaster types by assembly-qualified name, and the assembly they name is the image itself.
    /// </summary>
    private sealed class LoadedFirstResolver(MetadataAssemblyResolver files) : MetadataAssemblyResolver
    {
        public override Assembly? Resolve(MetadataLoadContext context, AssemblyName assemblyName) =>
            context.GetAssemblies().FirstOrDefault(loaded => AssemblyName.ReferenceMatchesDefinition(assemblyName, loaded.GetName())) ??
            files.Resolve(context, assemblyName);
    }
}

/// <summary>
/// A load context of the kind Nuplane gives a package: it carries its own copy of <c>Elsa.Persistence.EntityFramework</c>,
/// so the attribute types its assemblies are declared with are not the host's.
/// </summary>
internal sealed class PackageLoadContext() : AssemblyLoadContext($"readability-package-{Guid.NewGuid():N}", isCollectible: true)
{
    private static readonly Assembly HostPersistence = typeof(EfSchemaFamilyAttribute).Assembly;

    public Assembly Load(byte[] image)
    {
        using var stream = new MemoryStream(image);
        return LoadFromStream(stream);
    }

    protected override Assembly? Load(AssemblyName assemblyName) =>
        assemblyName.Name == HostPersistence.GetName().Name ? LoadFromAssemblyPath(HostPersistence.Location) : null;
}
