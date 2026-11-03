namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A retained dispatch address resolved by the control-boundary service. Constructing it grants no authority.</summary>
public sealed class RuntimeChildControlTarget
{
    public RuntimeChildControlTarget(string parentWorkflowExecutionId, string callingActivityExecutionId,
        string consumerActivityExecutionId, WorkflowExecutableIdentity parentExecutable, string rootActivityExecutionId,
        string consumerKey, int consumerVersion)
    {
        RuntimeChildControlValidation.Text(parentWorkflowExecutionId, nameof(parentWorkflowExecutionId));
        RuntimeChildControlValidation.Text(callingActivityExecutionId, nameof(callingActivityExecutionId));
        RuntimeChildControlValidation.Text(consumerActivityExecutionId, nameof(consumerActivityExecutionId));
        RuntimeChildControlValidation.Pin(parentExecutable);
        RuntimeChildControlValidation.Text(rootActivityExecutionId, nameof(rootActivityExecutionId));
        RuntimeChildControlValidation.Text(consumerKey, nameof(consumerKey), 128);
        if (consumerVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(consumerVersion));
        if (StringComparer.Ordinal.Equals(callingActivityExecutionId, consumerActivityExecutionId))
            throw new ArgumentException("A dispatch invocation cannot consume its own parent control.", nameof(consumerActivityExecutionId));
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, callingActivityExecutionId);
        DispatchId = identity.DispatchId;
        ChildWorkflowExecutionId = identity.ChildWorkflowExecutionId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        CallingActivityExecutionId = callingActivityExecutionId;
        ConsumerActivityExecutionId = consumerActivityExecutionId;
        ParentExecutable = parentExecutable;
        RootActivityExecutionId = rootActivityExecutionId;
        ConsumerKey = consumerKey;
        ConsumerVersion = consumerVersion;
    }

    public string DispatchId { get; }
    public string ChildWorkflowExecutionId { get; }
    public string ParentWorkflowExecutionId { get; }
    public string CallingActivityExecutionId { get; }
    public string ConsumerActivityExecutionId { get; }
    public WorkflowExecutableIdentity ParentExecutable { get; }
    public string RootActivityExecutionId { get; }
    public string ConsumerKey { get; }
    public int ConsumerVersion { get; }

    /// <summary>Compares stream address data only; a match does not validate committed source authority.</summary>
    public bool MatchesAddress(RuntimeChildControlTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return transport.DispatchId == DispatchId && transport.ParentWorkflowExecutionId == ParentWorkflowExecutionId &&
               transport.CallingActivityExecutionId == CallingActivityExecutionId &&
               transport.ConsumerActivityExecutionId == ConsumerActivityExecutionId &&
               WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(transport.ParentExecutable, ParentExecutable) &&
               transport.Batch.ChildWorkflowExecutionId == ChildWorkflowExecutionId &&
               transport.Batch.RootActivityExecutionId == RootActivityExecutionId &&
               transport.Batch.ConsumerKey == ConsumerKey && transport.Batch.ConsumerVersion == ConsumerVersion;
    }

}
