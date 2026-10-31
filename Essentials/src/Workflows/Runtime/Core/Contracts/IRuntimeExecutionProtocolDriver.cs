using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>A model-owned interpreter adapter. Resolved in the execution's partition-bound operation scope.</summary>
public interface IRuntimeExecutionProtocolDriver
{
    ValueTask ValidateMaterialAsync(RuntimeModelMaterial material, CancellationToken cancellationToken = default);
    ValueTask ValidateInputAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken = default);
    ValueTask ExecuteAsync(RuntimeModelExecutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>Returns protocol registrations to the single startup initialization aggregator.</summary>
public interface IRuntimeExecutionProtocolSource
{
    IEnumerable<RuntimeExecutionProtocolRegistration> GetProtocols();
}
