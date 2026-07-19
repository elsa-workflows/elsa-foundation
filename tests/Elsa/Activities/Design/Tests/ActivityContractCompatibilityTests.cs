using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Xunit;
using ActivityActivationRequirement = Elsa.Activities.Runtime.Core.Models.ActivityActivationRequirement;
using ActivityContract = Elsa.Activities.Runtime.Core.Models.ActivityContract;
using ActivityInputContract = Elsa.Activities.Runtime.Core.Models.ActivityInputContract;
using ActivityResultContract = Elsa.Activities.Runtime.Core.Models.ActivityResultContract;
using ActivityResultProjectionContract = Elsa.Activities.Runtime.Core.Models.ActivityResultProjectionContract;
using ActivityValuePolicy = Elsa.Activities.Runtime.Core.Models.ActivityValuePolicy;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityContractCompatibilityTests
{
    [Fact]
    public void AdditiveNullabilityMetadata_PreservesEstablishedPublicSignatures()
    {
        Assert.Contains(typeof(InputDefinition).GetConstructors(), constructor =>
            constructor.GetParameters().Length == 16);
        Assert.Contains(typeof(InputDefinition).GetMethods(), method =>
            method.Name == "Deconstruct" && method.GetParameters().Length == 16);
        Assert.Contains(typeof(ActivityInputContract).GetConstructors(), constructor =>
            constructor.GetParameters().Length == 8);
    }

    [Fact]
    public void PreNullabilityContractJson_PreservesItsCapturedSchemaFingerprint()
    {
        const string json =
            """
            {
              "activityTypeKey": "test/activity",
              "contractVersion": "1.0.0",
              "schemaFingerprint": "sha256:d5e16c02a4ea7eb05c9869468b94d3edbb5c37718a03c55204e18bd1b3f567c8",
              "descriptorKind": "test",
              "descriptorPayload": {},
              "inputs": {
                "message": {
                  "key": "message",
                  "name": "Message",
                  "type": {
                    "alias": "String",
                    "collectionKind": 0,
                    "schema": null,
                    "schemaVersion": null
                  },
                  "isRequired": false,
                  "hasDefault": false,
                  "defaultValue": null,
                  "policy": {
                    "isPersistable": true,
                    "isSensitive": false,
                    "requiresEncryption": false,
                    "redactionMode": null,
                    "lifecycle": 0,
                    "storage": 0,
                    "storageProfile": null,
                    "retentionPolicy": null
                  },
                  "editorMetadata": {}
                }
              },
              "result": {
                "type": {
                  "alias": "Unit",
                  "collectionKind": 0,
                  "schema": null,
                  "schemaVersion": null
                },
                "isRequired": false,
                "policy": {
                  "isPersistable": true,
                  "isSensitive": false,
                  "requiresEncryption": false,
                  "redactionMode": null,
                  "lifecycle": 0,
                  "storage": 0,
                  "storageProfile": null,
                  "retentionPolicy": null
                },
                "projections": {}
              },
              "outcomes": ["Done"],
              "activation": {
                "descriptorKind": "test",
                "capability": "test/activity"
              }
            }
            """;

        var contract = JsonSerializer.Deserialize<ActivityContract>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(contract);
        Assert.Null(contract.Inputs["message"].IsNullable);
        Assert.Equal("sha256:d5e16c02a4ea7eb05c9869468b94d3edbb5c37718a03c55204e18bd1b3f567c8", contract.SchemaFingerprint);
    }

    [Fact]
    public void ExplicitNullabilityValues_AreBehaviorallyHashed()
    {
        var legacyUnknown = NullabilityContract(null);
        var nullable = NullabilityContract(true);
        var nonNullable = NullabilityContract(false);

        Assert.NotEqual(legacyUnknown.SchemaFingerprint, nullable.SchemaFingerprint);
        Assert.NotEqual(legacyUnknown.SchemaFingerprint, nonNullable.SchemaFingerprint);
        Assert.NotEqual(nullable.SchemaFingerprint, nonNullable.SchemaFingerprint);
    }

    [Fact]
    public void StableKeyClrRename_PreservesSchemaFingerprint()
    {
        var before = Contract(inputName: "CustomerId", inputKey: "customer-id");
        var after = Contract(inputName: "AccountId", inputKey: "customer-id");

        Assert.Equal(before.SchemaFingerprint, after.SchemaFingerprint);
    }

    [Fact]
    public void StableKeyOrSchemaChange_ChangesSchemaFingerprint()
    {
        var baseline = Contract(inputName: "CustomerId", inputKey: "customer-id");
        var changedKey = Contract(inputName: "CustomerId", inputKey: "account-id");
        var changedType = Contract(
            inputName: "CustomerId",
            inputKey: "customer-id",
            inputType: new ValueTypeDescriptor("System.Int32"));

        Assert.NotEqual(baseline.SchemaFingerprint, changedKey.SchemaFingerprint);
        Assert.NotEqual(baseline.SchemaFingerprint, changedType.SchemaFingerprint);
    }

    [Fact]
    public void ExplicitResultRepresentation_ChangesSchemaFingerprintAndRoundTrips()
    {
        var text = Contract(
            inputName: "CustomerId",
            inputKey: "customer-id",
            result: Result(ValueRepresentation.TextValue));
        var formatted = Contract(
            inputName: "CustomerId",
            inputKey: "customer-id",
            result: Result(ValueRepresentation.FormattedContent));

        var serialized = JsonSerializer.Serialize(formatted, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var roundTripped = JsonSerializer.Deserialize<ActivityContract>(serialized, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotEqual(text.SchemaFingerprint, formatted.SchemaFingerprint);
        Assert.NotNull(roundTripped);
        Assert.Equal(ValueRepresentation.FormattedContent, roundTripped.Result.Projections["receipt-id"].SourceRepresentation);
        Assert.Equal(formatted.SchemaFingerprint, roundTripped.SchemaFingerprint);
    }

    [Fact]
    public void Contract_RejectsDuplicateStableKeysAndOutcomes()
    {
        var input = Input("CustomerId", "customer-id", new ValueTypeDescriptor("System.String"));

        Assert.Throws<ArgumentException>(() => new ActivityContract(
            "Payments.ChargeCard",
            "2.0.0",
            descriptorKind: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { typeAlias = "Payments.ChargeCard" }),
            inputs: [input, input],
            result: Result(),
            outcomes: ["completed"],
            activation: new ActivityActivationRequirement("clr", "Payments.ChargeCard")));
        Assert.Throws<ArgumentException>(() => new ActivityContract(
            "Payments.ChargeCard",
            "2.0.0",
            descriptorKind: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { typeAlias = "Payments.ChargeCard" }),
            inputs: [input],
            result: Result(),
            outcomes: ["completed", "completed"],
            activation: new ActivityActivationRequirement("clr", "Payments.ChargeCard")));
    }

    [Fact]
    public void ResultProjections_AreReadOnlyContractViews()
    {
        var properties = typeof(ActivityResultProjectionContract).GetProperties();

        Assert.All(properties, property => Assert.Null(property.SetMethod));
    }

    private static ActivityContract Contract(
        string inputName,
        string inputKey,
        ValueTypeDescriptor? inputType = null,
        ActivityResultContract? result = null) =>
        new(
            activityTypeKey: "Payments.ChargeCard",
            contractVersion: "2.0.0",
            descriptorKind: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { typeAlias = "Payments.ChargeCard" }),
            inputs: [Input(inputName, inputKey, inputType ?? new ValueTypeDescriptor("System.String"))],
            result: result ?? Result(),
            outcomes: ["completed", "declined"],
            activation: new ActivityActivationRequirement("clr", "Payments.ChargeCard"));

    private static ActivityContract NullabilityContract(bool? isNullable) =>
        new(
            activityTypeKey: "test/activity",
            contractVersion: "1.0.0",
            descriptorKind: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputs:
            [
                new ActivityInputContract(
                    "message",
                    "Message",
                    new ValueTypeDescriptor("String"),
                    isRequired: false,
                    hasDefault: false,
                    defaultValue: null,
                    ActivityValuePolicy.Default)
                {
                    IsNullable = isNullable
                }
            ],
            result: new ActivityResultContract(
                new ValueTypeDescriptor("Unit"),
                isRequired: false,
                ActivityValuePolicy.Default,
                []),
            outcomes: ["Done"],
            activation: new ActivityActivationRequirement("test", "test/activity"));

    private static ActivityInputContract Input(string name, string key, ValueTypeDescriptor type) =>
        new(
            key,
            name,
            type,
            isRequired: true,
            hasDefault: false,
            defaultValue: null,
            ActivityValuePolicy.Default);

    private static ActivityResultContract Result(ValueRepresentation? projectionRepresentation = null) =>
        new(
            new ValueTypeDescriptor("Payments.ChargeCardResult"),
            isRequired: true,
            ActivityValuePolicy.Default,
            [
                new ActivityResultProjectionContract(
                    "receipt-id",
                    "receiptId",
                    new ValueTypeDescriptor("System.String"),
                    isRequired: true,
                    ActivityValuePolicy.Default,
                    projectionRepresentation)
            ],
            ValueRepresentation.StructuredValue);
}
