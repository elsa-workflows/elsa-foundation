using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Validates realized workflow inputs against one immutable executable input contract.
/// </summary>
public interface IWorkflowExecutableInputValidator
{
    /// <summary>
    /// Returns a detached, canonical input bag on success or safe findings on failure.
    /// The ordered pair sequence deliberately preserves duplicate names for validation.
    /// </summary>
    WorkflowExecutableInputValidation Validate(
        WorkflowExecutableInputContract contract,
        IEnumerable<KeyValuePair<string, JsonElement>> inputs);

    /// <summary>
    /// Validates the contract and every statically known input while allowing required values to remain
    /// unresolved until execution. Literal defaults and type aliases are still validated.
    /// </summary>
    WorkflowExecutableInputValidation ValidateKnownInputs(
        WorkflowExecutableInputContract contract,
        IEnumerable<KeyValuePair<string, JsonElement>> inputs);
}
