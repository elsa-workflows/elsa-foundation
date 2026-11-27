using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Reads or drives an exact wait on the isolated actor of one authoring Test Run.</summary>
public interface IWorkflowTestRunWaitControl
{
    ValueTask<IReadOnlyList<WorkflowTestRunActiveWait>> ReadAsync(WorkflowTestScope scope, string workflowExecutionId,
        CancellationToken cancellationToken = default);

    /// <summary>A receipt with no stimulus digest checks a previous delivery without delivering anything.</summary>
    ValueTask<string> DeliverAsync(WorkflowTestScope scope, string workflowExecutionId, TestRunDeliveryReceipt receipt,
        JsonElement? stimulus, CancellationToken cancellationToken = default);
}

public sealed record WorkflowTestRunActiveWait(string WaitId, string FrameId, string NodeId, string Event);
