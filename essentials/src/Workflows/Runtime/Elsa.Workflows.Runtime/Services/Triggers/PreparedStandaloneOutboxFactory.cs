using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class PreparedStandaloneOutboxFactory
{
    internal static IRuntimePostCommitOutboxProcessor Create(IServiceProvider provider) =>
        new RuntimePostCommitOutboxProcessor(
            provider.GetRequiredService<IRuntimePostCommitOutboxStore>(),
            provider.GetRequiredService<IRuntimePostCommitIntentDispatcher>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IRuntimeFaultCapturePolicy>(),
            workflowDispatchStore: null,
            deliveryFailureProjectors: [],
            logger: provider.GetService<ILogger<RuntimePostCommitOutboxProcessor>>(),
            liveDrainDeliveryAccessor: null,
            coalescingSessionAccessor: null,
            persistenceAccessContextAccessor: provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            contributions: provider.GetServices<RuntimePostCommitIntentHandlerContribution>());
}
