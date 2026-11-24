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
    /// <summary>Maps with the selected native target's pinned policies; model inputs retain their model semantics.</summary>
    ValueTask<WorkflowExecutableInputValidation> MapAsync(WorkflowExecutableInputContract contract,
        IReadOnlyList<TriggerInputMapping> mappings, JsonElement sourceEnvelope, WorkflowValuePolicies policies,
        CancellationToken cancellationToken = default, TriggerInputSemantics semantics = TriggerInputSemantics.Native);

    /// <summary>Preflights literal mappings with the selected native target's pinned policies.</summary>
    WorkflowExecutableInputValidation ValidateMappings(WorkflowExecutableInputContract contract,
        IReadOnlyList<TriggerInputMapping> mappings, WorkflowValuePolicies policies, TriggerInputSemantics semantics = TriggerInputSemantics.Native);
}

/// <summary>Distinguishes native executable admission from the retained declared-default semantics of model-owned targets.</summary>
public enum TriggerInputSemantics { Native, ModelOwned }
