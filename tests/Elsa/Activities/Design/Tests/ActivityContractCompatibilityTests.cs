using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityContractCompatibilityTests
{
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
        ValueTypeDescriptor? inputType = null) =>
        new(
            activityTypeKey: "Payments.ChargeCard",
            contractVersion: "2.0.0",
            descriptorKind: "clr",
            descriptorPayload: JsonSerializer.SerializeToElement(new { typeAlias = "Payments.ChargeCard" }),
            inputs: [Input(inputName, inputKey, inputType ?? new ValueTypeDescriptor("System.String"))],
            result: Result(),
            outcomes: ["completed", "declined"],
            activation: new ActivityActivationRequirement("clr", "Payments.ChargeCard"));

    private static ActivityInputContract Input(string name, string key, ValueTypeDescriptor type) =>
        new(
            key,
            name,
            type,
            isRequired: true,
            hasDefault: false,
            defaultValue: null,
            ActivityValuePolicy.Default);

    private static ActivityResultContract Result() =>
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
                    ActivityValuePolicy.Default)
            ]);
}
