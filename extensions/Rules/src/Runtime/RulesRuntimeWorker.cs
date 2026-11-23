using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Rules.Runtime;

/// <summary>Host lifecycle adapter for bounded Rules rounds.</summary>
internal sealed class RulesRuntimeWorker(IServiceScopeFactory scopes, RulesRuntimeOptions options,
    TimeProvider clock, IHostApplicationLifetime lifetime, ILogger<RulesRuntimeWorker> logger)
    : RulesBackgroundWorker(scopes, options.MaxConcurrentAttempts, options.PollInterval, clock, lifetime, logger)
{
    protected override ValueTask<bool> ExecuteOneAsync(IServiceProvider services, string workerId, CancellationToken cancellationToken) =>
        new(services.GetRequiredService<RuleExecution>().ExecuteOneAsync(workerId, cancellationToken));
}
