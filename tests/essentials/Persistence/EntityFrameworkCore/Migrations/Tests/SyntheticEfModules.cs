using CShells.Features;
using Elsa.Persistence.EntityFramework;
using System.Reflection;
using System.Reflection.Emit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>One <c>[EfModule]</c> declaration to emit onto a throwaway assembly.</summary>
internal sealed record SyntheticModule(
    string Name,
    string HistoryModule,
    string[]? DependsOn = null,
    Type? PostgreSql = null,
    Type[]? PostMigration = null);

/// <summary>
/// Persisted assemblies carrying real EF module or enrolled feature metadata for synthetic producer and
/// module-graph cases. Emitted rather than checked in as a fixture project, so the cases travel with the
/// tests that exercise them and are discovered through the same paths as third-party declarations.
/// </summary>
internal static class SyntheticEfModules
{
    public static Assembly Build(string assemblyName, params SyntheticModule[] modules) =>
        Assembly.Load(BuildImage(assemblyName, modules));

    /// <summary>
    /// The raw assembly image, for a caller that needs to load it into an <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
    /// of its own rather than the default one <see cref="Build"/> loads into.
    /// </summary>
    public static byte[] BuildImage(string assemblyName, params SyntheticModule[] modules)
    {
        var attribute = typeof(EfModuleAttribute);
        var constructor = attribute.GetConstructor([typeof(string), typeof(Type)])!;
        var declarations = modules.Select(module =>
        {
            List<PropertyInfo> properties = [Property(nameof(EfModuleAttribute.HistoryModule))];
            List<object?> values = [module.HistoryModule];
            Add(nameof(EfModuleAttribute.DependsOn), module.DependsOn);
            Add(nameof(EfModuleAttribute.PostgreSql), module.PostgreSql);
            Add(nameof(EfModuleAttribute.PostMigration), module.PostMigration);
            return new CustomAttributeBuilder(constructor, [module.Name, typeof(object)], [.. properties], [.. values]);

            void Add(string name, object? value)
            {
                if (value is null)
                    return;
                properties.Add(Property(name));
                values.Add(value);
            }
        });

        var builder = new PersistedAssemblyBuilder(new(assemblyName), typeof(object).Assembly, declarations);
        builder.DefineDynamicModule(assemblyName);
        using var image = new MemoryStream();
        builder.Save(image);
        return image.ToArray();

        static PropertyInfo Property(string name) => typeof(EfModuleAttribute).GetProperty(name)!;
    }

    /// <summary>
    /// Emits one real, non-dynamic shell feature carrying many existing EF enrollment attributes. The
    /// loaded assembly is discovered by the same public producer path as any other host assembly.
    /// </summary>
    public static Assembly BuildParticipantFeature(string assemblyName, string featureName, int moduleCount)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly, []);
        var module = builder.DefineDynamicModule(assemblyName);
        var featureType = module.DefineType(
            $"{featureName}Feature",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(object),
            [typeof(IShellFeature)]);
        featureType.DefineDefaultConstructor(MethodAttributes.Public);

        var enrollmentConstructor = typeof(EfPersistenceResourceParticipantAttribute).GetConstructor(Type.EmptyTypes)!;
        featureType.SetCustomAttribute(new CustomAttributeBuilder(enrollmentConstructor, []));
        var moduleConstructor = typeof(UsesEfModuleAttribute).GetConstructor([typeof(string)])!;
        for (var index = 0; index < moduleCount; index++)
            featureType.SetCustomAttribute(new CustomAttributeBuilder(moduleConstructor, [$"Synthetic.Legacy.Module{index:D4}"]));

        var contractMethod = typeof(IShellFeature).GetMethod(nameof(IShellFeature.ConfigureServices))!;
        var implementation = featureType.DefineMethod(
            contractMethod.Name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot,
            contractMethod.ReturnType,
            contractMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        implementation.GetILGenerator().Emit(OpCodes.Ret);
        featureType.DefineMethodOverride(implementation, contractMethod);
        featureType.CreateType();

        using var image = new MemoryStream();
        builder.Save(image);
        return Assembly.Load(image.ToArray());
    }

    /// <summary>
    /// Emits persisted shell feature metadata without EF enrollment. These features exercise the
    /// normal host discovery and selection path while contributing no participant rows.
    /// </summary>
    public static Assembly BuildNonParticipantFeatures(string assemblyName, IReadOnlyList<string> featureNames)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly, []);
        var module = builder.DefineDynamicModule(assemblyName);
        var featureAttributeConstructor = typeof(ShellFeatureAttribute).GetConstructor([typeof(string)])!;
        var contractMethod = typeof(IShellFeature).GetMethod(nameof(IShellFeature.ConfigureServices))!;

        for (var index = 0; index < featureNames.Count; index++)
        {
            var featureType = module.DefineType(
                $"SyntheticNonParticipantFeature{index:D5}",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
                typeof(object),
                [typeof(IShellFeature)]);
            featureType.DefineDefaultConstructor(MethodAttributes.Public);
            featureType.SetCustomAttribute(new CustomAttributeBuilder(featureAttributeConstructor, [featureNames[index]]));

            var implementation = featureType.DefineMethod(
                contractMethod.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot,
                contractMethod.ReturnType,
                contractMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
            implementation.GetILGenerator().Emit(OpCodes.Ret);
            featureType.DefineMethodOverride(implementation, contractMethod);
            featureType.CreateType();
        }

        using var image = new MemoryStream();
        builder.Save(image);
        return Assembly.Load(image.ToArray());
    }
}
