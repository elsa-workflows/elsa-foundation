using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Forces construction of the recovery continuation protector during shell startup so a host that composes durable
/// recovery paging without a stable signing key fails to activate instead of failing every recovery sweep. Recovery
/// is a background sweep: without this guard the misconfiguration only surfaces as a repeating resumption-sweep
/// failure that backs off to its maximum interval while no execution is ever recovered.
/// </summary>
public sealed class ValidateRuntimeRecoveryContinuationCodecStartupTask : IStartupTask
{
    public ValidateRuntimeRecoveryContinuationCodecStartupTask(IRuntimeRecoveryContinuationCodec _)
    {
    }

    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
