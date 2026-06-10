using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Validators;

public sealed class RuntimeInputBindingValidator : IRuntimeInputBindingValidator
{
    public IReadOnlyCollection<RuntimeInputBindingDiagnostic> Validate(
        RuntimeInputBinding binding,
        RuntimeInputBindingValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(context);

        if (binding.Source != RuntimeInputBindingSource.ActivityOutput)
            return [];

        var diagnostics = new List<RuntimeInputBindingDiagnostic>();
        var reference = binding.ActivityOutput!;

        if (context.CrossesSuspensionBoundary)
            diagnostics.Add(new RuntimeInputBindingDiagnostic(
                RuntimeInputBindingDiagnosticCode.ActivityOutputCrossesSuspensionBoundary,
                binding.InputName,
                context.ExecutableNodeId,
                $"Input '{binding.InputName}' references activity output '{reference.OutputName}' across a suspension boundary; capture it into a durable value first."));

        if (context.HasAmbiguousProducerExecution && string.IsNullOrWhiteSpace(reference.ProducerActivityExecutionId))
            diagnostics.Add(new RuntimeInputBindingDiagnostic(
                RuntimeInputBindingDiagnosticCode.AmbiguousActivityOutputReference,
                binding.InputName,
                context.ExecutableNodeId,
                $"Input '{binding.InputName}' references activity output '{reference.OutputName}' without a concrete producer ActivityExecutionId in an ambiguous execution scope."));

        return diagnostics;
    }
}
