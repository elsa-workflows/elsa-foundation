using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// What a drain read of its run's workflow execution state when it began, under the run's ownership lease: the state,
/// or <see langword="null"/> when the store held none. It exists so that "read and found nothing" is not confused with
/// "not read".
/// </summary>
internal sealed record RuntimeRunStateObservation(WorkflowExecutionState? State);
