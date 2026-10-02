using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Exceptions;

namespace Elsa.Workflows.Design.Validations.Core;

/// <summary>
/// The admission of workflow definition state through the credential-literal rule (spec 188, FR-008) before it is
/// written: a caller that refuses a whole request calls <see cref="AdmitAsync"/> before its write, so a state it refuses
/// is not stored. The rule lives in <see cref="ICredentialLiteralValidator"/>; this helper only throws its
/// findings, so it holds no rule of its own and a host cannot replace it.
/// </summary>
/// <remarks>
/// A caller that handles many items and must refuse one without failing the rest (file reconciliation, git export) reads
/// the findings from <see cref="ICredentialLiteralValidator.Validate"/> itself and skips that item. The architecture
/// suite's coverage guard (<c>ArchitectureGuardTests.CredentialLiteralAdmission.cs</c>) checks that every writer of
/// workflow state it knows of takes <see cref="ICredentialLiteralValidator"/>. A static helper over the contract, like
/// <see cref="DraftValidationGate"/>, because a <c>.Core</c> project holds no class with injected dependencies.
/// </remarks>
public static class WorkflowStateAdmission
{
    /// <summary>Throws <see cref="CredentialLiteralRefusedException"/> when <paramref name="state"/> holds a refused binding.</summary>
    public static async Task AdmitAsync(this ICredentialLiteralValidator validator, WorkflowDefinitionState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        var refusals = await validator.Validate(state, cancellationToken);
        if (refusals.Count > 0)
            throw new CredentialLiteralRefusedException(refusals);
    }
}
