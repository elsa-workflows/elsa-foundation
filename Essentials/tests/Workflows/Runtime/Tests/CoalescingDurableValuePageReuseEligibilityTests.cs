using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingDurableValuePageReuseEligibilityTests
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    public void Owned_fake_EF_backend_with_stable_codec_and_accessor_is_eligible(ServiceLifetime accessorLifetime)
    {
        var raw = BuildComposition();
        ReplaceAccessor(raw, new ServiceDescriptor(typeof(IPersistenceAccessContextAccessor), typeof(FakeAccessor), accessorLifetime));
        var composition = Decorate(raw);

        Assert.True(composition.Registration.EligibleBeforeDecoration);
        Assert.True(composition.Registration.IsEligible);
    }

    [Fact]
    public void Scoped_factory_accessor_and_builtin_HMAC_instance_are_eligible()
    {
        var raw = BuildComposition();
        ReplaceAccessor(raw, ServiceDescriptor.Scoped<IPersistenceAccessContextAccessor>(_ => new FakeAccessor()));
        ReplaceCodec(raw, ServiceDescriptor.Singleton<IRuntimeRecoveryContinuationCodec>(CreateCodec()));
        var composition = Decorate(raw);

        Assert.True(composition.Registration.EligibleBeforeDecoration);
        Assert.True(composition.Registration.IsEligible);
    }

    [Theory]
    [InlineData("missing-backend")]
    [InlineData("duplicate-backend")]
    [InlineData("keyed-backend")]
    [InlineData("factory-backend")]
    [InlineData("wrong-backend-service-type")]
    [InlineData("in-memory-backend")]
    [InlineData("duplicate-owned-durable-contract")]
    [InlineData("keyed-owned-durable-contract")]
    [InlineData("keyed-owned-other-contract")]
    [InlineData("interface-only-backend")]
    [InlineData("missing-owned-concrete")]
    [InlineData("duplicate-owned-concrete")]
    [InlineData("concrete-override")]
    [InlineData("unowned-contract-override")]
    [InlineData("missing-accessor")]
    [InlineData("duplicate-accessor")]
    [InlineData("keyed-accessor")]
    [InlineData("transient-accessor")]
    [InlineData("missing-codec")]
    [InlineData("duplicate-codec")]
    [InlineData("keyed-codec")]
    [InlineData("factory-codec")]
    [InlineData("custom-codec")]
    [InlineData("scoped-codec")]
    public void Ineligible_pre_decoration_compositions_bypass_without_throwing(string scenario)
    {
        var raw = BuildPreDecorationCase(scenario);
        var registration = RuntimeCoalescingDurableValuePageReuseRegistration.CaptureBeforeDecoration(raw.Services);

        Assert.False(registration.EligibleBeforeDecoration);
        Assert.False(registration.IsEligible);
    }

    [Fact]
    public void Ineligible_registration_skips_decoration_enumeration()
    {
        var raw = BuildPreDecorationCase("in-memory-backend");
        var registration = RuntimeCoalescingDurableValuePageReuseRegistration.CaptureBeforeDecoration(raw.Services);
        var enumerationAttempted = false;
        var marker = new ServiceDescriptor(typeof(FakeMarker), typeof(FakeMarker), ServiceLifetime.Singleton);
        var carrier = ServiceDescriptor.Singleton(registration);

        registration.CaptureAfterDecoration(ThrowOnEnumeration(() => enumerationAttempted = true), marker, carrier);

        Assert.False(enumerationAttempted);
        Assert.False(registration.IsEligible);
    }

    [Fact]
    public void Invalid_operation_during_recapture_clears_previously_valid_eligibility()
    {
        var composition = Decorate(BuildComposition());
        Assert.True(composition.Registration.IsEligible);
        var enumerationAttempted = false;

        composition.Registration.CaptureAfterDecoration(
            ThrowOnEnumeration(() => enumerationAttempted = true),
            composition.Marker,
            composition.Carrier);

        Assert.True(enumerationAttempted);
        Assert.False(composition.Registration.IsEligible);
    }

    [Theory]
    [InlineData("no-durable-decoration")]
    [InlineData("wrong-wrapper-type")]
    [InlineData("wrong-inner-lifetime")]
    [InlineData("wrong-wrapper-lifetime")]
    [InlineData("wrong-inner-non-generic-type")]
    [InlineData("wrong-inner-generic-argument")]
    [InlineData("inner-marker-collision")]
    [InlineData("missing-marker")]
    [InlineData("replaced-marker")]
    [InlineData("missing-carrier")]
    [InlineData("replaced-carrier")]
    public void Incomplete_or_malformed_decoration_fails_closed(string scenario)
    {
        var composition = Decorate(BuildComposition(), scenario);

        Assert.True(composition.Registration.EligibleBeforeDecoration);
        Assert.False(composition.Registration.IsEligible);
    }

    [Fact]
    public void Existing_owned_inner_service_collision_fails_closed()
    {
        var raw = BuildComposition();
        // This pre-existing owned descriptor collides with the inner key the coalescing decorator will add.
        var existingInner = new ServiceDescriptor(
            typeof(CoalescingInner<IDurableValueStateStore>),
            _ => new CoalescingInner<IDurableValueStateStore>(new OwnedDurableValueStore()),
            ServiceLifetime.Scoped);
        raw.Services.Add(existingInner);
        ReplaceBackend(raw, RuntimeOperationalStateStoreBackend.EntityFramework,
            raw.ConcreteDescriptor, raw.DurableDescriptor, existingInner);

        var composition = Decorate(raw);

        Assert.True(composition.Registration.EligibleBeforeDecoration);
        Assert.False(composition.Registration.IsEligible);
    }

    [Fact]
    public void Duplicate_owned_scheduler_group_fails_closed_during_decoration()
    {
        var raw = BuildComposition();
        var first = ServiceDescriptor.Scoped<ISchedulerStateStore, InMemorySchedulerStateStore>();
        var selected = ServiceDescriptor.Scoped<ISchedulerStateStore, InMemorySchedulerStateStore>();
        raw.Services.Add(first);
        raw.Services.Add(selected);
        ReplaceBackend(raw, RuntimeOperationalStateStoreBackend.EntityFramework,
            raw.ConcreteDescriptor, raw.DurableDescriptor, first, selected);

        // The extension decorates the last registration, while the prior duplicate keeps the group ambiguous.
        var composition = Decorate(raw, additionalSchedulerDecoration: selected);

        Assert.True(composition.Registration.EligibleBeforeDecoration);
        Assert.False(composition.Registration.IsEligible);
    }

    [Theory]
    [InlineData("remove-inner")]
    [InlineData("replace-inner")]
    [InlineData("remove-wrapper")]
    [InlineData("replace-wrapper")]
    [InlineData("remove-backend")]
    [InlineData("replace-backend")]
    [InlineData("remove-codec")]
    [InlineData("replace-codec")]
    [InlineData("remove-accessor")]
    [InlineData("replace-accessor")]
    [InlineData("remove-concrete")]
    [InlineData("replace-concrete")]
    [InlineData("keyed-concrete")]
    [InlineData("keyed-accessor")]
    [InlineData("keyed-codec")]
    [InlineData("remove-marker")]
    [InlineData("replace-marker")]
    [InlineData("remove-carrier")]
    [InlineData("replace-carrier")]
    [InlineData("duplicate-durable-contract")]
    [InlineData("second-backend-instance")]
    [InlineData("unowned-backend-factory")]
    public void Changes_to_captured_post_decoration_groups_disable_reuse(string scenario)
    {
        var composition = Decorate(BuildComposition());
        Assert.True(composition.Registration.IsEligible);

        ApplyPostDecorationChange(composition, scenario);

        Assert.False(composition.Registration.IsEligible);
    }

    private static RawComposition BuildPreDecorationCase(string scenario)
    {
        var raw = BuildComposition();
        var services = raw.Services;

        switch (scenario)
        {
            case "missing-backend":
                services.Remove(raw.BackendDescriptor!);
                break;
            case "duplicate-backend":
                services.Add(ServiceDescriptor.Singleton(new RuntimeOperationalStateStoreBackend(
                    RuntimeOperationalStateStoreBackend.InMemory,
                    [raw.ConcreteDescriptor, raw.DurableDescriptor])));
                break;
            case "keyed-backend":
                services.Remove(raw.BackendDescriptor!);
                services.AddKeyedSingleton("alternate", raw.Backend);
                break;
            case "factory-backend":
                services.Remove(raw.BackendDescriptor!);
                services.AddSingleton<RuntimeOperationalStateStoreBackend>(_ => raw.Backend);
                break;
            case "wrong-backend-service-type":
            {
                services.Remove(raw.BackendDescriptor!);
                var wrongBackendDescriptor = ServiceDescriptor.Singleton(typeof(object), raw.Backend);
                services.Add(wrongBackendDescriptor);
                raw.BackendDescriptor = wrongBackendDescriptor;
                break;
            }
            case "in-memory-backend":
                services.Remove(raw.BackendDescriptor!);
                services.Add(ServiceDescriptor.Singleton(new RuntimeOperationalStateStoreBackend(
                    RuntimeOperationalStateStoreBackend.InMemory,
                    [raw.ConcreteDescriptor, raw.DurableDescriptor])));
                break;
            case "duplicate-owned-durable-contract":
            {
                var duplicate = ServiceDescriptor.Scoped<IDurableValueStateStore>(_ => new OwnedDurableValueStore());
                services.Add(duplicate);
                ReplaceBackend(raw, RuntimeOperationalStateStoreBackend.EntityFramework,
                    raw.ConcreteDescriptor, raw.DurableDescriptor, duplicate);
                break;
            }
            case "keyed-owned-durable-contract":
            {
                services.Remove(raw.DurableDescriptor);
                var keyed = ServiceDescriptor.DescribeKeyed(
                    typeof(IDurableValueStateStore), "alternate", typeof(OwnedDurableValueStore), ServiceLifetime.Scoped);
                services.Add(keyed);
                ReplaceBackend(raw, RuntimeOperationalStateStoreBackend.EntityFramework,
                    raw.ConcreteDescriptor, keyed);
                break;
            }
            case "keyed-owned-other-contract":
            {
                var keyed = ServiceDescriptor.DescribeKeyed(
                    typeof(ISchedulerStateStore), "alternate", typeof(InMemorySchedulerStateStore), ServiceLifetime.Scoped);
                services.Add(keyed);
                ReplaceBackend(raw, RuntimeOperationalStateStoreBackend.EntityFramework,
                    raw.ConcreteDescriptor, raw.DurableDescriptor, keyed);
                break;
            }
            case "interface-only-backend":
                services.Remove(raw.ConcreteDescriptor);
                ReplaceBackend(raw, RuntimeOperationalStateStoreBackend.EntityFramework, raw.DurableDescriptor);
                break;
            case "missing-owned-concrete":
                services.Remove(raw.ConcreteDescriptor);
                break;
            case "duplicate-owned-concrete":
                services.Add(raw.ConcreteDescriptor);
                break;
            case "concrete-override":
                services.Add(new ServiceDescriptor(typeof(OwnedDurableValueStore), typeof(OwnedDurableValueStore), ServiceLifetime.Scoped));
                break;
            case "unowned-contract-override":
                services.AddScoped<IDurableValueStateStore>(_ => new OwnedDurableValueStore());
                break;
            case "missing-accessor":
                services.Remove(raw.AccessorDescriptor!);
                break;
            case "duplicate-accessor":
                services.Add(new ServiceDescriptor(typeof(IPersistenceAccessContextAccessor), typeof(FakeAccessor), ServiceLifetime.Scoped));
                break;
            case "keyed-accessor":
                ReplaceAccessor(raw, ServiceDescriptor.DescribeKeyed(
                    typeof(IPersistenceAccessContextAccessor), "alternate", typeof(FakeAccessor), ServiceLifetime.Scoped));
                break;
            case "transient-accessor":
                ReplaceAccessor(raw, new ServiceDescriptor(typeof(IPersistenceAccessContextAccessor), typeof(FakeAccessor), ServiceLifetime.Transient));
                break;
            case "missing-codec":
                services.Remove(raw.CodecDescriptor!);
                break;
            case "duplicate-codec":
                services.Add(new ServiceDescriptor(typeof(IRuntimeRecoveryContinuationCodec), typeof(HmacRuntimeRecoveryContinuationCodec), ServiceLifetime.Singleton));
                break;
            case "keyed-codec":
                ReplaceCodec(raw, ServiceDescriptor.DescribeKeyed(
                    typeof(IRuntimeRecoveryContinuationCodec), "alternate", typeof(HmacRuntimeRecoveryContinuationCodec), ServiceLifetime.Singleton));
                break;
            case "factory-codec":
                ReplaceCodec(raw, new ServiceDescriptor(typeof(IRuntimeRecoveryContinuationCodec), _ => CreateCodec(), ServiceLifetime.Singleton));
                break;
            case "custom-codec":
                ReplaceCodec(raw, new ServiceDescriptor(typeof(IRuntimeRecoveryContinuationCodec), typeof(FakeCodec), ServiceLifetime.Singleton));
                break;
            case "scoped-codec":
                ReplaceCodec(raw, new ServiceDescriptor(typeof(IRuntimeRecoveryContinuationCodec), typeof(HmacRuntimeRecoveryContinuationCodec), ServiceLifetime.Scoped));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown pre-decoration case.");
        }

        return raw;
    }

    private static RawComposition BuildComposition()
    {
        IServiceCollection services = new ServiceCollection();
        var concreteDescriptor = new ServiceDescriptor(
            typeof(OwnedDurableValueStore), typeof(OwnedDurableValueStore), ServiceLifetime.Scoped);
        var durableDescriptor = ServiceDescriptor.Scoped<IDurableValueStateStore>(provider =>
            provider.GetRequiredService<OwnedDurableValueStore>());
        services.Add(concreteDescriptor);
        services.Add(durableDescriptor);

        var backend = new RuntimeOperationalStateStoreBackend(
            RuntimeOperationalStateStoreBackend.EntityFramework,
            [concreteDescriptor, durableDescriptor]);
        var backendDescriptor = ServiceDescriptor.Singleton(backend);
        services.Add(backendDescriptor);

        var accessorDescriptor = new ServiceDescriptor(
            typeof(IPersistenceAccessContextAccessor), typeof(FakeAccessor), ServiceLifetime.Scoped);
        services.Add(accessorDescriptor);
        var codecDescriptor = new ServiceDescriptor(
            typeof(IRuntimeRecoveryContinuationCodec), typeof(HmacRuntimeRecoveryContinuationCodec), ServiceLifetime.Singleton);
        services.Add(codecDescriptor);

        return new(services, concreteDescriptor, durableDescriptor, backend, backendDescriptor, accessorDescriptor, codecDescriptor);
    }

    private static DecoratedComposition Decorate(
        RawComposition raw,
        string scenario = "valid",
        ServiceDescriptor? additionalSchedulerDecoration = null)
    {
        var registration = RuntimeCoalescingDurableValuePageReuseRegistration.CaptureBeforeDecoration(raw.Services);
        var services = raw.Services;
        var decorations = new List<RuntimeCoalescingDurableValuePageReuseRegistration.ServiceDecoration>();
        ServiceDescriptor? inner = null;
        ServiceDescriptor? wrapper = null;

        if (scenario != "no-durable-decoration")
        {
            services.Remove(raw.DurableDescriptor);
            var innerServiceType = scenario switch
            {
                "wrong-inner-non-generic-type" or "inner-marker-collision" => typeof(FakeMarker),
                "wrong-inner-generic-argument" => typeof(CoalescingInner<IPersistenceAccessContextAccessor>),
                _ => typeof(CoalescingInner<IDurableValueStateStore>)
            };
            var innerLifetime = scenario == "wrong-inner-lifetime" ? ServiceLifetime.Singleton : raw.DurableDescriptor.Lifetime;
            inner = new ServiceDescriptor(
                innerServiceType,
                _ => new CoalescingInner<IDurableValueStateStore>(new OwnedDurableValueStore()),
                innerLifetime);
            var wrapperServiceType = scenario == "wrong-wrapper-type" ? typeof(FakeMarker) : typeof(IDurableValueStateStore);
            var wrapperLifetime = scenario == "wrong-wrapper-lifetime" ? ServiceLifetime.Singleton : raw.DurableDescriptor.Lifetime;
            wrapper = new ServiceDescriptor(wrapperServiceType, _ => new OwnedDurableValueStore(), wrapperLifetime);
            services.Add(inner);
            services.Add(wrapper);
            decorations.Add(new(raw.DurableDescriptor, inner, wrapper));
        }

        if (additionalSchedulerDecoration is not null)
        {
            services.Remove(additionalSchedulerDecoration);
            var additionalInner = new ServiceDescriptor(
                typeof(CoalescingInner<ISchedulerStateStore>),
                _ => new CoalescingInner<ISchedulerStateStore>(new InMemorySchedulerStateStore()),
                additionalSchedulerDecoration.Lifetime);
            var additionalWrapper = new ServiceDescriptor(
                typeof(ISchedulerStateStore),
                _ => new InMemorySchedulerStateStore(),
                additionalSchedulerDecoration.Lifetime);
            services.Add(additionalInner);
            services.Add(additionalWrapper);
            decorations.Add(new(additionalSchedulerDecoration, additionalInner, additionalWrapper));
        }

        var marker = new ServiceDescriptor(typeof(FakeMarker), typeof(FakeMarker), ServiceLifetime.Singleton);
        if (scenario == "inner-marker-collision")
            marker = inner!;
        else if (scenario != "missing-marker")
            services.Add(scenario == "replaced-marker"
                ? new ServiceDescriptor(typeof(FakeMarker), typeof(FakeMarker), ServiceLifetime.Singleton)
                : marker);

        var carrier = new ServiceDescriptor(typeof(RuntimeCoalescingDurableValuePageReuseRegistration), registration);
        if (scenario != "missing-carrier")
            services.Add(scenario == "replaced-carrier"
                ? new ServiceDescriptor(typeof(RuntimeCoalescingDurableValuePageReuseRegistration), registration)
                : carrier);

        registration.CaptureAfterDecoration(decorations, marker, carrier);
        return new(raw, registration, inner, wrapper, marker, carrier);
    }

    private static void ApplyPostDecorationChange(DecoratedComposition composition, string scenario)
    {
        var services = composition.Raw.Services;
        var raw = composition.Raw;

        switch (scenario)
        {
            case "remove-inner":
                services.Remove(composition.Inner!);
                break;
            case "replace-inner":
                services.Remove(composition.Inner!);
                services.Add(new ServiceDescriptor(composition.Inner!.ServiceType,
                    _ => new CoalescingInner<IDurableValueStateStore>(new OwnedDurableValueStore()), composition.Inner.Lifetime));
                break;
            case "remove-wrapper":
                services.Remove(composition.Wrapper!);
                break;
            case "replace-wrapper":
                services.Remove(composition.Wrapper!);
                services.Add(new ServiceDescriptor(typeof(IDurableValueStateStore), _ => new OwnedDurableValueStore(), raw.DurableDescriptor.Lifetime));
                break;
            case "remove-backend":
                services.Remove(raw.BackendDescriptor!);
                break;
            case "replace-backend":
                services.Remove(raw.BackendDescriptor!);
                services.Add(ServiceDescriptor.Singleton(new RuntimeOperationalStateStoreBackend(
                    RuntimeOperationalStateStoreBackend.EntityFramework,
                    [raw.ConcreteDescriptor, raw.DurableDescriptor])));
                break;
            case "remove-codec":
                services.Remove(raw.CodecDescriptor!);
                break;
            case "replace-codec":
                ReplaceCodec(raw, new ServiceDescriptor(typeof(IRuntimeRecoveryContinuationCodec), typeof(HmacRuntimeRecoveryContinuationCodec), ServiceLifetime.Singleton));
                break;
            case "remove-accessor":
                services.Remove(raw.AccessorDescriptor!);
                break;
            case "replace-accessor":
                ReplaceAccessor(raw, new ServiceDescriptor(typeof(IPersistenceAccessContextAccessor), typeof(FakeAccessor), ServiceLifetime.Scoped));
                break;
            case "remove-concrete":
                services.Remove(raw.ConcreteDescriptor);
                break;
            case "replace-concrete":
                services.Remove(raw.ConcreteDescriptor);
                services.Add(new ServiceDescriptor(typeof(OwnedDurableValueStore), typeof(OwnedDurableValueStore), ServiceLifetime.Scoped));
                break;
            case "keyed-concrete":
                services.Add(ServiceDescriptor.DescribeKeyed(typeof(OwnedDurableValueStore), "alternate", typeof(OwnedDurableValueStore), ServiceLifetime.Scoped));
                break;
            case "keyed-accessor":
                services.Add(ServiceDescriptor.DescribeKeyed(typeof(IPersistenceAccessContextAccessor), "alternate", typeof(FakeAccessor), ServiceLifetime.Scoped));
                break;
            case "keyed-codec":
                services.Add(ServiceDescriptor.DescribeKeyed(typeof(IRuntimeRecoveryContinuationCodec), "alternate", typeof(HmacRuntimeRecoveryContinuationCodec), ServiceLifetime.Singleton));
                break;
            case "remove-marker":
                services.Remove(composition.Marker);
                break;
            case "replace-marker":
                services.Remove(composition.Marker);
                services.Add(new ServiceDescriptor(typeof(FakeMarker), typeof(FakeMarker), ServiceLifetime.Singleton));
                break;
            case "remove-carrier":
                services.Remove(composition.Carrier);
                break;
            case "replace-carrier":
                services.Remove(composition.Carrier);
                services.Add(new ServiceDescriptor(typeof(RuntimeCoalescingDurableValuePageReuseRegistration), composition.Registration));
                break;
            case "duplicate-durable-contract":
                services.AddScoped<IDurableValueStateStore>(_ => new OwnedDurableValueStore());
                break;
            case "second-backend-instance":
                services.Add(ServiceDescriptor.Singleton(new RuntimeOperationalStateStoreBackend(
                    RuntimeOperationalStateStoreBackend.EntityFramework,
                    [raw.ConcreteDescriptor, raw.DurableDescriptor])));
                break;
            case "unowned-backend-factory":
                services.AddSingleton<RuntimeOperationalStateStoreBackend>(_ => raw.Backend);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown post-decoration case.");
        }
    }

    private static void ReplaceBackend(RawComposition raw, string name, params ServiceDescriptor[] ownedDescriptors)
    {
        raw.Services.Remove(raw.BackendDescriptor!);
        var backend = new RuntimeOperationalStateStoreBackend(name, ownedDescriptors);
        var descriptor = ServiceDescriptor.Singleton(backend);
        raw.Services.Add(descriptor);
        raw.Backend = backend;
        raw.BackendDescriptor = descriptor;
    }

    private static void ReplaceAccessor(RawComposition raw, ServiceDescriptor replacement)
    {
        raw.Services.Remove(raw.AccessorDescriptor!);
        raw.Services.Add(replacement);
        raw.AccessorDescriptor = replacement;
    }

    private static void ReplaceCodec(RawComposition raw, ServiceDescriptor replacement)
    {
        raw.Services.Remove(raw.CodecDescriptor!);
        raw.Services.Add(replacement);
        raw.CodecDescriptor = replacement;
    }

    private static IRuntimeRecoveryContinuationCodec CreateCodec() => new HmacRuntimeRecoveryContinuationCodec(
        Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));

    private static IEnumerable<RuntimeCoalescingDurableValuePageReuseRegistration.ServiceDecoration> ThrowOnEnumeration(Action onEnumeration) =>
        Enumerable.Repeat(default(RuntimeCoalescingDurableValuePageReuseRegistration.ServiceDecoration), 1)
            .Select<RuntimeCoalescingDurableValuePageReuseRegistration.ServiceDecoration,
                RuntimeCoalescingDurableValuePageReuseRegistration.ServiceDecoration>(_ =>
            {
                onEnumeration();
                throw new InvalidOperationException("Simulated service collection mutation during enumeration.");
            });

    private sealed class RawComposition(
        IServiceCollection services,
        ServiceDescriptor concreteDescriptor,
        ServiceDescriptor durableDescriptor,
        RuntimeOperationalStateStoreBackend backend,
        ServiceDescriptor? backendDescriptor,
        ServiceDescriptor? accessorDescriptor,
        ServiceDescriptor? codecDescriptor)
    {
        public IServiceCollection Services { get; } = services;
        public ServiceDescriptor ConcreteDescriptor { get; } = concreteDescriptor;
        public ServiceDescriptor DurableDescriptor { get; } = durableDescriptor;
        public RuntimeOperationalStateStoreBackend Backend { get; set; } = backend;
        public ServiceDescriptor? BackendDescriptor { get; set; } = backendDescriptor;
        public ServiceDescriptor? AccessorDescriptor { get; set; } = accessorDescriptor;
        public ServiceDescriptor? CodecDescriptor { get; set; } = codecDescriptor;
    }

    private sealed record DecoratedComposition(
        RawComposition Raw,
        RuntimeCoalescingDurableValuePageReuseRegistration Registration,
        ServiceDescriptor? Inner,
        ServiceDescriptor? Wrapper,
        ServiceDescriptor Marker,
        ServiceDescriptor Carrier);

    private sealed class FakeAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope("tenant"));
    }

    private sealed class FakeCodec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }

    private sealed class OwnedDurableValueStore : IDurableValueStateStore
    {
        public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(DurableValueStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMarker;
}
