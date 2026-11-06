using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Events;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Disclosure;
using Elsa.Activities.Design.Disclosure.Extensions;
using Elsa.Activities.Design.Disclosure.Tasks;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Registration;

public sealed class CatalogDisclosureRegistrationTests
{
    [Fact]
    public async Task Repeated_feature_configuration_retains_one_startup_task_and_one_replacement()
    {
        var services = CatalogDisclosureTestHarness.Services();
        new ActivityCatalogDisclosureFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.Single(scope.ServiceProvider.GetServices<IStartupTask>().OfType<InitializeCatalogDisclosure>());
        Assert.Single(services.Where(item => item.ServiceType == typeof(IActivityCatalogDisclosureEvaluator)));
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        Assert.Equal(CatalogDisclosureStatus.Allowed, Assert.Single(await scope.ServiceProvider
            .GetRequiredService<IActivityCatalogDisclosureEvaluator>().EvaluateAsync(
                [CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Public)], CatalogDisclosurePurpose.Metadata)).Status);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("producer")]
    [InlineData("owner")]
    [InlineData("replacement")]
    [InlineData("aggregator")]
    public async Task Invalid_final_composition_fails_before_the_registry_becomes_usable(string fault)
    {
        var services = CatalogDisclosureTestHarness.Services();
        switch (fault)
        {
            case "policy":
                services.AddActivityCatalogDisclosurePolicy<CatalogDisclosureTestHarness.Policy>("source.policy", "1");
                break;
            case "producer":
                foreach (var descriptor in services.Where(item => item.ServiceType == typeof(CatalogDisclosureTestHarness.Producer) &&
                             item.IsKeyedService && Equals(item.ServiceKey, "source")).ToArray())
                    services.Remove(descriptor);
                services.AddKeyedTransient<CatalogDisclosureTestHarness.Producer>("source");
                break;
            case "owner":
                foreach (var descriptor in services.Where(item => item.ServiceType == typeof(IActivityCatalogAdmissionOwner)).ToArray())
                    services.Remove(descriptor);
                break;
            case "replacement":
                services.AddScoped<IActivityCatalogDisclosureEvaluator, DuplicateEvaluator>();
                break;
            case "aggregator":
                services.RemoveAll<IEventHandler<ActivityCatalogDisclosureInitializing>>();
                break;
        }
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<CatalogDisclosureUnavailableException>(() => CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider));
        // A failed startup cannot leave a partly published registry that allows even Public material.
        var stockEvaluator = scope.ServiceProvider.GetServices<IActivityCatalogDisclosureEvaluator>()
            .Single(evaluator => evaluator is not DuplicateEvaluator);
        await Assert.ThrowsAsync<CatalogDisclosureUnavailableException>(() => stockEvaluator.EvaluateAsync(
                [CatalogDisclosureTestHarness.Resource(CatalogDisclosureTestHarness.Public)], CatalogDisclosurePurpose.Metadata).AsTask());
        Assert.Throws<CatalogAdmissionException>(() => scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source"));
    }

    [Fact]
    public async Task Concrete_and_interface_aliases_share_one_producer_per_registration_and_scope()
    {
        var services = CatalogDisclosureTestHarness.Services();
        services.AddActivityCatalogProducer<CatalogDisclosureTestHarness.Producer, CatalogDisclosureTestHarness.Owner>(
            "second", CatalogProducerOwnerScope.CurrentTenant, new("source.policy", "1"), typeof(CatalogDisclosureTestHarness.ISource));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(first.ServiceProvider);
        var state = provider.GetRequiredService<CatalogDisclosureTestHarness.SourceState>();
        Assert.Equal(0, state.Created); // Initialization froze types, never producer instances.
        var bindings = first.ServiceProvider.GetRequiredService<ICatalogProducerScope>();
        var origin = bindings.GetOrigin("source");
        Assert.Same(origin.ScopeBinding, bindings.GetBinding("source"));
        var source = first.ServiceProvider.GetRequiredKeyedService<CatalogDisclosureTestHarness.Producer>("source");
        Assert.Same(source, first.ServiceProvider.GetRequiredKeyedService<CatalogDisclosureTestHarness.ISource>("source"));
        Assert.Same(source, first.ServiceProvider.GetRequiredKeyedService<CatalogDisclosureTestHarness.ISecondAlias>("source"));
        Assert.NotSame(source, first.ServiceProvider.GetRequiredKeyedService<CatalogDisclosureTestHarness.Producer>("second"));
        Assert.NotSame(origin.ScopeBinding, bindings.GetOrigin("second").ScopeBinding);
        using var second = provider.CreateScope();
        Assert.NotSame(origin.ScopeBinding, second.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source").ScopeBinding);
    }

    [Fact]
    public async Task Parallel_origin_resolution_keeps_one_binding_identity_in_one_scope()
    {
        using var provider = CatalogDisclosureTestHarness.Services().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        var producerScope = scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>();
        var origins = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => producerScope.GetOrigin("source"))));
        Assert.All(origins, origin => Assert.Same(origins[0].ScopeBinding, origin.ScopeBinding));
        Assert.Equal(1, provider.GetRequiredService<CatalogDisclosureTestHarness.SourceState>().Created);
    }

    [Fact]
    public async Task A_same_type_alias_substitute_is_refused_even_with_a_matching_registration_key()
    {
        var services = CatalogDisclosureTestHarness.Services();
        foreach (var descriptor in services.Where(item => item.ServiceType == typeof(CatalogDisclosureTestHarness.ISource) &&
                     item.IsKeyedService && Equals(item.ServiceKey, "source")).ToArray())
            services.Remove(descriptor);
        services.AddKeyedScoped<CatalogDisclosureTestHarness.ISource>("source", (provider, _) =>
            new CatalogDisclosureTestHarness.Producer(provider.GetRequiredService<CatalogDisclosureTestHarness.SourceState>()));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        await CatalogDisclosureTestHarness.Initialize(scope.ServiceProvider);
        Assert.Throws<CatalogAdmissionException>(() => scope.ServiceProvider.GetRequiredService<ICatalogProducerScope>().GetOrigin("source"));
    }

    [Fact]
    public void Origins_and_contributions_never_serialize_registration_or_binding_evidence()
    {
        var origin = new CatalogAdmissionOrigin("private-registration-sentinel", new { Secret = "private-producer-sentinel" });
        Assert.Equal("{}", JsonSerializer.Serialize(origin));
        var json = JsonSerializer.Serialize(new CatalogAdmissionContribution(CatalogDisclosureTestHarness.Candidate(), origin));
        Assert.DoesNotContain("private-registration-sentinel", json);
        Assert.DoesNotContain("private-producer-sentinel", json);
        Assert.DoesNotContain("ScopeBinding", json);
    }

    [Fact]
    public void Malformed_unicode_registration_keys_are_rejected_without_lossy_wire_aliases()
    {
        var services = CatalogDisclosureTestHarness.Services();
        Assert.Throws<ArgumentException>(() => services.AddActivityCatalogDisclosurePolicy<CatalogDisclosureTestHarness.Policy>("policy-\uD800", "1"));
        Assert.Throws<ArgumentException>(() => services.AddActivityCatalogProducer<CatalogDisclosureTestHarness.Producer,
            CatalogDisclosureTestHarness.Owner>("source-\uDC00", CatalogProducerOwnerScope.Global));
    }

    private sealed class DuplicateEvaluator : IActivityCatalogDisclosureEvaluator
    {
        public ValueTask<IReadOnlyList<CatalogDisclosureDecision>> EvaluateAsync(IReadOnlyList<CatalogDisclosureResource> resources,
            CatalogDisclosurePurpose purpose, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

internal static class CatalogDisclosureTestHarness
{
    public static CatalogDisclosureRequirement Public => new(1, CatalogDisclosureKind.Public, []);
    public static CatalogDisclosureRequirement Protected(params string[] keys) => new(1, CatalogDisclosureKind.Protected,
        (keys.Length == 0 ? ["source.policy"] : keys).Select(key => new CatalogDisclosurePolicyRequirement(key, "1",
            JsonSerializer.SerializeToElement(new { source = "source-a", tenant = "opaque-binding-tenant" }))).ToArray());
    public static CatalogDisclosureResource Resource(CatalogDisclosureRequirement requirement) =>
        new("version", "version-a", new(CatalogOwnerScopeKind.Tenant, "tenant-a"), requirement);

    public static CatalogAdmissionCandidate Candidate(CatalogOwnerScope? owner = null, CatalogDisclosureRequirement? inherited = null)
    {
        owner ??= new(CatalogOwnerScopeKind.Tenant, "tenant-a");
        var definition = new ActivityDefinition { Id = "definition-a", ActivityTypeKey = "test.activity", Category = "test", TenantId = owner.TenantId };
        var version = new ActivityDefinitionVersion("1.0.0", definition.Id)
        {
            Id = "version-a", Definition = definition, TenantId = owner.TenantId,
            ProviderKey = "provider", ProviderSchemaVersion = "1", ConsumerKey = "consumer", ConsumerSchemaVersion = "1",
            DescriptorPayload = JsonSerializer.SerializeToElement(new { }),
            SourceKind = "spoofable-CLR-label", SourceId = "spoofable-public-owner"
        };
        return new(definition, version, owner, owner, inherited ?? Public);
    }

    public static ServiceCollection Services(bool producer = true, bool currentAuthority = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new PolicyState());
        services.AddSingleton(new OwnerState());
        services.AddSingleton(new SourceState());
        services.AddSingleton(new ContextState());
        if (currentAuthority)
            services.AddScoped<IActivityCatalogDisclosureAuthority, Authority>();
        services.AddScoped<IActivityCatalogOwnerScopeAccessor, ScopeAccessor>();
        new EventsFeature().ConfigureServices(services);
        new ActivityCatalogDisclosureFeature().ConfigureServices(services);
        services.AddActivityCatalogDisclosurePolicy<Policy>("source.policy", "1");
        if (producer)
            services.AddActivityCatalogProducer<Producer, Owner>("source", CatalogProducerOwnerScope.CurrentTenant,
                new("source.policy", "1"), typeof(ISource), typeof(ISecondAlias));
        return services;
    }

    public static Task Initialize(IServiceProvider services) =>
        services.GetServices<IStartupTask>().OfType<InitializeCatalogDisclosure>().Single().ExecuteAsync(CancellationToken.None);

    public interface ISource { }
    public interface ISecondAlias { }
    public sealed class SourceState { public int Created; public Action? BeforeCreation; }
    public sealed class Producer : ISource, ISecondAlias
    {
        public Producer(SourceState state) { Interlocked.Increment(ref state.Created); state.BeforeCreation?.Invoke(); }
    }
    public sealed class PolicyState
    {
        public int Created;
        public int Calls;
        public Func<CatalogDisclosureResource, CatalogDisclosurePolicyRequirement, CatalogDisclosurePurpose, CancellationToken,
            ValueTask<CatalogDisclosureDecision>> Decide = (_, _, _, _) => ValueTask.FromResult(new CatalogDisclosureDecision(CatalogDisclosureStatus.Allowed));
    }
    public sealed class Policy : IActivityCatalogDisclosurePolicy
    {
        private readonly PolicyState state;
        public Policy(PolicyState state) { this.state = state; Interlocked.Increment(ref state.Created); }
        public ValueTask<CatalogDisclosureDecision> EvaluateAsync(CatalogDisclosureResource resource,
            CatalogDisclosurePolicyRequirement requirement, CatalogDisclosurePurpose purpose, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref state.Calls);
            return state.Decide(resource, requirement, purpose, cancellationToken);
        }
    }
    public sealed class OwnerState
    {
        public CatalogDisclosureRequirement Result = Protected();
        public Exception? Failure;
        public Action? Before;
        public int Calls;
    }
    public sealed class Owner(OwnerState state) : IActivityCatalogAdmissionOwner
    {
        public ValueTask<CatalogDisclosureRequirement> ClassifyAsync(CatalogAdmissionCandidate candidate, CancellationToken cancellationToken = default)
        {
            state.Calls++;
            state.Before?.Invoke();
            if (state.Failure is not null) throw state.Failure;
            return ValueTask.FromResult(state.Result);
        }
    }
    public sealed class ContextState
    {
        public bool Present = true;
        public Action? BeforePresence;
        public CatalogOwnerScope Scope = new(CatalogOwnerScopeKind.Tenant, "tenant-a");
    }
    public sealed class Authority(ContextState state) : IActivityCatalogDisclosureAuthority
    {
        public bool IsPresent { get { state.BeforePresence?.Invoke(); return state.Present; } }
    }
    public sealed class ScopeAccessor(ContextState state) : IActivityCatalogOwnerScopeAccessor
    {
        public CatalogOwnerScope Current => state.Scope;
    }
}
