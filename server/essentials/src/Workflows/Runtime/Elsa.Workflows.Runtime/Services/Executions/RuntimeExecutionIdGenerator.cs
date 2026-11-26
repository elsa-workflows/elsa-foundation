using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Time-ordered UUIDv7 source identities without a coordinated worker number.</summary>
internal sealed class RuntimeExecutionIdGenerator(TimeProvider timeProvider) : IRuntimeExecutionIdGenerator
{
    public string NewWorkflowExecutionId() => NewId(timeProvider.GetUtcNow());
    public string NewWorkflowExecutionCommandId() => NewId(timeProvider.GetUtcNow());
    public string NewWorkflowExecutionCommandEnvelopeId() => NewId(timeProvider.GetUtcNow());
    public string NewActivityExecutionId() => NewId(timeProvider.GetUtcNow());

    // Persisted identities remain opaque strings; deterministic successor derivation is deliberately unchanged.
    internal static string NewId(DateTimeOffset now) => Guid.CreateVersion7(now).ToString("N");
}
