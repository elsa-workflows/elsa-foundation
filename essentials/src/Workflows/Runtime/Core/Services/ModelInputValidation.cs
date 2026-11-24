using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Preserves the existing declared-default admission of model-owned targets, without native optional completion.</summary>
public static class ModelInputValidation
{
    public static WorkflowExecutableInputValidation Normalize(IWorkflowExecutableInputValidator validator,
        WorkflowExecutableInputContract contract, IEnumerable<KeyValuePair<string, JsonElement>> inputs)
    {
        var known = validator.ValidateKnownInputs(contract, inputs, WorkflowValuePolicies.Off);
        if (!known.IsValid) return known;
        var missing = contract.Inputs.Where(input => input.IsRequired && !known.NormalizedInputs.ContainsKey(input.Name))
            .Select(input => new WorkflowExecutableInputValidationFinding(WorkflowExecutableInputValidationReasonCodes.MissingRequired,
                "A required model input has no supplied value or declared default.", input.Name)).ToArray();
        return missing.Length == 0 ? known : WorkflowExecutableInputValidation.Failure(missing);
    }
}
