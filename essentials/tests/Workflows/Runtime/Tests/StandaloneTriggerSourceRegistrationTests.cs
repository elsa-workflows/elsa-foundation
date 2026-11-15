using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StandaloneTriggerSourceRegistrationTests
{
    private static StandaloneTriggerSourceDescriptor Descriptor(string family = "First.Event") => new("event", 1, family, new ValueTypeDescriptor("Test.Event", schemaVersion: 1));

    [Fact]
    public void Producer_registration_replays_exactly_and_refuses_conflicting_identity_or_family()
    {
        var services = new ServiceCollection();
        services.AddStandaloneTriggerIngress<FirstSource>(Descriptor());
        var count = services.Count;
        services.AddStandaloneTriggerIngress<FirstSource>(Descriptor());
        Assert.Equal(count, services.Count);
        Assert.Throws<InvalidOperationException>(() => services.AddStandaloneTriggerIngress<FirstSource>(Descriptor("Changed.Event")));
        Assert.Throws<InvalidOperationException>(() => services.AddStandaloneTriggerIngress<SecondSource>(Descriptor()));
        services.AddStandaloneTriggerIngress<SecondSource>(Descriptor("Second.Event"));
        Assert.Equal(2, services.Count(item => item.ServiceType == typeof(StandaloneTriggerSourceDescriptor) && !item.IsKeyedService));
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(1, true, true)]
    [InlineData(2, true, false)]
    public void Capability_requires_one_authorizing_context_without_invoking_callbacks(int contextCount, bool authorizes, bool expected)
    {
        var services = new ServiceCollection();
        services.AddStandaloneTriggerIngress<FirstSource>(Descriptor());
        for (var i = 0; i < contextCount; i++) services.AddScoped<IStandaloneTriggerSourceContext<FirstSource>>(_ => new Context<FirstSource>(authorizes));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(expected, scope.ServiceProvider.GetRequiredService<StandaloneTriggerSourceCapability>().SupportsResourceAuthorization);
    }

    [Fact]
    public void Distinct_producers_resolve_their_own_typed_factory_port()
    {
        var services = new ServiceCollection();
        services.AddStandaloneTriggerIngress<FirstSource>(Descriptor());
        services.AddStandaloneTriggerIngress<SecondSource>(Descriptor("Second.Event"));
        services.AddScoped<IStandaloneTriggerIngressFactory, CapturingFactory>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var first = Assert.IsType<CapturedPort<FirstSource>>(scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngress<FirstSource>>());
        var second = Assert.IsType<CapturedPort<SecondSource>>(scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngress<SecondSource>>());
        Assert.Equal("First.Event", first.Descriptor.SourceFamily);
        Assert.Equal("Second.Event", second.Descriptor.SourceFamily);
        Assert.Same(first, scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngress<FirstSource>>());
    }

    [Fact]
    public void Source_executor_registration_preserves_protocol_and_refuses_owner_or_source_kind_conflicts()
    {
        var services = new ServiceCollection();
        services.AddRuntimeDeliverySourceExecutor<Executor>("Test.Source", RuntimePostCommitRetryPolicy.None);
        services.AddRuntimeDeliverySourceExecutor<Executor>("Test.Source", RuntimePostCommitRetryPolicy.None);
        using var provider = services.BuildServiceProvider();
        var contribution = Assert.Single(provider.GetServices<RuntimePostCommitIntentHandlerContribution>());
        Assert.Equal(RuntimeDeliverySourceKind.StandaloneIngress, contribution.SourceKind);
        Assert.Equal(RuntimeSubscriptionDelivery.CurrentVersion, contribution.SubscriptionDeliveryProtocolVersion);
        Assert.True(contribution.RequiresDurableSource);
        Assert.Null(contribution.StimulusDeliveryProtocolVersion);
        Assert.NotNull(provider.GetRequiredService<Executor>());
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimeDeliverySourceExecutor<OtherExecutor>("Test.Source", RuntimePostCommitRetryPolicy.None));
        Assert.Throws<InvalidOperationException>(() => services.AddRuntimePostCommitIntentHandler<LegacyHandler>("Test.Source"));
        Assert.Throws<ArgumentException>(() => new RuntimePostCommitIntentHandlerContribution("Test.Source", typeof(Executor), RuntimePostCommitRetryPolicy.None,
            true, null, RuntimeDeliverySourceKind.StandaloneIngress, RuntimeSubscriptionDelivery.CurrentVersion + 1));
    }

    private sealed class FirstSource;
    private sealed class SecondSource;
    private sealed class Context<TSource>(bool authorizes) : IStandaloneTriggerSourceContext<TSource> where TSource : class
    {
        public bool SupportsResourceAuthorization => authorizes;
        public ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Registration must not invoke a source.");
        public ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Discovery must not invoke policy.");
    }
    private sealed class CapturingFactory(IServiceProvider services) : IStandaloneTriggerIngressFactory
    {
        public IStandaloneTriggerIngress<TSource> Create<TSource>() where TSource : class => new CapturedPort<TSource>(services.GetRequiredKeyedService<StandaloneTriggerSourceDescriptor>(typeof(TSource)));
    }
    private sealed class CapturedPort<TSource>(StandaloneTriggerSourceDescriptor descriptor) : IStandaloneTriggerIngress<TSource> where TSource : class
    {
        internal StandaloneTriggerSourceDescriptor Descriptor => descriptor;
        public ValueTask<StandaloneTriggerIngressResult> AdmitAsync(StandaloneTriggerSignal signal, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Registration must not admit work.");
    }
    private sealed class Executor : IRuntimeDeliverySourceExecutor
    { public ValueTask HandleAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Registration must not execute."); }
    private sealed class OtherExecutor : IRuntimeDeliverySourceExecutor
    { public ValueTask HandleAsync(RuntimeDeliverySourceContext source, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Registration must not execute."); }
    private sealed class LegacyHandler : IRuntimePostCommitIntentHandler
    { public ValueTask HandleAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Registration must not execute."); }
}
