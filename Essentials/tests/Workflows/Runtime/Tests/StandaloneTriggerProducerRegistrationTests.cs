using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StandaloneTriggerProducerRegistrationTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Multiple_bound_Event_producers_keep_distinct_ports_and_resolve_one_type_activation_capability(bool distinctFamily)
    {
        var services = new ServiceCollection();
        var schema = new ValueTypeDescriptor("Test.Event", schemaVersion: 1);
        services.AddStandaloneTriggerIngress<FirstSource>(new("event", 1, "First.Event", schema));
        services.AddStandaloneTriggerIngress<FirstSource>(new("event", 1, "First.Event", schema));
        if (!distinctFamily)
        {
            Assert.Throws<InvalidOperationException>(() => services.AddStandaloneTriggerIngress<SecondSource>(new("event", 1, "First.Event", schema)));
            return;
        }
        services.AddStandaloneTriggerIngress<SecondSource>(new("event", 1, "Second.Event", schema));
        services.AddSingleton<IStandaloneTriggerStore>(new InMemoryStandaloneTriggerStore(new InMemoryRuntimeCheckpointStoreState(), new Access(), TimeProvider.System));
        services.AddSingleton<ITriggerSourceResourceCommitStore, SupportedPhysical>();
        services.AddSingleton<IRuntimeIngressDeliveryStore, SupportedIngress>();
        services.AddScoped<ITriggerManagementCommitter, ProtectedTriggerManagementCommitter>();
        services.AddSingleton<IStandaloneTriggerIngressFactory, CapturingFactory>();
        services.AddScoped<IStandaloneTriggerSourceContext<FirstSource>, Context<FirstSource>>();
        services.AddScoped<IStandaloneTriggerSourceContext<SecondSource>, Context<SecondSource>>();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();

        var management = scope.ServiceProvider.GetRequiredService<ITriggerManagementCommitter>();

        Assert.True(management.CanActivateType("event", 1));
        Assert.False(management.CanActivateType("event", 2));
        Assert.False(management.CanActivateType("http", 1));
        Assert.Equal(2, provider.GetServices<StandaloneTriggerSourceDescriptor>().Count());
        var first = Assert.IsType<CapturedPort<FirstSource>>(scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngress<FirstSource>>());
        var second = Assert.IsType<CapturedPort<SecondSource>>(scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngress<SecondSource>>());
        Assert.Equal("First.Event", first.Descriptor.SourceFamily);
        Assert.Equal(distinctFamily ? "Second.Event" : "First.Event", second.Descriptor.SourceFamily);
    }

    private sealed class FirstSource;
    private sealed class SecondSource;
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("physical-scope")); }
    private sealed class SupportedPhysical : ITriggerSourceResourceCommitStore
    {
        public bool SupportsActivatedResources => true;
        public ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write,
            IReadOnlyList<RuntimeIngressReservationDescriptor> reservations, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Capability discovery must not commit a resource.");
    }
    private sealed class SupportedIngress : IRuntimeIngressDeliveryStore { public bool SupportsAdmission => true; }
    private sealed class Context<TSource> : IStandaloneTriggerSourceContext<TSource> where TSource : class
    {
        public bool SupportsResourceAuthorization => true;
        public ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Capability discovery must not invoke a resource policy callback.");
        public ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Producer registration must not invoke a source.");
    }
    private sealed class CapturingFactory(IServiceProvider services) : IStandaloneTriggerIngressFactory
    {
        public IStandaloneTriggerIngress<TSource> Create<TSource>() where TSource : class => new CapturedPort<TSource>(services.GetRequiredKeyedService<StandaloneTriggerSourceDescriptor>(typeof(TSource)));
    }
    private sealed class CapturedPort<TSource>(StandaloneTriggerSourceDescriptor descriptor) : IStandaloneTriggerIngress<TSource> where TSource : class
    {
        internal StandaloneTriggerSourceDescriptor Descriptor => descriptor;
        public ValueTask<StandaloneTriggerIngressResult> AdmitAsync(StandaloneTriggerSignal signal, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Producer registration must not admit a source.");
    }
}
