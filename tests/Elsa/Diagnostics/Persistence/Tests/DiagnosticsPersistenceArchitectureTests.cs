using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    [Fact]
    public void Explicit_singleton_store_lifetime_is_preserved_for_default_and_replacement_registrations()
    {
        var defaultServices = new ServiceCollection();
        defaultServices.AddDefaultDiagnosticsStore<ITestStore, FirstStore>(ServiceLifetime.Singleton);
        var replacementServices = new ServiceCollection();
        replacementServices.ReplaceDiagnosticsStore<ITestStore, ReplacementStore>(ServiceLifetime.Singleton);

        Assert.All(
            defaultServices.Where(descriptor =>
                descriptor.ServiceType == typeof(ITestStore) || descriptor.ServiceType == typeof(FirstStore)),
            descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
        Assert.All(
            replacementServices.Where(descriptor =>
                descriptor.ServiceType == typeof(ITestStore) || descriptor.ServiceType == typeof(ReplacementStore)),
            descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));
    }

    [Fact]
    public void Invalid_store_lifetime_is_rejected_by_default_and_replacement_registration()
    {
        var invalid = (ServiceLifetime)int.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().AddDefaultDiagnosticsStore<ITestStore, FirstStore>(invalid));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCollection().ReplaceDiagnosticsStore<ITestStore, FirstStore>(invalid));
    }

    [Fact]
    public void Observer_replacement_is_order_independent_and_rejects_a_second_explicit_selection()
    {
        var defaultThenExplicit = new ServiceCollection();
        defaultThenExplicit.AddDiagnosticsPersistenceObservability();
        defaultThenExplicit.ReplaceDiagnosticsStore<IDiagnosticsPersistenceObserver, FirstObserver>(ServiceLifetime.Singleton);
        Assert.Throws<InvalidOperationException>(() =>
            defaultThenExplicit.ReplaceDiagnosticsStore<IDiagnosticsPersistenceObserver, SecondObserver>(ServiceLifetime.Singleton));

        var explicitThenDefault = new ServiceCollection();
        explicitThenDefault.ReplaceDiagnosticsStore<IDiagnosticsPersistenceObserver, FirstObserver>(ServiceLifetime.Singleton);
        explicitThenDefault.AddDiagnosticsPersistenceObservability();
        Assert.Throws<InvalidOperationException>(() =>
            explicitThenDefault.ReplaceDiagnosticsStore<IDiagnosticsPersistenceObserver, SecondObserver>(ServiceLifetime.Singleton));

        using var firstProvider = defaultThenExplicit.BuildServiceProvider();
        using var secondProvider = explicitThenDefault.BuildServiceProvider();
        Assert.IsType<FirstObserver>(firstProvider.GetRequiredService<IDiagnosticsPersistenceObserver>());
        Assert.IsType<FirstObserver>(secondProvider.GetRequiredService<IDiagnosticsPersistenceObserver>());
    }

    [Fact]
    public void Observability_composition_rejects_untracked_explicit_observer_conflicts_before_or_after_default()
    {
        var beforeDefault = new ServiceCollection();
        beforeDefault.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        beforeDefault.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();
        Assert.Throws<InvalidOperationException>(beforeDefault.AddDiagnosticsPersistenceObservability);

        var afterDefault = new ServiceCollection();
        afterDefault.AddDiagnosticsPersistenceObservability();
        afterDefault.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        afterDefault.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();
        Assert.Throws<InvalidOperationException>(afterDefault.AddDiagnosticsPersistenceObservability);
    }

    [Fact]
    public void Startup_validation_rejects_observer_conflicts_registered_after_composition()
    {
        var services = new ServiceCollection();
        services.AddDiagnosticsPersistenceObservability();
        services.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(typeof(DiagnosticsPersistenceCounters).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(FirstObserver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DiagnosticsPersistenceRegistration.ReplaceDiagnosticsStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_validation_rejects_two_direct_observer_types_with_actionable_descriptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver, FirstObserver>();
        services.AddDiagnosticsPersistenceObservability();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(typeof(FirstObserver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DiagnosticsPersistenceRegistration.ReplaceDiagnosticsStore), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_validation_describes_factory_and_type_observer_conflicts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver>(_ => new FirstObserver());
        services.AddDiagnosticsPersistenceObservability();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains("factory registration", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_validation_accepts_an_instance_observer_and_duplicate_composition_registers_one_validator()
    {
        var observer = new FirstObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver>(observer);
        services.AddDiagnosticsPersistenceObservability();
        services.AddDiagnosticsPersistenceObservability();

        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(DiagnosticsPersistenceObserverRegistrationValidator));
        Assert.Single(services, descriptor =>
            descriptor.ImplementationType?.Name == "DiagnosticsPersistenceObserverRegistrationOptionsAdapter");
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();
        Assert.Same(observer, provider.GetRequiredService<IDiagnosticsPersistenceObserver>());
    }

    [Fact]
    public void Observer_registration_validation_exports_only_the_constitution_mandated_implementation()
    {
        var assembly = typeof(DiagnosticsPersistenceObserverRegistrationValidator).Assembly;
        var exportedRegistrationTypes = assembly.ExportedTypes
            .Where(type => type.Name.StartsWith("DiagnosticsPersistenceObserverRegistration", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal([typeof(DiagnosticsPersistenceObserverRegistrationValidator)], exportedRegistrationTypes);
        Assert.True(typeof(DiagnosticsPersistenceObserverRegistrationValidator).IsSealed);
        var constructor = Assert.Single(typeof(DiagnosticsPersistenceObserverRegistrationValidator).GetConstructors());
        Assert.Equal([typeof(IServiceCollection)], constructor.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Contains(assembly.GetTypes(), type =>
            type.Name == "DiagnosticsPersistenceObserverRegistrationOptionsAdapter" && !type.IsPublic);
    }

    [Fact]
    public void Startup_validation_describes_instance_and_type_observer_conflicts()
    {
        var observer = new FirstObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IDiagnosticsPersistenceObserver>(observer);
        services.AddDiagnosticsPersistenceObservability();
        services.AddSingleton<IDiagnosticsPersistenceObserver, SecondObserver>();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Contains(observer.GetType().FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(SecondObserver).FullName!, exception.Message, StringComparison.Ordinal);
    }

    private interface ITestStore;
    private sealed class FirstStore : ITestStore;
    private sealed class SecondStore : ITestStore;
    private sealed class ReplacementStore : ITestStore;
    private sealed class FirstObserver : TestObserver;
    private sealed class SecondObserver : TestObserver;

    private abstract class TestObserver : IDiagnosticsPersistenceObserver
    {
        public void RecordState(DiagnosticsDrainState state) { }
        public void RecordRetry(DiagnosticsPersistenceOperation operation, int attempt, int maxAttempts) { }
        public void RecordOperationFailure(DiagnosticsPersistenceOperation operation) { }
        public void RecordLoss(DiagnosticsPersistenceLossReason reason, long count) { }
    }
}
