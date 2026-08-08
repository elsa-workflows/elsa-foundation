using Elsa.Expressions.Core.Contracts;

namespace Elsa.Expressions.Core.Models;

/// <summary>
/// The expression types the authoring surface always accepts, independent of which expression-language
/// features are composed: <c>Literal</c> (a constant value), <c>Object</c> (a structured literal) and
/// <c>Input</c> (a reference to a workflow input). They are deliberately NOT registered in
/// <see cref="IExpressionDescriptorRegistry"/> — the registry is also consumed by draft semantic
/// validation, and registering them there would change validation behaviour rather than just publish a fact.
/// </summary>
/// <remarks>
/// One-projection rule (spec 149 / RFC #1191): the served descriptor endpoint
/// (<c>ListExpressionDescriptorsRequestHandler</c>) and the build-time contract fragments both take the
/// intrinsic set from here. They were previously hardcoded inside the API handler, which made them
/// invisible to any build-time projection — so the published contracts omitted <c>Literal</c>, the
/// expression type used on nearly every input of nearly every definition, while a consumer reading only
/// the contracts had no way to discover it (consumer merge-gate evaluation of `35363cebd`).
/// </remarks>
public static class IntrinsicExpressionDescriptors
{
    public static IReadOnlyList<IExpressionDescriptor> All { get; } =
    [
        Create("Literal", ExpressionEditingMode.Literal),
        Create("Object", ExpressionEditingMode.Structured),
        Create("Input", ExpressionEditingMode.Reference)
    ];

    private static ExpressionDescriptor Create(string typeName, ExpressionEditingMode editingMode) =>
        new(typeName, editingMode) { DisplayName = typeName };
}
