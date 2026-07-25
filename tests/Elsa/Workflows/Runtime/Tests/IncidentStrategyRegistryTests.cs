using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Strategies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class IncidentStrategyRegistryTests
{
    [Fact]
    public void Catalog_lists_canonical_descriptors_without_constructing_strategies()
    {
        CountingStrategy.Constructions = 0;
        var services = NewServices();
        services.AddIncidentStrategy<CountingStrategy>(Descriptor("Contoso.Zeta", "1", "Zeta"));
        services.AddIncidentStrategy<SecondStrategy>(Descriptor("Contoso.Alpha", "2", "Alpha"));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var catalog = provider.GetRequiredService<IIncidentStrategyCatalog>();

        var descriptors = catalog.List();

        Assert.Equal(["Contoso.Alpha", "Contoso.Fault", "Contoso.Zeta"], descriptors.Select(descriptor => descriptor.Reference.Alias));
        Assert.Equal(0, CountingStrategy.Constructions);
        Assert.True(catalog.TryGet(new IncidentStrategyReference("contoso.zeta", "1"), out var descriptor));
        Assert.Equal("Contoso.Zeta", descriptor.Reference.Alias);
        Assert.Equal(TestDefaultReference, catalog.DefaultStrategy);
    }

    [Fact]
    public void Resolver_constructs_a_strategy_only_in_its_scope()
    {
        CountingStrategy.Constructions = 0;
        var services = NewServices();
        services.AddIncidentStrategy<CountingStrategy>(Descriptor("Contoso.Counting", "1", "Counting"));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IIncidentStrategyResolver>()
            .Resolve(new IncidentStrategyReference("CONTOSO.COUNTING", "1"));
        var firstAgain = firstScope.ServiceProvider.GetRequiredService<IIncidentStrategyResolver>()
            .Resolve(new IncidentStrategyReference("Contoso.Counting", "1"));
        using var secondScope = provider.CreateScope();
        var second = secondScope.ServiceProvider.GetRequiredService<IIncidentStrategyResolver>()
            .Resolve(new IncidentStrategyReference("Contoso.Counting", "1"));

        Assert.Same(first, firstAgain);
        Assert.NotSame(first, second);
        Assert.Equal(2, CountingStrategy.Constructions);
    }

    [Fact]
    public void Registration_enforces_exactly_one_scoped_strategy_service()
    {
        var singletonServices = NewServices();
        singletonServices.AddSingleton<CountingStrategy>();

        var singletonError = Assert.Throws<InvalidOperationException>(() =>
            singletonServices.AddIncidentStrategy<CountingStrategy>(Descriptor("Contoso.Singleton", "1", "Singleton")));
        Assert.Contains("must be scoped", singletonError.Message, StringComparison.Ordinal);

        var duplicateServices = NewServices();
        duplicateServices.AddScoped<CountingStrategy>();
        duplicateServices.AddScoped<CountingStrategy>();

        var duplicateError = Assert.Throws<InvalidOperationException>(() =>
            duplicateServices.AddIncidentStrategy<CountingStrategy>(Descriptor("Contoso.DuplicateService", "1", "Duplicate service")));
        Assert.Contains("multiple service registrations", duplicateError.Message, StringComparison.Ordinal);

        var scopedServices = NewServices();
        scopedServices.AddScoped<CountingStrategy>();
        scopedServices.AddIncidentStrategy<CountingStrategy>(Descriptor("Contoso.Scoped", "1", "Scoped"));

        Assert.Single(scopedServices, descriptor => descriptor.ServiceType == typeof(CountingStrategy));
        Assert.Equal(
            ServiceLifetime.Scoped,
            Assert.Single(scopedServices, descriptor => descriptor.ServiceType == typeof(CountingStrategy)).Lifetime);
    }

    [Fact]
    public void Attribute_registration_requires_one_direct_attribute_and_humanizes_its_display_name()
    {
        var services = NewServices();
        services.AddIncidentStrategy<AttributedIncidentStrategy>();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var descriptor = Assert.Single(provider.GetRequiredService<IIncidentStrategyCatalog>().List(), candidate =>
            candidate.Reference.Equals(new IncidentStrategyReference("Contoso.Attributed", "vNext")));

        Assert.Equal("Attributed", descriptor.DisplayName);
    }

    [Fact]
    public void Registration_rejects_conflicting_case_insensitive_identity_and_unqualified_custom_alias()
    {
        var services = NewServices();
        services.AddIncidentStrategy<CountingStrategy>(Descriptor("Contoso.Duplicate", "1", "Duplicate"));

        Assert.Throws<InvalidOperationException>(() => services.AddIncidentStrategy<SecondStrategy>(
            Descriptor("contoso.duplicate", "1", "Duplicate")));
        Assert.Throws<ArgumentException>(() => services.AddIncidentStrategy<SecondStrategy>(
            Descriptor("Unqualified", "1", "Unqualified")));
    }

    [Theory]
    [InlineData("Fault")]
    [InlineData("ContinueWithIncidents")]
    public void Registration_rejects_third_party_claims_of_reserved_builtin_aliases(string alias)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddIncidentStrategy<CountingStrategy>(
            Descriptor(alias, "1", alias)));

        Assert.Contains("reserved for runtime-owned registrations", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(IncidentStrategyRegistration));
    }

    [Fact]
    public void Catalog_requires_an_exact_registered_default_and_safe_intents_remain_third_party_namespaced()
    {
        var noDefault = new ServiceCollection();
        noDefault.AddIncidentStrategy<CountingStrategy>(Descriptor("Contoso.Only", "1", "Only"));
        using var noDefaultProvider = noDefault.BuildServiceProvider(validateScopes: true);
        Assert.Throws<InvalidOperationException>(() => noDefaultProvider.GetRequiredService<IIncidentStrategyCatalog>());

        var services = NewServices();
        services.AddIncidentStrategySafeIntent(new IncidentStrategySafeIntentDescriptor("Contoso.Notify"));

        Assert.Throws<ArgumentException>(() => services.AddIncidentStrategySafeIntent(
            new IncidentStrategySafeIntentDescriptor("Elsa.Activities.Primitives.PublishStimulus")));
        Assert.Throws<ArgumentException>(() => services.AddIncidentStrategySafeIntent(
            new IncidentStrategySafeIntentDescriptor("Contoso.SchedulerControl")));
    }

    [Fact]
    public void Runtime_composition_idempotently_registers_both_builtin_strategies()
    {
        var services = new ServiceCollection();

        services.AddWorkflowRuntime();
        services.AddWorkflowRuntime();

        using var provider = services.BuildServiceProvider();
        var references = provider.GetRequiredService<IIncidentStrategyCatalog>().List()
            .Select(descriptor => descriptor.Reference)
            .ToArray();
        Assert.Contains(IncidentStrategyBuiltIns.FaultReference, references);
        Assert.Contains(IncidentStrategyBuiltIns.ContinueWithIncidentsReference, references);
    }

    [Fact]
    public void Runtime_composition_retry_completes_builtin_registration_after_the_second_builtin_initially_fails()
    {
        var services = new ServiceCollection();
        services.AddScoped<ContinueWithIncidentsIncidentStrategy>();
        services.AddScoped<ContinueWithIncidentsIncidentStrategy>();

        Assert.Throws<InvalidOperationException>(() => services.AddWorkflowRuntime());

        services.RemoveAll<ContinueWithIncidentsIncidentStrategy>();
        services.AddScoped<ContinueWithIncidentsIncidentStrategy>();
        services.AddWorkflowRuntime();

        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(FaultIncidentStrategy));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(ContinueWithIncidentsIncidentStrategy));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IncidentStrategyRegistration) &&
            descriptor.ImplementationInstance is IncidentStrategyRegistration registration &&
            registration.Descriptor.Reference.Equals(IncidentStrategyBuiltIns.FaultReference));
        Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(IncidentStrategyRegistration) &&
            descriptor.ImplementationInstance is IncidentStrategyRegistration registration &&
            registration.Descriptor.Reference.Equals(IncidentStrategyBuiltIns.ContinueWithIncidentsReference));

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var references = provider.GetRequiredService<IIncidentStrategyCatalog>().List()
            .Select(descriptor => descriptor.Reference)
            .ToArray();
        Assert.Contains(IncidentStrategyBuiltIns.FaultReference, references);
        Assert.Contains(IncidentStrategyBuiltIns.ContinueWithIncidentsReference, references);
    }

    private static ServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddIncidentStrategy<FaultStrategy>(new IncidentStrategyDescriptor(TestDefaultReference, "Fault"));
        services.AddSingleton(new IncidentStrategyCatalogOptions { DefaultStrategy = TestDefaultReference });
        return services;
    }

    private static IncidentStrategyReference TestDefaultReference { get; } = new("Contoso.Fault", "1");

    private static IncidentStrategyDescriptor Descriptor(string alias, string version, string displayName) =>
        new(new IncidentStrategyReference(alias, version), displayName);

    private sealed class CountingStrategy : IIncidentStrategy
    {
        public static int Constructions;

        public CountingStrategy() => Constructions++;

        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(IncidentResolutionActions.WaitForIntervention());
    }

    private sealed class SecondStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(IncidentResolutionActions.WaitForIntervention());
    }

    private sealed class FaultStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(IncidentResolutionActions.FaultWorkflow());
    }

    [IncidentStrategy("Contoso.Attributed", "vNext")]
    private sealed class AttributedIncidentStrategy : IIncidentStrategy
    {
        public ValueTask<IIncidentResolutionAction> ResolveAsync(IncidentStrategyContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(IncidentResolutionActions.WaitForIntervention());
    }
}
