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
    public void DesignInputNullability_IsARequiredPartOfThePublicSignature()
    {
        var constructor = Assert.Single(typeof(InputDefinition).GetConstructors());
        var parameters = constructor.GetParameters();

        // 19 = 17 pre-G1 parameters + HasStaticDefault (G1) + EnumValues (enum members are contract).
        Assert.Equal(19, parameters.Length);
        var nullability = Assert.Single(parameters, parameter => parameter.Name == nameof(InputDefinition.IsNullable));
        Assert.Equal(typeof(bool), nullability.ParameterType);
        Assert.False(nullability.HasDefaultValue);
        Assert.Contains(typeof(InputDefinition).GetMethods(), method =>
            method.Name == "Deconstruct" && method.GetParameters().Length == 19);
    }

    [Fact]
    public void RuntimeInputNullability_IsARequiredPartOfThePublicSignature()
    {
        var constructor = Assert.Single(typeof(ActivityInputContract).GetConstructors());
        var nullability = Assert.Single(constructor.GetParameters(), parameter =>
            StringComparer.OrdinalIgnoreCase.Equals(parameter.Name, nameof(ActivityInputContract.IsNullable)));

        Assert.Equal(typeof(bool), nullability.ParameterType);
        Assert.False(nullability.HasDefaultValue);
        Assert.Null(typeof(ActivityInputContract).GetProperty(nameof(ActivityInputContract.IsNullable))!.SetMethod);
    }

    [Fact]
    public void ExplicitNullabilityValues_AreBehaviorallyHashed()
    {
        var nullable = NullabilityContract(true);
        var nonNullable = NullabilityContract(false);

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

    private static ActivityContract NullabilityContract(bool isNullable) =>
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
                    isNullable,
                    hasDefault: false,
                    defaultValue: null,
                    ActivityValuePolicy.Default)
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
            isNullable: false,
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
