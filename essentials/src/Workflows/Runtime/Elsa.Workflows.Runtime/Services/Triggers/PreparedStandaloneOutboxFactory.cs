using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class PreparedStandaloneOutboxFactory
{
    internal static IRuntimePostCommitOutboxProcessor Create(IServiceProvider provider) =>
        StandaloneTriggerSourceRegistration.CreatePreparedOutbox(provider, provider.GetService<ILogger<RuntimePostCommitOutboxProcessor>>());
}
