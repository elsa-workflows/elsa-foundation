using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resolvers;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Tests.Fixtures;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The runtime value model of a secret binding: a <see cref="RuntimeInputBindingSource.SecretRead"/> binding and the
/// withheld envelope that stands in for its value in persisted state (spec 188, T004).
/// </summary>
public sealed class RuntimeSecretBindingModelTests
{
    // Web casing with enums written by name. Not the EF stores' options, which also frame strings as UTF-16: the
    // persisted shape is proved against those in RuntimeArtifactJsonSecretBindingTests (EF persistence tests).
    private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly RuntimeSecretReference Reference = new("payments.api-key", "text");
    private static readonly ValueConversionPlan IdentityPlan = ValueConversionPlan.Identity(StringType, ValueRepresentation.TextValue);
    private static readonly ValueProtectionPolicy SecretPolicy = new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);

    private static RuntimeInputBinding WithheldLiteral(string inputKey) =>
        new(inputKey, StringType, SecretPolicy, RuntimeInputBindingSource.Literal, literal: WithheldValues.Secret(StringType));

    [Fact]
    public void A_secret_read_binding_carries_exactly_its_secret_payload()
    {
        var binding = SecretBinding(Reference);

        Assert.Equal(RuntimeInputBindingSource.SecretRead, binding.Source);
        Assert.Same(Reference, binding.Secret);
        Assert.Null(binding.Literal);
        Assert.Null(binding.LiteralValue);
    }

    [Fact]
    public void A_secret_read_binding_without_its_secret_payload_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeInputBinding(
            "apiKey", StringType, SecretPolicy, RuntimeInputBindingSource.SecretRead));
        Assert.Throws<ArgumentException>(() => new RuntimeInputBinding(
            "apiKey", StringType, SecretPolicy, RuntimeInputBindingSource.SecretRead,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("x"), SecretPolicy)));
    }

    [Fact]
    public void A_secret_payload_next_to_another_payload_or_on_another_source_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeInputBinding(
            "apiKey", StringType, SecretPolicy, RuntimeInputBindingSource.SecretRead,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("x"), SecretPolicy),
            secret: Reference));
        Assert.Throws<ArgumentException>(() => new RuntimeInputBinding(
            "apiKey", StringType, SecretPolicy, RuntimeInputBindingSource.Literal,
            secret: Reference));
        Assert.Throws<ArgumentException>(() => new RuntimeSecretReference(" "));
    }

    [Fact]
    public void A_secret_read_binding_below_the_secret_policy_minimum_still_validates()
    {
        // The {IsSensitive, RequiresEncryption} minimum is a publish rule. An imported artifact can carry a lower
        // policy, and the model must still load it so the runtime protections, not the model, decide what happens.
        var binding = SecretBinding(Reference, ValueProtectionPolicy.InstanceInline);

        Assert.False(binding.EffectivePolicy.IsSensitive);
        Assert.False(binding.EffectivePolicy.RequiresEncryption);
    }

    [Fact]
    public void A_withheld_envelope_carries_no_inline_or_external_payload()
    {
        var withheld = WithheldValue.SecretReference(Reference, IdentityPlan);

        Assert.Throws<ArgumentException>(() => new ValueEnvelope(
            StringType, ValuePresence.Withheld, JsonSerializer.SerializeToElement("value"), null, SecretPolicy, withheld));
        Assert.Throws<ArgumentException>(() => new ValueEnvelope(
            StringType, ValuePresence.Withheld, null, new DurableValueExternalReference("profile", "locator", new Dictionary<string, string>()), SecretPolicy, withheld));
        Assert.Throws<ArgumentException>(() => new ValueEnvelope(
            StringType, ValuePresence.Withheld, null, null, SecretPolicy));
    }

    [Fact]
    public void Only_a_withheld_envelope_carries_a_withheld_marker()
    {
        var withheld = WithheldValue.SecretReference(Reference, IdentityPlan);

        Assert.Throws<ArgumentException>(() => new ValueEnvelope(
            StringType, ValuePresence.Present, JsonSerializer.SerializeToElement("value"), null, SecretPolicy, withheld));
        Assert.Throws<ArgumentException>(() => new ValueEnvelope(
            StringType, ValuePresence.Absent, null, null, SecretPolicy, withheld));
    }

    [Fact]
    public void A_secret_reference_marker_requires_its_reference_and_the_encryption_marker_forbids_one()
    {
        Assert.Throws<ArgumentException>(() => new WithheldValue(WithheldValueKind.SecretReference));
        Assert.Throws<ArgumentException>(() => new WithheldValue(WithheldValueKind.PolicyRequiresEncryption, Reference));

        var encryptionRequired = ValueEnvelope.Withheld(StringType, new WithheldValue(WithheldValueKind.PolicyRequiresEncryption), SecretPolicy);

        Assert.Equal(ValuePresence.Withheld, encryptionRequired.Presence);
        Assert.Null(encryptionRequired.WithheldValue!.Secret);
    }

    [Fact]
    public void A_secret_read_binding_round_trips_through_the_runtime_artifact_json_shape()
    {
        var json = JsonSerializer.Serialize(SecretBinding(Reference), ArtifactJsonOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("SecretRead", node["source"]!.GetValue<string>());
        Assert.Equal("payments.api-key", node["secret"]!["name"]!.GetValue<string>());
        Assert.Equal("text", node["secret"]!["typeName"]!.GetValue<string>());
        Assert.False(node["secret"]!.AsObject().ContainsKey("scope"));

        var roundTripped = JsonSerializer.Deserialize<RuntimeInputBinding>(json, ArtifactJsonOptions)!;
        Assert.Equal(RuntimeInputBindingSource.SecretRead, roundTripped.Source);
        Assert.Equal(Reference, roundTripped.Secret);
        Assert.Equal(IdentityPlan.Fingerprint, roundTripped.ConversionPlan!.Fingerprint);
    }

    [Fact]
    public void A_withheld_envelope_round_trips_through_the_runtime_artifact_json_shape_without_a_value()
    {
        var envelope = ValueEnvelope.Withheld(StringType, WithheldValue.SecretReference(Reference, IdentityPlan), SecretPolicy);

        var json = JsonSerializer.Serialize(envelope, ArtifactJsonOptions);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("Withheld", node["presence"]!.GetValue<string>());
        Assert.Equal("SecretReference", node["withheld"]!["kind"]!.GetValue<string>());
        Assert.Equal("payments.api-key", node["withheld"]!["secret"]!["name"]!.GetValue<string>());
        Assert.NotNull(node["withheld"]!["conversionPlan"]);
        Assert.Null(node["inlineValue"]);
        Assert.Null(node["externalReference"]);

        var roundTripped = JsonSerializer.Deserialize<ValueEnvelope>(json, ArtifactJsonOptions)!;
        Assert.Equal(ValuePresence.Withheld, roundTripped.Presence);
        Assert.Equal(WithheldValueKind.SecretReference, roundTripped.WithheldValue!.Kind);
        Assert.Equal(Reference, roundTripped.WithheldValue.Secret);
        Assert.Equal(IdentityPlan.Fingerprint, roundTripped.WithheldValue.ConversionPlan!.Fingerprint);
    }

    [Fact]
    public void Bindings_and_envelopes_without_a_secret_serialize_without_the_new_members()
    {
        // Content-addressed artifacts and templates hash these serialized shapes, so an executable without secret
        // bindings must serialize exactly as it did before secret reads existed.
        var literal = new RuntimeInputBinding(
            "text", StringType, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(StringType, JsonSerializer.SerializeToElement("hello"), ValueProtectionPolicy.InstanceInline));

        foreach (var options in new[] { ArtifactJsonOptions, JsonSerializerOptions.Default })
        {
            var binding = JsonNode.Parse(JsonSerializer.Serialize(literal, options))!.AsObject();
            Assert.DoesNotContain(binding, property => StringComparer.OrdinalIgnoreCase.Equals(property.Key, "secret"));
            Assert.DoesNotContain(binding.Single(property => StringComparer.OrdinalIgnoreCase.Equals(property.Key, "literal")).Value!.AsObject(),
                property => StringComparer.OrdinalIgnoreCase.Equals(property.Key, "withheld"));
        }
    }

    [Fact]
    public void The_executable_hash_formats_a_secret_read_by_its_reference()
    {
        var hasher = new WorkflowExecutableHasher();
        var contract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []);

        string NodeHash(RuntimeSecretReference reference) => hasher.ComputeHash(Node(SecretBinding(reference)));
        string BehavioralHash(RuntimeSecretReference reference) => hasher.ComputeHash(Node(SecretBinding(reference)), contract, []);

        Assert.Equal(NodeHash(Reference), NodeHash(new("payments.api-key", "text")));
        Assert.NotEqual(NodeHash(Reference), NodeHash(new("payments.other-key", "text")));
        Assert.NotEqual(NodeHash(Reference), NodeHash(new("payments.api-key", "rsa-key")));
        Assert.Equal(BehavioralHash(Reference), BehavioralHash(new("payments.api-key", "text")));
        Assert.NotEqual(BehavioralHash(Reference), BehavioralHash(new("payments.other-key", "text")));
        Assert.NotEqual(BehavioralHash(Reference), BehavioralHash(new("payments.api-key", "text", "billing")));
    }

    [Fact]
    public void The_executable_hash_formats_a_withheld_literal_by_its_reference()
    {
        // Only an imported artifact can carry a literal whose envelope is withheld; two that differ only in the
        // secret they withhold must not share a hash.
        var hasher = new WorkflowExecutableHasher();
        var contract = new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []);

        ExecutableNode WithheldNode(RuntimeSecretReference reference) => Node(new(
            "apiKey", StringType, SecretPolicy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Withheld(StringType, WithheldValue.SecretReference(reference, IdentityPlan), SecretPolicy)));
        string NodeHash(RuntimeSecretReference reference) => hasher.ComputeHash(WithheldNode(reference));
        string BehavioralHash(RuntimeSecretReference reference) => hasher.ComputeHash(WithheldNode(reference), contract, []);

        Assert.Equal(NodeHash(Reference), NodeHash(new("payments.api-key", "text")));
        Assert.NotEqual(NodeHash(Reference), NodeHash(new("payments.other-key", "text")));
        Assert.Equal(BehavioralHash(Reference), BehavioralHash(new("payments.api-key", "text")));
        Assert.NotEqual(BehavioralHash(Reference), BehavioralHash(new("payments.other-key", "text")));
    }

    [Fact]
    public void The_binding_resolver_passes_a_secret_read_through_as_a_withheld_reference()
    {
        var resolved = new RuntimeInputBindingResolver().Resolve(
            SecretBinding(Reference),
            new RuntimeInputBindingResolutionContext("wfexec-1", "actexec-1"));

        var envelope = resolved.Envelope!;
        Assert.Equal(RuntimeInputBindingSource.SecretRead, resolved.Source);
        Assert.Equal(ValuePresence.Withheld, envelope.Presence);
        Assert.Null(envelope.InlineValue);
        Assert.Equal(Reference, envelope.WithheldValue!.Secret);
        Assert.Equal(SecretPolicy, envelope.Policy);
    }

    [Fact]
    public void A_variable_initial_value_that_stands_for_a_withheld_value_is_refused_with_the_fixed_code()
    {
        // Publication refuses a secret as a variable's initial value, so only an artifact that skipped it carries one. The
        // marker belongs to the literal's binding, so it is refused rather than retyped into the variable frame.
        var declaration = new RuntimeVariableDeclaration("token", "Token", StringType, SecretPolicy, WithheldLiteral("token"));

        var exception = Assert.Throws<WithheldValueException>(() => new RuntimeVariableDeclarationProjector().ProjectInitialValues([declaration]));

        Assert.Equal(SecretBindingDiagnostics.WithheldVariableNotResolved("Token").Message, exception.Message);
    }

    [Fact]
    public void The_literal_reader_backstop_refuses_a_literal_that_stands_for_a_withheld_value()
    {
        // A withheld literal has no value to read, and every publish-time literal reader would otherwise take it for an
        // unauthored input and apply its default.
        var exception = Assert.Throws<WithheldValueException>(() =>
            SecretBindingDiagnostics.ThrowIfSecretRead(WithheldLiteral("path"), "node-1", "path"));

        Assert.Equal(SecretBindingDiagnostics.WithheldInputNotResolved("path").Message, exception.Message);
    }

    [Fact]
    public async Task The_snapshot_binding_fingerprint_distinguishes_secret_references()
    {
        async Task<string> FingerprintAsync(RuntimeSecretReference reference)
        {
            var binding = SecretBinding(reference);
            var node = new ExecutableNode(
                executableNodeId: "node-1",
                authoredActivityId: "node-1",
                activityType: "Test.Activity",
                activityTypeVersion: "1.0.0",
                descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
                inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputName] = binding },
                metadata: new Dictionary<string, string>(),
                activityContract: new ActivityContract(
                    "Test.Activity",
                    "1.0.0",
                    "test",
                    JsonSerializer.SerializeToElement(new { }),
                    [new ActivityInputContract(binding.InputName, binding.InputName, StringType, true, false, false, null, ActivityValuePolicy.Default)],
                    new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
                    ["Done"],
                    new ActivityActivationRequirement("test", "Test.Activity")));
            var snapshot = await new RuntimeActivityInputMaterializer().MaterializeSnapshotAsync(
                node,
                "actexec-1",
                new RuntimeInputBindingResolutionContext("wfexec-1", "actexec-1"),
                DateTimeOffset.UnixEpoch);
            return snapshot.BindingFingerprint;
        }

        Assert.Equal(await FingerprintAsync(Reference), await FingerprintAsync(new("payments.api-key", "text")));
        Assert.NotEqual(await FingerprintAsync(Reference), await FingerprintAsync(new("payments.other-key", "text")));
    }

    private static RuntimeInputBinding SecretBinding(RuntimeSecretReference reference, ValueProtectionPolicy? policy = null) =>
        new(
            "apiKey",
            StringType,
            policy ?? SecretPolicy,
            RuntimeInputBindingSource.SecretRead,
            conversionPlan: IdentityPlan,
            secret: reference);

    private static ExecutableNode Node(RuntimeInputBinding binding) =>
        new(
            executableNodeId: "node-1",
            authoredActivityId: "node-1",
            activityType: "Test.Activity",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputName] = binding },
            metadata: new Dictionary<string, string>());
}
