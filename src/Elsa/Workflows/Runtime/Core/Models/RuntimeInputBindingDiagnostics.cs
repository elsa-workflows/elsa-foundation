namespace Elsa.Workflows.Runtime.Core.Models;

public sealed record RuntimeInputBindingValidationContext(
    string ExecutableNodeId,
    bool CrossesSuspensionBoundary,
    bool HasAmbiguousProducerExecution);

public sealed record RuntimeInputBindingDiagnostic(
    RuntimeInputBindingDiagnosticCode Code,
    string InputName,
    string ExecutableNodeId,
    string Message);

public enum RuntimeInputBindingDiagnosticCode
{
    AmbiguousActivityOutputReference,
    ActivityOutputCrossesSuspensionBoundary
}
