using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>Localizes a provider failure from EF Runtime history reads, observation writes or explicit retention.</summary>
/// <remarks>
/// This Runtime-owned boundary deliberately derives from <see cref="IOException"/>: history callers already
/// distinguish dependency I/O from hard argument, policy and integrity violations. No concrete provider exception
/// is required by callers, and the original provider failure remains available as <see cref="Exception.InnerException"/>.
/// This exception does not establish whether an interrupted commit became durable; callers retain their existing
/// replay and acknowledgement rules. The named sibling stays internal because consumers classify the existing
/// I/O family rather than depending on a concrete EF provider type.
/// </remarks>
/// <seealso cref="IRuntimeHistoryStore"/>
/// <seealso cref="IRuntimeHistoryObservationWriter"/>
/// <seealso cref="IRuntimeHistoryRetentionStore"/>
internal sealed class RuntimeHistoryEntityFrameworkPersistenceException(
    string operation,
    string scope,
    string workflowExecutionId,
    Exception innerException) : IOException(
        $"The EF Runtime history store failed while {operation} for workflow execution '{workflowExecutionId}' in scope '{scope}'.", innerException)
{
    public string Operation { get; } = operation;
    public string Scope { get; } = scope;
    public string WorkflowExecutionId { get; } = workflowExecutionId;
}
