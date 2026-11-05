namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Declared public copy and an optional fixed editor location for one provider-owned diagnostic code.
/// These values must be static guidance, without user content or infrastructure details.
/// </summary>
public sealed record ActivityProviderDiagnosticDescriptor(
    string Code,
    string Message,
    string Remediation,
    string? JsonPointer = null,
    string? ActionLabel = null);
