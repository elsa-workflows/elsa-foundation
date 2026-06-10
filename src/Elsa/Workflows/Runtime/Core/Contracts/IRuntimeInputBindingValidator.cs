using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeInputBindingValidator
{
    IReadOnlyCollection<RuntimeInputBindingDiagnostic> Validate(RuntimeInputBinding binding, RuntimeInputBindingValidationContext context);
}
