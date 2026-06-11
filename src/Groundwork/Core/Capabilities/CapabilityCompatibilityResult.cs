using Groundwork.Core.Validation;

namespace Groundwork.Core.Capabilities;

public sealed record CapabilityCompatibilityResult(IReadOnlyList<GroundworkDiagnostic> Diagnostics)
{
    public bool IsCompatible => Diagnostics.All(diagnostic => !diagnostic.IsError);

    public IReadOnlyList<GroundworkDiagnostic> Errors => Diagnostics.Where(diagnostic => diagnostic.IsError).ToList();

    public static CapabilityCompatibilityResult Compatible { get; } = new([]);
}
