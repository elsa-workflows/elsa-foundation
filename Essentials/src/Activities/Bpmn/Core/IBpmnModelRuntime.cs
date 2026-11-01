using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Supported model publication, execution and human-work commands used by the BPMN API.</summary>
/// <remarks>Invalid owned definition, input or persisted model JSON raises <see cref="BpmnModelValidationException"/>
/// with material/execution context and the original JSON exception as its inner cause. Transport parsing is separate.</remarks>
public interface IBpmnModelRuntime
{
    ValueTask<BpmnPublishedModel> PublishAsync(BpmnModelDefinition definition, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    /// <summary>Publishes with trusted immutable authored provenance; raw publication keeps its existing identity and payload.</summary>
    ValueTask<BpmnPublishedModel> PublishFromSourceAsync(BpmnModelDefinition definition, BpmnModelPublicationSource source,
        string requestId, BpmnModelCaller caller, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<BpmnPublishedModel>(new NotSupportedException("This BPMN host does not support authored-source publication."));
    ValueTask<BpmnModelCommandOutcome> StartAsync(string sourceReferenceId, string requestId,
        IReadOnlyDictionary<string, JsonElement>? inputs, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    ValueTask<RuntimeModelExecutionState?> FindExecutionAsync(string executionId, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    ValueTask<RuntimeStorePage<BpmnHumanWork>> ListWorkAsync(string executionId, RuntimeStorePageRequest request,
        BpmnModelCaller caller, CancellationToken cancellationToken = default);
    ValueTask<BpmnModelCommandOutcome> CompleteHumanAsync(string executionId, string requestId, string invocationId,
        string attemptId, string[]? outcomes, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    /// <summary>Closes semantic progression and retains any native cancellation responsibility until its actual settlement.</summary>
    ValueTask<BpmnModelCommandOutcome> CancelAsync(string executionId, string requestId, string? reason,
        BpmnModelCaller caller, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<BpmnModelCommandOutcome>(new NotSupportedException("This BPMN host does not support cancellation."));
    ValueTask<BpmnModelCommandOutcome?> FindCommandAsync(string executionId, string requestId, BpmnModelCaller caller, CancellationToken cancellationToken = default);
    /// <summary>Inspects retained model-owned service failures without projecting them as native activities.</summary>
    ValueTask<RuntimeStorePage<BpmnServiceIncident>> ListIncidentsAsync(string executionId, RuntimeStorePageRequest request,
        BpmnModelCaller caller, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<RuntimeStorePage<BpmnServiceIncident>>(new NotSupportedException("This BPMN host does not support service incidents."));
    /// <summary>Retries only the exact current held service failure; identical request replay cannot create another attempt.</summary>
    ValueTask<BpmnModelCommandOutcome> RetryServiceAsync(string executionId, string requestId, string incidentId,
        string invocationId, string failedAttemptId, BpmnModelCaller caller, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<BpmnModelCommandOutcome>(new NotSupportedException("This BPMN host does not support service retry."));
}

/// <summary>Captured by the authenticated transport; execution authority is never bound from request JSON.</summary>
public sealed record BpmnModelCaller(string ActorId, string? TenantId);

public sealed record BpmnPublishedModel(WorkflowExecutableIdentity Executable, string IncarnationId,
    string SourceReferenceId, string Profile);

/// <summary>A retained input result or durable admission still awaiting consumption.</summary>
public sealed record BpmnModelCommandOutcome(string ExecutionId, string InputId, bool Replay, bool Pending, JsonElement? Result);

public sealed record BpmnHumanWork(string ElementId, string BindingId, string TokenId, string? IterationId,
    string InvocationId, string AttemptId);

/// <summary>Authorized service-failure inspection; failed worker evidence remains retained after resolution.</summary>
public sealed record BpmnServiceIncident(string IncidentId, string ElementId, string BindingId, string InvocationId,
    string FailedAttemptId, string WorkerExecutionId, string FaultCode, DateTimeOffset FailedAt,
    int AttemptNumber, int MaxAttempts, string Status, string? RetryAttemptId);

/// <summary>Missing or foreign-scope model resource. Transports return the same absence response.</summary>
public sealed class BpmnModelResourceMissingException() : Exception("The BPMN resource is not available.");

/// <summary>A new model definition or supported command cannot be interpreted by this hosting profile.</summary>
public sealed class BpmnModelValidationException(string message, Exception? innerException = null) : Exception(message, innerException);
