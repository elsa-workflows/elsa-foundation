using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using RuntimeActivityInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using static Elsa.Workflows.Publishing.Api.Tests.SecretBindingCompilerFixture;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// <c>VF-ACT-011</c> (spec 188, FR-010, research R8): an input whose effective policy requires encryption accepts only a
/// secret reference or no binding, because no other value reaches the activity without being persisted first. It is
/// decided through both <c>CompileAll</c> overloads: the catalog overload, where only a credential declaration makes an
/// input require encryption, and the pinned-contract overload, where a contract's policy can require encryption on an
/// input that is not a credential. A binding that carries no value, such as an empty or null literal, leaves the input
/// unbound and compiles exactly as an unbound input does (spec 188 edge case). A non-credential sensitive input does not
/// require encryption and takes a literal or an expression (T104). Slice 6 places the credential rule ahead of this one
/// for credential inputs (T060).
/// </summary>
public sealed class EncryptionRequiredBindingTests
{
    private const string InputKey = "apiKey";

    // A value the refusal must never echo, built at run time.
    private static readonly string Sentinel = $"bound-value-{Guid.NewGuid():N}";

    private static readonly ValueTypeDescriptor StringType = new("String");

    private readonly RuntimeInputBindingCompiler _compiler = new(TestWellKnownTypeRegistry.Create());

    /// <summary>How the input reaches an <c>CompileAll</c> overload.</summary>
    public enum InputShape
    {
        /// <summary>A catalog input declared a credential.</summary>
        CatalogCredential,

        /// <summary>A pinned contract input flagged a credential, with the policy publication pins for one.</summary>
        PinnedCredential,

        /// <summary>A pinned contract input that is not a credential but whose policy requires encryption.</summary>
        PinnedEncryptionRequired
    }

    public static TheoryData<InputShape, string> RefusedBindings
    {
        get
        {
            var data = new TheoryData<InputShape, string>();
            foreach (var shape in Enum.GetValues<InputShape>())
            foreach (var binding in new[] { "Literal", "Object", "Variable", "WorkflowRequest", "JavaScript", "Default" })
                data.Add(shape, binding);
            return data;
        }
    }

    public static TheoryData<InputShape, string, bool, bool> EmptyBindings
    {
        get
        {
            var data = new TheoryData<InputShape, string, bool, bool>();
            foreach (var shape in Enum.GetValues<InputShape>())
            foreach (var binding in new[] { "EmptyLiteral", "NullLiteral", "JsonNullLiteral", "NullValue" })
            foreach (var (isRequired, hasDefault) in new[] { (false, false), (true, false), (false, true) })
                data.Add(shape, binding, isRequired, hasDefault);
            return data;
        }
    }

    public static TheoryData<InputShape> Shapes => new(Enum.GetValues<InputShape>());

    [Theory]
    [MemberData(nameof(RefusedBindings))]
    public void Anything_but_a_secret_reference_is_refused_without_echoing_the_binding(InputShape shape, string binding)
    {
        var exception = Assert.Throws<ArgumentException>(() => CompileAll(shape, hasDefault: false, Authored(binding)));

        Assert.Equal(SecretBindingDiagnostics.EncryptionRequiredBindingRefused(NodeId, InputKey).Message, exception.Message);
        Assert.DoesNotContain(Sentinel, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EmptyBindings))]
    public void A_binding_that_carries_no_value_compiles_as_an_unbound_input(InputShape shape, string binding, bool isRequired, bool hasDefault)
    {
        if (!isRequired && !hasDefault)
        {
            var compiled = CompileAll(shape, hasDefault, isRequired, Authored(binding));
            var unbound = CompileAll(shape, hasDefault, isRequired);

            Assert.Equal(RuntimeInputBindingSource.Literal, compiled.Source);
            Assert.Equal(ValuePresence.Absent, compiled.Literal!.Presence);
            Assert.Null(compiled.Literal.InlineValue);
            Assert.Equal(PolicyOf(unbound), PolicyOf(compiled));
            return;
        }

        // A required input is reported missing and a declared default stays refused, exactly as when nothing is authored.
        var expected = isRequired
            ? $"VF-ACT-003: Activity node '{NodeId}' omits required input '{InputKey}'"
            : SecretBindingDiagnostics.EncryptionRequiredDefaultRefused(NodeId, InputKey).Message;
        var authoredException = Assert.Throws<ArgumentException>(() => CompileAll(shape, hasDefault, isRequired, Authored(binding)));
        var unboundException = Assert.Throws<ArgumentException>(() => CompileAll(shape, hasDefault, isRequired));

        Assert.StartsWith(expected, authoredException.Message, StringComparison.Ordinal);
        Assert.Equal(unboundException.Message, authoredException.Message);
    }

    [Fact]
    public async Task Publication_accepts_an_empty_literal_on_a_declared_credential_input()
    {
        var executable = await CompileAsync(
            Node(typeof(DeclaredInputsActivity), State(nameof(DeclaredInputsActivity.ApiKey), "EmptyLiteral")),
            [typeof(DeclaredInputsActivity)]);

        var binding = executable.RootActivity.InputBindings[nameof(DeclaredInputsActivity.ApiKey)];
        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal(ValuePresence.Absent, binding.Literal!.Presence);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void An_unbound_input_whose_contract_declares_a_default_is_refused_because_the_default_is_a_literal(InputShape shape)
    {
        var exception = Assert.Throws<ArgumentException>(() => CompileAll(shape, hasDefault: true));

        Assert.Equal(SecretBindingDiagnostics.EncryptionRequiredDefaultRefused(NodeId, InputKey).Message, exception.Message);
        // The input was left unbound, so the message names the default as the fault and its removal as the fix.
        Assert.Contains("declared default is a literal value", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Remove the default", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("no binding", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void A_secret_reference_is_accepted(InputShape shape)
    {
        var binding = CompileAll(shape, hasDefault: false, Secret(InputKey));

        Assert.Equal(RuntimeInputBindingSource.SecretRead, binding.Source);
        Assert.True(binding.EffectivePolicy.IsSensitive);
        Assert.True(binding.EffectivePolicy.RequiresEncryption);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void An_unbound_input_without_a_default_is_accepted(InputShape shape)
    {
        var binding = CompileAll(shape, hasDefault: false);

        Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
        Assert.Equal(ValuePresence.Absent, binding.Literal!.Presence);
        Assert.True(binding.EffectivePolicy.RequiresEncryption);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_input_whose_policy_does_not_require_encryption_accepts_a_literal(bool declaredSensitive)
    {
        var definition = Input() with { IsSensitive = declaredSensitive ? true : null };
        var contract = Contract(new ActivityValuePolicy(true, declaredSensitive, RequiresEncryption: false), isCredential: false, hasDefault: false);

        foreach (var binding in CompileThroughBothOverloads(definition, contract, Authored("Literal")))
        {
            Assert.Equal(RuntimeInputBindingSource.Literal, binding.Source);
            Assert.Equal(Sentinel, binding.Literal!.InlineValue!.Value.GetString());
            Assert.Equal(declaredSensitive, binding.EffectivePolicy.IsSensitive);
            Assert.False(binding.EffectivePolicy.RequiresEncryption);
        }
    }

    [Fact]
    public void An_expression_on_a_sensitive_input_that_is_not_a_credential_compiles_sensitive_and_unencrypted()
    {
        // T104 (spec edge case, A21): the declaration makes the input sensitive, and neither the credential rule nor
        // VF-ACT-011 applies, so the expression compiles and its value is later materialized as present.
        var definition = Input() with { IsSensitive = true };
        var contract = Contract(new ActivityValuePolicy(true, IsSensitive: true, RequiresEncryption: false), isCredential: false, hasDefault: false);

        foreach (var binding in CompileThroughBothOverloads(definition, contract, Authored("JavaScript")))
        {
            Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
            Assert.True(binding.EffectivePolicy.IsSensitive);
            Assert.False(binding.EffectivePolicy.RequiresEncryption);
        }
    }

    [Fact]
    public async Task Publication_refuses_a_literal_on_a_declared_credential_input()
    {
        var exception = await AssertRefusedAsync(
            Node(typeof(DeclaredInputsActivity), State(nameof(DeclaredInputsActivity.ApiKey), "Literal")),
            [typeof(DeclaredInputsActivity)]);

        Assert.Equal(SecretBindingDiagnostics.EncryptionRequiredBindingRefused(NodeId, nameof(DeclaredInputsActivity.ApiKey)).Message, exception.Message);
        Assert.DoesNotContain(Sentinel, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_binding_refusal_wins_over_an_encryption_required_refusal_on_the_same_node()
    {
        // VF-ACT-012 is decided for the whole node before any input is compiled, so it keeps precedence.
        var exception = await AssertRefusedAsync(
            Node(
                typeof(DeclaredInputsActivity),
                State(nameof(DeclaredInputsActivity.ApiKey), "Literal"),
                Secret(nameof(DeclaredInputsActivity.Persisted))),
            [typeof(DeclaredInputsActivity)]);

        Assert.Equal(
            SecretBindingDiagnostics.SecretBindingRefused(NodeId, nameof(DeclaredInputsActivity.Persisted), SecretBindingRefusalReason.PersistedByActivity).Message,
            exception.Message);
    }

    private RuntimeInputBinding CompileAll(InputShape shape, bool hasDefault, params WorkflowArgumentState[] states) =>
        CompileAll(shape, hasDefault, isRequired: false, states);

    private RuntimeInputBinding CompileAll(InputShape shape, bool hasDefault, bool isRequired, params WorkflowArgumentState[] states)
    {
        var bindings = shape switch
        {
            InputShape.CatalogCredential => _compiler.CompileAll(
                NodeId,
                [Input() with { IsSensitive = true, IsCredential = true, IsRequired = isRequired, DefaultValue = hasDefault ? JsonSerializer.SerializeToElement(Sentinel) : null }],
                states),
            InputShape.PinnedCredential => _compiler.CompileAll(
                NodeId,
                [Contract(new ActivityValuePolicy(true, IsSensitive: true, RequiresEncryption: true), isCredential: true, hasDefault, isRequired)],
                states),
            _ => _compiler.CompileAll(
                NodeId,
                [Contract(new ActivityValuePolicy(true, IsSensitive: false, RequiresEncryption: true), isCredential: false, hasDefault, isRequired)],
                states)
        };
        return Assert.Single(bindings).Value;
    }

    private IEnumerable<RuntimeInputBinding> CompileThroughBothOverloads(
        InputDefinition definition,
        RuntimeActivityInputContract contract,
        WorkflowArgumentState state) =>
    [
        Assert.Single(_compiler.CompileAll(NodeId, [definition], [state])).Value,
        Assert.Single(_compiler.CompileAll(NodeId, [contract], [state])).Value
    ];

    private static (DurableValueLifecycle, DurableValueStorage, bool, bool) PolicyOf(RuntimeInputBinding binding) =>
        (binding.EffectivePolicy.Lifecycle, binding.EffectivePolicy.Storage, binding.EffectivePolicy.IsSensitive, binding.EffectivePolicy.RequiresEncryption);

    private static InputDefinition Input() =>
        new(InputKey, "ApiKey", new TypeReference("String"), null, "ApiKey", null, IsNullable: true);

    private static RuntimeActivityInputContract Contract(ActivityValuePolicy policy, bool isCredential, bool hasDefault, bool isRequired = false) =>
        new(
            InputKey,
            "ApiKey",
            StringType,
            isRequired,
            isNullable: true,
            hasDefault,
            hasDefault ? JsonSerializer.SerializeToElement(Sentinel) : null,
            policy,
            isCredential: isCredential);

    private static WorkflowArgumentState Authored(string binding) => State(InputKey, binding);

    private static WorkflowArgumentState State(string inputKey, string binding) =>
        new(inputKey, binding switch
        {
            "NullValue" => null!,
            "NullLiteral" => new ArgumentValue(null, "Literal"),
            "JsonNullLiteral" => new ArgumentValue(JsonSerializer.SerializeToElement<object?>(null), "Literal"),
            "Literal" => new ArgumentValue(JsonSerializer.SerializeToElement(Sentinel), "Literal"),
            "EmptyLiteral" => new ArgumentValue(JsonSerializer.SerializeToElement(string.Empty), "Literal"),
            "Object" => new ArgumentValue(JsonSerializer.SerializeToElement(new { note = Sentinel }), "Object"),
            "Variable" => new ArgumentValue(JsonSerializer.SerializeToElement("var-token"), "Variable"),
            "WorkflowRequest" => new ArgumentValue(JsonSerializer.SerializeToElement(new { memberKey = "token" }), "WorkflowRequest"),
            "JavaScript" => new ArgumentValue(JsonSerializer.SerializeToElement($"'{Sentinel}'"), "JavaScript"),
            "Default" => new ArgumentValue(null, "Default"),
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding, null)
        }, null, null, null, null);
}
