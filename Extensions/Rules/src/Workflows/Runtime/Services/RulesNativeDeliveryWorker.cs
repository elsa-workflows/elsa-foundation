using Elsa.Rules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Rules.Workflows.Runtime.Services;

internal sealed class RulesNativeDeliveryWorker(
    IServiceScopeFactory scopes,
    RulesNativeDeliveryOptions options,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    ILogger<RulesNativeDeliveryWorker> logger)
    : RulesBackgroundWorker(scopes, options.MaxConcurrentDeliveries, options.PollInterval, clock, lifetime, logger)
{
    protected override ValueTask<bool> ExecuteOneAsync(
        IServiceProvider services,
        string workerId,
        CancellationToken cancellationToken) =>
        services.GetRequiredService<RulesNativeDeliveryExecution>().ExecuteOneAsync(workerId, cancellationToken);
}
