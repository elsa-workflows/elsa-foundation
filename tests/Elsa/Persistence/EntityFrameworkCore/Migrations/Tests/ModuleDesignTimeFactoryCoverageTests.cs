using Elsa.EntityFrameworkCore.Tooling;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// Spec 171 FR-019: every <see cref="ModuleDesignTimeFactory{TContext}"/> line in
/// <c>tools/ef/Elsa.EntityFrameworkCore.Tooling/ModuleDesignTimeFactories.cs</c> must correspond to a
/// provider context some <c>[EfModule]</c> declares, and every declared provider context must have a
/// factory line, so the two lists cannot silently diverge. Review of #1871 found no slice's filed scope
/// owned this requirement.
/// </summary>
public sealed class ModuleDesignTimeFactoryCoverageTests
{
    /// <summary>
    /// Secrets declares all four provider contexts but only its MySQL context has a factory line today;
    /// #1877 adds the other three. Named here, not silently tolerated, and <see
    /// cref="The_named_secrets_exception_fails_once_1877_adds_the_missing_factory_lines"/> fails the moment
    /// #1877 closes the gap, so this exception cannot outlive the reason it exists.
    /// </summary>
    private static readonly Type[] SecretsFactoryGapUntil1877 =
    [
        typeof(SecretsSqliteDbContext),
        typeof(SecretsSqlServerDbContext),
        typeof(SecretsPostgreSqlDbContext)
    ];

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
    public void Every_declared_provider_context_has_a_factory_line_apart_from_the_named_secrets_exception()
    {
        var factories = FactoryContexts().ToHashSet();
        var missing = DeclaredProviderContexts()
            .Where(context => !factories.Contains(context) && !SecretsFactoryGapUntil1877.Contains(context))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"Declared provider contexts with no design-time factory line: {string.Join(", ", missing.Select(context => context.Name))}.");
    }

    /// <summary>
    /// #1877 removes <see cref="SecretsFactoryGapUntil1877"/>: once it adds Secrets' three missing factory
    /// lines, this fails, so the exception list cannot silently keep excusing a gap that no longer exists.
    /// </summary>
    [Fact]
    public void The_named_secrets_exception_fails_once_1877_adds_the_missing_factory_lines()
    {
        var factories = FactoryContexts().ToHashSet();
        var stillMissing = SecretsFactoryGapUntil1877.Where(context => !factories.Contains(context)).ToArray();

        Assert.Equal(SecretsFactoryGapUntil1877, stillMissing);
    }
}
