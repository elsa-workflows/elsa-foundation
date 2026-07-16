namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>Raised when runtime inspection finds pending schema work or incompatible applied state.</summary>
public sealed class GroundworkRuntimeSchemaAdmissionException : InvalidOperationException
{
    public GroundworkRuntimeSchemaAdmissionException(GroundworkRuntimeSchemaAdmissionResult result)
        : base(CreateMessage(result)) => Result = result;

    public GroundworkRuntimeSchemaAdmissionResult Result { get; }

    private static string CreateMessage(GroundworkRuntimeSchemaAdmissionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var diagnostics = result.Diagnostics.Count == 0
            ? "No provider diagnostic was returned."
            : string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Code}: {diagnostic.Message} [{diagnostic.Target}]"));
        return $"Groundwork runtime schema admission failed for target '{result.TargetFingerprint}'. " +
               "Runtime startup did not apply or repair schema." + Environment.NewLine + diagnostics;
    }
}
