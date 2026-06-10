namespace Groundwork.Core.Validation;

public sealed record GroundworkDiagnostic(
    GroundworkDiagnosticSeverity Severity,
    string Code,
    string Message,
    string? Target = null)
{
    public bool IsError => Severity == GroundworkDiagnosticSeverity.Error;

    public static GroundworkDiagnostic Error(string code, string message, string? target = null) =>
        new(GroundworkDiagnosticSeverity.Error, code, message, target);

    public static GroundworkDiagnostic Warning(string code, string message, string? target = null) =>
        new(GroundworkDiagnosticSeverity.Warning, code, message, target);
}

public enum GroundworkDiagnosticSeverity
{
    Error,
    Warning
}
