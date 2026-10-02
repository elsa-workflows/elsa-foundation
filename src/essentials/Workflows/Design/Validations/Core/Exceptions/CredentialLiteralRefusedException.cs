using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Exceptions;

/// <summary>
/// A workflow definition state binds a literal, an object, a value read or an expression to an input its activity
/// declares a credential (spec 188, FR-008). Thrown by <see cref="WorkflowStateAdmission.AdmitAsync"/> before its caller
/// writes, so nothing is stored, and by publication before an input is compiled. It carries one finding per refused
/// binding; neither the findings nor the message carry the bound value.
/// </summary>
/// <remarks>
/// It is an <see cref="ArgumentException"/>, because the refused content is the caller's: publication reports it as one
/// of its compile errors (400), and the design API maps it to 400 with its findings keyed by path.
/// </remarks>
public sealed class CredentialLiteralRefusedException : ArgumentException
{
    /// <summary>
    /// Creates the refusal for <paramref name="findings"/>, at least one. The message joins the findings' messages, none of
    /// which carries the bound value.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="findings"/> is empty.</exception>
    public CredentialLiteralRefusedException(IReadOnlyList<ValidationError> findings)
        : base(Describe(findings))
    {
        Findings = findings;
    }

    /// <summary>One finding per refused binding, each keyed by the input's path.</summary>
    public IReadOnlyList<ValidationError> Findings { get; }

    private static string Describe(IReadOnlyList<ValidationError> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0)
            throw new ArgumentException("A credential-literal refusal needs at least one finding.", nameof(findings));

        return string.Join(" ", findings.Select(finding => finding.Message));
    }
}
