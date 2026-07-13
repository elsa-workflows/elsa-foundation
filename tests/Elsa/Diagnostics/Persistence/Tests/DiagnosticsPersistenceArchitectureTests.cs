using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsPersistenceArchitectureTests
{
    [Fact]
    public void Diagnostics_core_and_shared_lifecycle_have_no_Groundwork_references()
    {
        var assemblies = new[]
        {
            typeof(IStructuredLogStore).Assembly,
            typeof(IOpenTelemetryStore).Assembly,
            typeof(DiagnosticsPersistenceRegistration).Assembly
        };

        foreach (var assembly in assemblies)
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name?.StartsWith("Groundwork", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Replacement_helper_leaves_exactly_one_store_and_one_shared_instance()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestStore, FirstStore>();
        services.AddSingleton<ITestStore, SecondStore>();

        services.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITestStore));
        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<ITestStore>(), provider.GetRequiredService<ReplacementStore>());
    }

    [Fact]
    public void Two_explicit_store_replacements_are_rejected_instead_of_last_write_wins()
    {
        var services = new ServiceCollection();
        services.ReplaceDiagnosticsStore<ITestStore, FirstStore>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.ReplaceDiagnosticsStore<ITestStore, SecondStore>());

        Assert.Contains(typeof(ITestStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FirstStore).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondStore).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_and_explicit_registration_have_order_independent_selection_semantics()
    {
        var defaultThenExplicit = new ServiceCollection();
        defaultThenExplicit.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();
        defaultThenExplicit.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>();
        using var firstProvider = defaultThenExplicit.BuildServiceProvider();

        var explicitThenDefault = new ServiceCollection();
        explicitThenDefault.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>();
        explicitThenDefault.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();
        using var secondProvider = explicitThenDefault.BuildServiceProvider();

        Assert.IsType<ReplacementStore>(firstProvider.GetRequiredService<ITestStore>());
        Assert.IsType<ReplacementStore>(secondProvider.GetRequiredService<ITestStore>());
        Assert.Single(defaultThenExplicit, descriptor => descriptor.ServiceType == typeof(ITestStore));
        Assert.Single(explicitThenDefault, descriptor => descriptor.ServiceType == typeof(ITestStore));
    }

    [Fact]
    public void Repeated_default_registration_and_preexisting_contract_remain_non_overriding()
    {
        var repeatedDefault = new ServiceCollection();
        repeatedDefault.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();
        repeatedDefault.AddDefaultDiagnosticsStore<ITestStore, SecondStore>();
        using var firstProvider = repeatedDefault.BuildServiceProvider();

        var preexisting = new ServiceCollection();
        preexisting.AddSingleton<ITestStore, ReplacementStore>();
        preexisting.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();
        using var secondProvider = preexisting.BuildServiceProvider();

        Assert.IsType<FirstStore>(firstProvider.GetRequiredService<ITestStore>());
        Assert.IsType<ReplacementStore>(secondProvider.GetRequiredService<ITestStore>());
        Assert.Single(repeatedDefault, descriptor => descriptor.ServiceType == typeof(ITestStore));
        Assert.Single(preexisting, descriptor => descriptor.ServiceType == typeof(ITestStore));
    }

    [Fact]
    public void Diagnostics_stores_default_to_scoped_and_share_one_instance_within_each_scope()
    {
        var services = new ServiceCollection();
        services.AddDefaultDiagnosticsStore<ITestStore, FirstStore>();

        Assert.Equal(ServiceLifetime.Scoped,
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITestStore)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped,
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(FirstStore)).Lifetime);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        Assert.Same(
            firstScope.ServiceProvider.GetRequiredService<ITestStore>(),
            firstScope.ServiceProvider.GetRequiredService<FirstStore>());
        Assert.NotSame(
            firstScope.ServiceProvider.GetRequiredService<ITestStore>(),
            secondScope.ServiceProvider.GetRequiredService<ITestStore>());
    }

    [Theory]
    [InlineData(nameof(DiagnosticsPersistenceRegistration.AddDefaultDiagnosticsStore))]
    [InlineData(nameof(DiagnosticsPersistenceRegistration.ReplaceDiagnosticsStore))]
    public void Store_registration_allows_an_explicit_documented_lifetime(string methodName)
    {
        var method = typeof(DiagnosticsPersistenceRegistration)
            .GetMethods()
            .Single(candidate => candidate.Name == methodName);

        Assert.Contains(method.GetParameters(), parameter => parameter.ParameterType == typeof(ServiceLifetime));
    }

    private interface ITestStore;
    private sealed class FirstStore : ITestStore;
    private sealed class SecondStore : ITestStore;
    private sealed class ReplacementStore : ITestStore;
}
