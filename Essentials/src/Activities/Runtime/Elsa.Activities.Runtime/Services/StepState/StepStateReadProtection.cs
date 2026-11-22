using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services.StepVariables;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services.StepState;

internal static class StepStateReadProtection
{
    internal static ActivityValuePolicy? Read(StepVariableSession? variables, StepWorkflowState? state)
    {
        ActivityValuePolicy? result = null;
        foreach (var policy in (variables?.ReadPolicies ?? []).Concat(state?.ReadPolicies ?? []))
        {
            if (!policy.IsSensitive && !policy.RequiresEncryption && string.IsNullOrWhiteSpace(policy.RedactionMode)) continue;
            var minimum = new ActivityValuePolicy(true, policy.IsSensitive, policy.RequiresEncryption, policy.RedactionMode);
            result = result is null ? minimum : ValuePolicyCombiner.Combine(result, minimum, "Step body read protection");
        }
        return result;
    }
}
