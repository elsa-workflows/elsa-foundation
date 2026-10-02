using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// The credential-literal rule (spec 188, FR-008, FR-009): an activity input the activity declares a credential accepts
/// only a secret reference or no binding. This is the one acceptance predicate the design-time validator and publication
/// both apply, so a binding is judged the same way at every entry point a definition passes through.
/// </summary>
/// <remarks>
/// The caller says whether the input is a credential, read from the explicit declaration (the catalog's
/// <c>InputDefinition.IsCredential</c>, or a pinned contract's <c>IsCredential</c>) and never inferred from a policy
/// that requires encryption.
/// </remarks>
public static class CredentialInputBinding
{
    /// <summary>The stable rule identifier every refusal carries, in the validation-category form.</summary>
    public const string RuleId = "Inputs/CredentialLiteral";

    /// <summary>
    /// The authored expression type of a secret reference. It duplicates the Secrets module's expression type name on
    /// purpose, so this project needs no reference to that module.
    /// </summary>
    public const string SecretExpressionType = "Secret";

    private const string DefaultExpressionType = "Default";

    /// <summary>
    /// True when <paramref name="state"/> may bind an input whose credential declaration is
    /// <paramref name="isCredential"/>. An input that is not a credential accepts any binding. A credential input
    /// accepts no binding, a binding that carries no value (<see cref="ArgumentState.IsBound"/>, so an empty or null
    /// literal leaves it unbound) and a secret reference. Everything else is refused: a literal, an object, a request
    /// for the declared default, a value read and any expression.
    /// </summary>
    public static bool IsAccepted(bool isCredential, ArgumentState? state) =>
        !isCredential
        || state is null
        || IsSecretReference(state.Value)
        || !IsDefaultRequest(state.Value) && !state.IsBound();

    // Expression types compare ordinally, ignoring case, as publication compares them.
    private static bool IsSecretReference(ArgumentValue? value) =>
        string.Equals(value?.ExpressionType, SecretExpressionType, StringComparison.OrdinalIgnoreCase);

    // A request for the declared default carries no value of its own, but it binds the default, which is a literal.
    private static bool IsDefaultRequest(ArgumentValue? value) =>
        string.Equals(value?.ExpressionType, DefaultExpressionType, StringComparison.OrdinalIgnoreCase);
}
