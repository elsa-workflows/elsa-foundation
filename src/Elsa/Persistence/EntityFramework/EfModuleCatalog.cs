using System.Reflection;

namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// The one place that reads <see cref="EfModuleAttribute"/> declarations off a set of assemblies (ADR
/// 0076 D2). Every module is discovered the same way, first-party and third-party alike; there is no
/// first-party-only special case.
/// </summary>
public static class EfModuleCatalog
{
    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Enumerates every <see cref="EfModuleAttribute"/> declared on <paramref name="assemblies"/>, one
    /// <see cref="EfModuleDescriptor"/> per declaration. Refuses discovery — rather than silently picking
    /// one — when two declarations share a canonical name case-insensitively, naming both sources.
    /// </summary>
    public static IReadOnlyList<EfModuleDescriptor> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var descriptors = assemblies
            .Distinct()
            .SelectMany(assembly => assembly.GetCustomAttributes<EfModuleAttribute>().Select(attribute => Describe(assembly, attribute)))
            .ToArray();

        var collision = descriptors
            .GroupBy(descriptor => descriptor.Name, NameComparer)
            .FirstOrDefault(group => group.Count() > 1);

        if (collision is not null)
        {
            var sources = string.Join(", ", collision.Select(descriptor => $"'{descriptor.Name}' in {descriptor.Assembly.GetName().Name}"));
            throw new InvalidOperationException($"Two EF modules declare the same name case-insensitively: {sources}.");
        }

        return descriptors;
    }

    /// <summary>
    /// Finds the descriptor in <paramref name="modules"/> whose <see cref="EfModuleDescriptor.Name"/>
    /// matches <paramref name="name"/> case-insensitively, or <c>null</c> when none does.
    /// </summary>
    public static EfModuleDescriptor? Find(IReadOnlyList<EfModuleDescriptor> modules, string name)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(name);

        return modules.FirstOrDefault(descriptor => NameComparer.Equals(descriptor.Name, name));
    }

    private static EfModuleDescriptor Describe(Assembly assembly, EfModuleAttribute attribute)
    {
        if (string.IsNullOrWhiteSpace(attribute.Name))
            throw new InvalidOperationException($"{assembly.GetName().Name} declares an [EfModule] with no Name.");
        if (string.IsNullOrWhiteSpace(attribute.HistoryModule))
            throw new InvalidOperationException($"{assembly.GetName().Name} declares [EfModule(\"{attribute.Name}\")] with no HistoryModule.");

        try
        {
            EfMigrationsHistory.TableName(attribute.HistoryModule);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"{assembly.GetName().Name} declares [EfModule(\"{attribute.Name}\")] with an invalid HistoryModule '{attribute.HistoryModule}': {ex.Message}",
                ex);
        }

        return new EfModuleDescriptor(
            attribute.Name,
            attribute.ContextType,
            attribute.HistoryModule,
            attribute.Sqlite,
            attribute.SqlServer,
            attribute.PostgreSql,
            attribute.MySql,
            attribute.DependsOn,
            attribute.PostMigration,
            assembly);
    }
}
