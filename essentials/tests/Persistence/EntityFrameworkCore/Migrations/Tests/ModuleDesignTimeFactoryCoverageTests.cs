extern alias DecisionsTooling;

using Elsa.Decisions.Persistence.EntityFrameworkCore;
using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Every <see cref="ModuleDesignTimeFactory{TContext}"/> line in
/// the central tooling assembly or Decisions' linked tooling assembly must correspond to a
/// provider context some <c>[EfModule]</c> declares, and every declared provider context must have a
/// factory line, so the two lists cannot silently diverge. Review of #1871 found no slice's filed scope
/// owned this requirement.
/// </summary>
/// <remarks>
/// The list has no exceptions. Secrets carried a named one until #1877 added its SQLite, SQL Server and
/// PostgreSQL factory lines beside the MySQL line it already had.
/// </remarks>
public sealed class ModuleDesignTimeFactoryCoverageTests
{
    private static readonly Type[] FactoryDefinitions =
    [
        typeof(ModuleDesignTimeFactory<>),
        typeof(DecisionsTooling::Elsa.EntityFrameworkCore.Tooling.ModuleDesignTimeFactory<>)
    ];

    private static IReadOnlyList<Type> FactoryContexts() => FactoryDefinitions
        .SelectMany(definition => definition.Assembly.GetTypes()
            .Where(type => !type.IsAbstract)
            .Select(type => ModuleContextOf(type, definition)))
        .Where(context => context is not null)
        .Select(context => context!)
        .ToArray();

    private static Type? ModuleContextOf(Type factory, Type definition)
    {
        for (var candidate = factory; candidate is not null; candidate = candidate.BaseType)
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == definition)
                return candidate.GetGenericArguments()[0];

        return null;
    }

    private static IReadOnlyList<Type> DeclaredProviderContexts() => EfModuleCatalog.Discover(ModuleContextCatalog.Modules)
        .SelectMany(descriptor => ModuleContextCatalog.Providers.Select(descriptor.ProviderContext))
        .Where(context => context is not null)
        .Select(context => context!)
        .ToArray();

    [Fact]
    public void Decisions_linked_factory_definition_covers_all_four_actual_provider_contexts()
    {
        var central = FactoryDefinitions[0];
        var decisions = FactoryDefinitions[1];
        Assert.Equal(central.FullName, decisions.FullName);
        Assert.NotEqual(central, decisions);
        Assert.NotEqual(central.Assembly, decisions.Assembly);
        var contexts = decisions.Assembly.GetTypes()
            .Where(type => !type.IsAbstract)
            .Select(type => ModuleContextOf(type, decisions))
            .Where(type => type is not null)
            .Select(type => type!)
            .ToArray();
        Assert.Equal(4, contexts.Length);
        Assert.All(contexts, context => Assert.Equal(typeof(DecisionsEfModule).Assembly, context.Assembly));
        Assert.Equal(ModuleContextCatalog.Providers.SelectMany(ModuleContextCatalog.Contexts)
                .Where(context => context.Assembly == typeof(DecisionsEfModule).Assembly)
                .OrderBy(context => context.FullName, StringComparer.Ordinal),
            contexts.OrderBy(context => context.FullName, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_factory_line_corresponds_to_a_declared_provider_context()
    {
        var declared = DeclaredProviderContexts().ToHashSet();
        var orphaned = FactoryContexts().Where(context => !declared.Contains(context)).ToArray();

        Assert.True(orphaned.Length == 0,
            $"Design-time factory lines with no [EfModule] provider context: {string.Join(", ", orphaned.Select(context => context.Name))}.");
    }

    [Fact]
    public void Every_declared_provider_context_has_a_factory_line()
    {
        var factories = FactoryContexts().ToHashSet();
        var missing = DeclaredProviderContexts().Where(context => !factories.Contains(context)).ToArray();

        Assert.True(missing.Length == 0,
            $"Declared provider contexts with no design-time factory line: {string.Join(", ", missing.Select(context => context.Name))}.");
    }
}
