using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>Replacement contract: one mapper per runtime scope, using the ordinary declared workflow-input validator.</summary>
public interface ITriggerInputMapper
{
    /// <summary>Evaluates a detached source once. Successful normalized values are captured for delivery and never remapped on retry.</summary>
    ValueTask<WorkflowExecutableInputValidation> MapAsync(WorkflowExecutableInputContract contract,
        IReadOnlyList<TriggerInputMapping> mappings, JsonElement sourceEnvelope, CancellationToken cancellationToken = default);

    /// <summary>Checks declared destinations, literal values and required mapping coverage without executing expressions.</summary>
    WorkflowExecutableInputValidation ValidateMappings(WorkflowExecutableInputContract contract,
        IReadOnlyList<TriggerInputMapping> mappings);
}
