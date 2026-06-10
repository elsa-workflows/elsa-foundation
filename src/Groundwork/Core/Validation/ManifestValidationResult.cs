namespace Groundwork.Core.Validation;

public sealed record ManifestValidationResult(IReadOnlyList<GroundworkDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.All(diagnostic => !diagnostic.IsError);

    public IReadOnlyList<GroundworkDiagnostic> Errors => Diagnostics.Where(diagnostic => diagnostic.IsError).ToList();

    public static ManifestValidationResult Success { get; } = new([]);
}
