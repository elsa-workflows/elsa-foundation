using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Projects the closed value spaces a consumer has to match against exactly (spec 150 FR-C1/C5/C6).
/// </summary>
/// <remarks>
/// Every one of these was a required field typed as a bare string, enumerated nowhere. A benchmarked
/// session probed four endpoints looking for the variable type alias vocabulary, got 404 from all of them,
/// and guessed. The values are read from the product types that define them — the alias convention, the
/// collection-kind enum, the intrinsic-kind enum, the authoring-site constants — so the published
/// vocabulary cannot drift from the one the server enforces, and the freshness gate fails the PR if it does.
/// </remarks>
public static class VocabularyProjector
{
    public static ContractVocabularies Project() => new(
        SchemaVersion: ContractFragment.CurrentSchemaVersion,
        TypeAliases: new TypeAliasVocabulary(
            // The framework-owned bare aliases. Anything else MUST be dotted — a bare alias outside this set
            // is rejected at registration, so a consumer cannot invent one.
            ReservedBare: TypeAliasConvention.ReservedBareAliases.Order(StringComparer.Ordinal).ToArray(),
            NullableSuffix: "?",
            Governs:
                "These are the aliases that appear as the `type` of a published activity input or output. They "
                + "are NOT the set a variable may be declared as — that set is smaller and is published per "
                + "fragment under `expressions.variableTypes` (the runtime serves the same union at "
                + "GET /expressions/variable-types). Declaring a variable as a reserved alias outside that "
                + "smaller set fails.",
            OtherTypesRule:
                "Any type outside the reserved set is aliased by its namespace-qualified CLR full name, with no "
                + "assembly name and no version (for example 'Elsa.Activities.Http.Models.HttpRequestModel'). "
                + "Aliases resolve case-insensitively.",
            NullableRule:
                "A nullable value type is the underlying alias with a '?' suffix ('Int32?'). The nullable "
                + "companion is derived automatically, so it is never registered separately.",
            Note:
                "'Any' is accepted as a result-type alias on an expression definition and means 'do not constrain "
                + "the result'. It is not a variable type."),
        CollectionKinds: Enum.GetValues<CollectionKind>().Select(WireSpelling.Of).Order(StringComparer.Ordinal).ToArray(),
        IntrinsicKinds: Enum.GetNames<AuthoredWorkflowIntrinsicKind>()
            .Select(name => char.ToLowerInvariant(name[0]) + name[1..])
            .Order(StringComparer.Ordinal)
            .ToArray(),
        AuthoredVia:
        [
            new VocabularyTerm(InputAuthoringSites.Argument,
                "The input is authored as an entry in the node's `inputs` collection. This is the default and "
                + "covers every activity input."),
            new VocabularyTerm(InputAuthoringSites.IntrinsicBlock,
                "The input is carried by the node's sibling `intrinsic` block rather than by an `inputs` entry — "
                + "an intrinsic's variable target is descriptor-only and has no argument binding. Authoring it as "
                + "a plain input is rejected.")
        ],
        Publication: new PublicationVocabulary(
            // DUPLICATED (accepted, pinned): the default lives on PublicationAuthority in
            // Elsa.Workflows.Publishing.Core, which this tool does not reference — a whole project reference
            // to read one string is the wrong trade (§2.17). VocabularyTests asserts the published value
            // equals that type's default, so the two cannot drift silently.
            DefaultSlotName: "default",
            SlotNote:
                "Publishing without naming a slot targets this one. It is also the {slotName} to pass to "
                + "DELETE /publishing/workflows/{definitionId}/slots/{slotName}, which is the documented way "
                + "to release an exclusive HTTP route — a consumer that cannot name the default slot cannot "
                + "perform the remedy the contracts prescribe.",
            OperationKeyNote:
                "`operationKey` on a submit or publish body is an IDEMPOTENCY token, not a way to address an "
                + "existing definition. Repeating a request with the same key replays the original outcome "
                + "instead of performing it again; omitting it generates a fresh key, making every call a "
                + "distinct operation. It cannot be used to add a version to an existing definition — nothing "
                + "in the submit body can, which is why re-submitting a name creates a second definition."),
        ExpressionTypeSemantics:
        [
            new VocabularyTerm("Literal",
                "A constant value carried verbatim in the binding. The expression type used on nearly every "
                + "authored input."),
            new VocabularyTerm("Object",
                "A structured JSON literal, deserialized straight into the input's CLR type without reaching any "
                + "expression evaluator. This is the expression type a collection-typed input takes: the payload "
                + "is a JSON array for Array/List/HashSet and a JSON object for Dictionary."),
            new VocabularyTerm("Input",
                "A reference to a workflow input rather than an inline value."),
            new VocabularyTerm("Variable",
                "A reference to a declared variable, keyed by the variable's referenceKey and NOT by its name. "
                + "Two payload forms are accepted: the bare string `\"orderId\"`, which defaults the scope to "
                + "workflow scope, or the object `{\"referenceKey\":\"orderId\",\"declaringScopeId\":\"workflow\"}` "
                + "when the scope must be explicit. Required as the expression type of any authored output "
                + "target.")
        ]);
}

/// <summary>docs/contracts/vocabularies.json — the closed value spaces, generated from the types that own them.</summary>
public sealed record ContractVocabularies(
    string SchemaVersion,
    TypeAliasVocabulary TypeAliases,
    IReadOnlyList<string> CollectionKinds,
    IReadOnlyList<string> IntrinsicKinds,
    IReadOnlyList<VocabularyTerm> AuthoredVia,
    PublicationVocabulary Publication,
    IReadOnlyList<VocabularyTerm> ExpressionTypeSemantics);

public sealed record TypeAliasVocabulary(
    IReadOnlyList<string> ReservedBare,
    string NullableSuffix,
    string Governs,
    string OtherTypesRule,
    string NullableRule,
    string Note);

public sealed record VocabularyTerm(string Value, string Meaning);

/// <summary>Publication facts a consumer needs before it can perform the remedies the contracts prescribe.</summary>
public sealed record PublicationVocabulary(string DefaultSlotName, string SlotNote, string OperationKeyNote);
