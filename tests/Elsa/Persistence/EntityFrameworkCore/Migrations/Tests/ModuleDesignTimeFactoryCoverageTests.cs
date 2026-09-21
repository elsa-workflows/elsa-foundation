using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Persistence.EntityFramework;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Spec 171 FR-019: every <see cref="ModuleDesignTimeFactory{TContext}"/> line in
/// <c>tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs</c> must correspond to a
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
    private static IReadOnlyList<Type> FactoryContexts() => typeof(ModuleDesignTimeFactory<>).Assembly
        .GetTypes()
        .Where(type => !type.IsAbstract)
        .Select(ModuleContextOf)
        .Where(context => context is not null)
        .Select(context => context!)
        .ToArray();

    private static Type? ModuleContextOf(Type factory)
    {
        for (var candidate = factory; candidate is not null; candidate = candidate.BaseType)
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(ModuleDesignTimeFactory<>))
                return candidate.GetGenericArguments()[0];

        return null;
    }

    private static IReadOnlyList<Type> DeclaredProviderContexts() => EfModuleCatalog.Discover(ModuleContextCatalog.Modules)
        .SelectMany(descriptor => ModuleContextCatalog.Providers.Select(descriptor.ProviderContext))
        .Where(context => context is not null)
        .Select(context => context!)
        .ToArray();

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
