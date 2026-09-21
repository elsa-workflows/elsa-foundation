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
/// Real assemblies carrying real <c>[EfModule]</c> metadata, for the module graphs no first-party module
/// has: a dependency cycle, a dependency outside the selection, and a declared post-migration action.
/// Emitted rather than checked in as a fixture project, so the graphs that must be refused travel with the
/// test that refuses them — and discovered through exactly the path a third-party module would be.
/// </summary>
internal static class SyntheticEfModules
{
    public static Assembly Build(string assemblyName, params SyntheticModule[] modules)
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
        return Assembly.Load(image.ToArray());

        static PropertyInfo Property(string name) => typeof(EfModuleAttribute).GetProperty(name)!;
    }
}
