namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>
/// Reports that an activity-result binding could not identify exactly one committed producer
/// invocation in the consumer's structural frame and causal scheduling lineage.
/// </summary>
public sealed class CausalActivityResultResolutionException : InvalidOperationException
{
    public CausalActivityResultResolutionException(
        string message,
        CausalActivityResultResolutionFailureReason reason,
        string consumerInvocationId,
        string producerExecutableNodeId,
        string projectionKey,
        string producerScopeId,
        IReadOnlyCollection<string>? candidateInvocationIds = null)
        : base(message)
    {
        Reason = reason;
        ConsumerInvocationId = consumerInvocationId;
        ProducerExecutableNodeId = producerExecutableNodeId;
        ProjectionKey = projectionKey;
        ProducerScopeId = producerScopeId;
        CandidateInvocationIds = candidateInvocationIds?.ToArray() ?? [];
    }

    public CausalActivityResultResolutionFailureReason Reason { get; }
    public string ConsumerInvocationId { get; }
    public string ProducerExecutableNodeId { get; }
    public string ProjectionKey { get; }
    public string ProducerScopeId { get; }
    public IReadOnlyCollection<string> CandidateInvocationIds { get; }
}

public enum CausalActivityResultResolutionFailureReason
{
    Unavailable,
    Ambiguous
}
