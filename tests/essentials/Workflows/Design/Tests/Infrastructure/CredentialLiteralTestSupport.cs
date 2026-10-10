using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

namespace Elsa.Workflows.Design.Tests.Infrastructure;

/// <summary>
/// One cataloged activity with a credential input, a sensitive input that is not a credential and a plain input, and the
/// bindings the credential-literal rule (spec 188, FR-008) judges, for the tests of every entry point.
/// </summary>
/// <remarks>
/// The Design API test project compiles this file too (a linked <c>Compile</c> item, with
/// <see cref="StubActivityCatalog"/>). Each project builds the validator in its own part,
/// <c>CredentialLiteralTestSupport.Validator.cs</c>, because only this project has the test activity structure.
/// </remarks>
internal static partial class CredentialLiteralTestSupport
{
    public const string ActivityVersionId = "av-credential";
    public const string UncatalogedActivityVersionId = "av-not-installed";
    public const string CredentialKey = "apiKey";
    public const string CredentialName = "ApiKey";
    public const string SensitiveKey = "note";
    public const string PlainKey = "label";
    public const string NodeId = "send";

    /// <summary>A literal no refusal may echo. Built at run time, never credential-shaped.</summary>
    public static readonly string Literal = $"literal-value-{Guid.NewGuid():N}";

    public static InputDefinition[] Inputs { get; } =
    [
        Input(CredentialKey, CredentialName) with { IsSensitive = true, IsCredential = true },
        Input(SensitiveKey, "Note") with { IsSensitive = true },
        Input(PlainKey, "Label")
    ];

    /// <summary>A catalog holding the credential activity, and the root the baseline validator helpers wrap nodes in.</summary>
    public static StubActivityCatalog Catalog() => new StubActivityCatalog().Add(ActivityVersionId, Inputs);

    /// <summary>A state whose root is a node of <paramref name="activityVersionId"/> bound as <paramref name="inputs"/> says.</summary>
    public static WorkflowDefinitionState State(string activityVersionId = ActivityVersionId, params ArgumentState[] inputs) =>
        new([], new ActivityNode(NodeId, activityVersionId, inputs, []), [], [], null);

    /// <summary>A state binding the credential input as <paramref name="binding"/> names it.</summary>
    public static WorkflowDefinitionState CredentialBoundAs(string binding) => State(ActivityVersionId, Bind(CredentialKey, binding));

    /// <summary>
    /// A binding of <paramref name="inputKey"/>: <c>Literal</c> carries <paramref name="literal"/>, or <see cref="Literal"/>
    /// when it is null; <c>Secret</c> is a secret reference; <c>SecretText</c> the literal as the payload of a
    /// <c>Secret</c> binding; <c>SecretWithExtraMember</c> a reference with the literal in a member outside the reference;
    /// <c>UnpickedSecret</c> a <c>Secret</c> binding with no payload; <c>EmptyLiteral</c> an empty string; the rest are
    /// the other authored kinds.
    /// </summary>
    public static ArgumentState Bind(string inputKey, string binding, string? literal = null) =>
        new(inputKey, binding switch
        {
            "Literal" => new ArgumentValue(JsonSerializer.SerializeToElement(literal ?? Literal), "Literal"),
            "Secret" => new ArgumentValue(JsonSerializer.SerializeToElement(new { name = "payments-reference" }), "Secret"),
            "SecretText" => new ArgumentValue(JsonSerializer.SerializeToElement(literal ?? Literal), "Secret"),
            "SecretWithExtraMember" => new ArgumentValue(JsonSerializer.SerializeToElement(new { name = "payments-reference", note = literal ?? Literal }), "Secret"),
            "UnpickedSecret" => new ArgumentValue(null, "Secret"),
            "EmptyLiteral" => new ArgumentValue(JsonSerializer.SerializeToElement(string.Empty), "Literal"),
            "Object" => new ArgumentValue(JsonSerializer.SerializeToElement(new { note = Literal }), "Object"),
            "Variable" => new ArgumentValue(JsonSerializer.SerializeToElement("variable-reference"), "Variable"),
            "JavaScript" => new ArgumentValue(JsonSerializer.SerializeToElement($"'{Literal}'"), "JavaScript"),
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding, null)
        }, null, null, null, null);

    private static InputDefinition Input(string key, string name) =>
        new(key, name, new TypeReference("String"), null, name, null, IsNullable: true);
}
