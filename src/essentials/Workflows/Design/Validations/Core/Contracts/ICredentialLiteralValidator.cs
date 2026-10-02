using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Design.Validations.Core.Contracts;

/// <summary>
/// Declares <see cref="ICredentialLiteralValidator"/> as a single-implementation replacement contract (framework
/// constitution §2.6.2).
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class CredentialLiteralValidatorReplacementContractAttribute : Attribute;

/// <summary>
/// Finds the bindings of a workflow definition state that the credential-literal rule (spec 188, FR-008) refuses: a
/// literal, an object, a default request, a value read or an expression on an input its activity declares a credential.
/// The application-layer writers of workflow state the architecture suite's coverage guard knows of take it: one that
/// refuses a whole request admits the state through <see cref="WorkflowStateAdmission.AdmitAsync"/>, and one that refuses
/// a single item of many reads the findings here.
/// </summary>
/// <remarks>
/// This is a <b>replacement contract</b> (framework constitution §2.6.2), declared by
/// <see cref="CredentialLiteralValidatorReplacementContractAttribute"/>: at most one implementation is meaningful per
/// container. <c>WorkflowDesignValidations</c> registers the default, and a host that composes a second one does not
/// start: shell activation fails with <see cref="Exceptions.MultipleCredentialLiteralValidatorsException"/>, naming every
/// registration, whatever order they were registered in. An implementation
/// judges only the nodes whose activity version the catalog holds, and returns no finding for a node it cannot resolve
/// (publication refuses such a node, because it cannot compile it).
/// </remarks>
[CredentialLiteralValidatorReplacementContract]
public interface ICredentialLiteralValidator
{
    /// <summary>
    /// Returns one finding per refused binding, each naming the activity node and the input and never the bound value,
    /// or an empty list when <paramref name="state"/> holds none.
    /// </summary>
    ValueTask<IReadOnlyList<ValidationError>> Validate(WorkflowDefinitionState state, CancellationToken cancellationToken);
}
