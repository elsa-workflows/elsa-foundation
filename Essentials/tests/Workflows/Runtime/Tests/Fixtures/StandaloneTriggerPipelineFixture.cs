using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Triggers;

namespace Elsa.Workflows.Runtime.Tests.Fixtures;

/// <summary>Actual processing implementations for preparation tests; capability discovery must not resolve or execute a handler.</summary>
internal static class StandaloneTriggerPipelineFixture
{
    internal static (IRuntimePostCommitOutboxProcessor Processor, IRuntimePostCommitIntentDispatcher Dispatcher) Create(
        IRuntimePostCommitOutboxStore outbox, IPersistenceAccessContextAccessor access, TimeProvider clock)
    {
        RuntimePostCommitIntentHandlerContribution[] contributions =
        [new(RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind, typeof(StandaloneTriggerDeliveryHandler),
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)), true, null,
            RuntimeDeliverySourceKind.StandaloneIngress, RuntimeSubscriptionDelivery.CurrentVersion)];
        var dispatcher = new RuntimePostCommitIntentDispatcher(contributions, new NoHandlerResolution());
        var processor = new RuntimePostCommitOutboxProcessor(outbox, dispatcher, clock, DefaultRuntimeFaultCapturePolicy.CreateDefault(),
            null, [], null, null, null, access, contributions);
        return (processor, dispatcher);
    }

    private sealed class NoHandlerResolution : IServiceProvider
    {
        public object? GetService(Type serviceType) => throw new InvalidOperationException("Preparation must not resolve a delivery handler.");
    }
}
