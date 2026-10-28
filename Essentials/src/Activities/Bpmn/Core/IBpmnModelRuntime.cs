using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Supported model publication, execution and human-work commands used by the BPMN API.</summary>
public interface IBpmnModelRuntime
{
    ValueTask<BpmnPublishedModel> PublishAsync(BpmnModelDefinition definition, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    ValueTask<BpmnModelCommandOutcome> StartAsync(string sourceReferenceId, string requestId,
        IReadOnlyDictionary<string, JsonElement>? inputs, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    ValueTask<RuntimeModelExecutionState?> FindExecutionAsync(string executionId, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    ValueTask<RuntimeStorePage<BpmnHumanWork>> ListWorkAsync(string executionId, RuntimeStorePageRequest request,
        BpmnModelCaller caller, CancellationToken cancellationToken = default);
    ValueTask<BpmnModelCommandOutcome> CompleteHumanAsync(string executionId, string requestId, string invocationId,
        string attemptId, string[]? outcomes, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    ValueTask<BpmnModelCommandOutcome?> FindCommandAsync(string executionId, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken = default);
}

/// <summary>Captured by the authenticated transport; execution authority is never bound from request JSON.</summary>
public sealed record BpmnModelCaller(string ActorId, string? TenantId);

public sealed record BpmnPublishedModel(WorkflowExecutableIdentity Executable, string IncarnationId,
    string SourceReferenceId, string Profile);

/// <summary>A retained input result or durable admission still awaiting consumption.</summary>
public sealed record BpmnModelCommandOutcome(string ExecutionId, string InputId, bool Replay, bool Pending, JsonElement? Result);

public sealed record BpmnHumanWork(string ElementId, string BindingId, string TokenId, string? IterationId,
    string InvocationId, string AttemptId);

/// <summary>Missing or foreign-scope model resource. Transports return the same absence response.</summary>
public sealed class BpmnModelResourceMissingException() : Exception("The BPMN resource is not available.");

/// <summary>A new model definition or supported command cannot be interpreted by this hosting profile.</summary>
public sealed class BpmnModelValidationException(string message, Exception? innerException = null) : Exception(message, innerException);
