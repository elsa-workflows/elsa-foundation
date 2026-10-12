using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A secret read binding, the withheld envelope that stands in for its value (spec 188, T004) and a pinned credential
/// input declaration (slice 5), round-tripped through the options the EF stores persist runtime artifacts with: enums by
/// name, strings framed as UTF-16.
/// </summary>
public sealed class RuntimeArtifactJsonSecretBindingTests
{
    private static readonly Type ArtifactJson = typeof(EfWorkflowExecutionStateStore).Assembly.GetType(
        "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.RuntimeArtifactJson",
        throwOnError: true)!;

    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly RuntimeSecretReference Reference = new("payments.api-key", "text");
    private static readonly ValueConversionPlan IdentityPlan = ValueConversionPlan.Identity(StringType, ValueRepresentation.TextValue);
    private static readonly ValueProtectionPolicy SecretPolicy = new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);

    [Fact]
    public void A_secret_read_binding_round_trips_through_the_runtime_artifact_options()
    {
        var binding = new RuntimeInputBinding(
            "apiKey",
            StringType,
            SecretPolicy,
            RuntimeInputBindingSource.SecretRead,
            conversionPlan: IdentityPlan,
            secret: Reference);

        var json = Invoke<string>("Serialize", typeof(RuntimeInputBinding), binding);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("SecretRead", node["source"]!.GetValue<string>());
        Assert.Equal(EfRelationalIdentity.Encode(Reference.Name), node["secret"]!["name"]!.GetValue<string>());
        Assert.Null(node["literal"]);

        var roundTripped = Invoke<RuntimeInputBinding>("Deserialize", typeof(RuntimeInputBinding), json);
        Assert.Equal(RuntimeInputBindingSource.SecretRead, roundTripped.Source);
        Assert.Equal(Reference, roundTripped.Secret);
        Assert.Equal(IdentityPlan.Fingerprint, roundTripped.ConversionPlan!.Fingerprint);
        Assert.Equal(json, Invoke<string>("Serialize", typeof(RuntimeInputBinding), roundTripped));
    }

    [Fact]
    public void A_withheld_envelope_round_trips_through_the_runtime_artifact_options_without_a_value()
    {
        var envelope = ValueEnvelope.Withheld(StringType, WithheldValue.SecretReference(Reference, IdentityPlan), SecretPolicy);

        var json = Invoke<string>("Serialize", typeof(ValueEnvelope), envelope);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("Withheld", node["presence"]!.GetValue<string>());
        Assert.Equal("SecretReference", node["withheld"]!["kind"]!.GetValue<string>());
        Assert.Equal(EfRelationalIdentity.Encode(Reference.Name), node["withheld"]!["secret"]!["name"]!.GetValue<string>());
        Assert.Null(node["inlineValue"]);
        Assert.Null(node["externalReference"]);

        var roundTripped = Invoke<ValueEnvelope>("Deserialize", typeof(ValueEnvelope), json);
        Assert.Equal(ValuePresence.Withheld, roundTripped.Presence);
        Assert.Equal(WithheldValueKind.SecretReference, roundTripped.WithheldValue!.Kind);
        Assert.Equal(Reference, roundTripped.WithheldValue.Secret);
        Assert.Equal(IdentityPlan.Fingerprint, roundTripped.WithheldValue.ConversionPlan!.Fingerprint);
        Assert.Equal(json, Invoke<string>("Serialize", typeof(ValueEnvelope), roundTripped));
    }

    [Fact]
    public void A_pinned_credential_flag_round_trips_and_is_left_out_where_nothing_is_declared()
    {
        // Spec 188, slice 5: the pinned contract carries the credential declaration explicitly, written only where set,
        // so a contract whose inputs declare none keeps its persisted shape.
        var contract = new ActivityContract(
            "test.activity",
            "1.0.0",
            "test",
            JsonSerializer.SerializeToElement(new { type = "test" }),
            [
                new ActivityInputContract("apiKey", "ApiKey", StringType, false, true, false, null, ActivityValuePolicy.Default with { IsSensitive = true, RequiresEncryption = true }, isCredential: true),
                new ActivityInputContract("label", "Label", StringType, false, true, false, null, ActivityValuePolicy.Default)
            ],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("test", "test.activity"));

        var json = Invoke<string>("Serialize", typeof(ActivityContract), contract);
        var inputs = JsonNode.Parse(json)!["inputs"]!.AsObject();

        Assert.True(inputs[EfRelationalIdentity.Encode("apiKey")]!["isCredential"]!.GetValue<bool>());
        Assert.False(inputs[EfRelationalIdentity.Encode("label")]!.AsObject().ContainsKey("isCredential"));

        var roundTripped = Invoke<ActivityContract>("Deserialize", typeof(ActivityContract), json);
        Assert.True(roundTripped.Inputs["apiKey"].IsCredential);
        Assert.False(roundTripped.Inputs["label"].IsCredential);
        Assert.Equal(contract.SchemaFingerprint, roundTripped.SchemaFingerprint);
        Assert.Equal(json, Invoke<string>("Serialize", typeof(ActivityContract), roundTripped));
    }

    // RuntimeArtifactJson is internal to the EF persistence assembly; this project reaches it by reflection, as
    // EfRuntimeCheckpointRunHealthParticipantTests already does.
    private static T Invoke<T>(string name, Type typeArgument, object argument) =>
        (T)ArtifactJson.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name == name && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(typeArgument)
            .Invoke(null, [argument])!;
}
