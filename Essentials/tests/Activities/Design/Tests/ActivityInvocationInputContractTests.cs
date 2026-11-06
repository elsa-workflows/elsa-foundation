using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityInvocationInputContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Both_existing_constructor_signatures_keep_the_absent_capability_wire_and_identity()
    {
        var first = LegacyContract();
        var second = new ActivityContract(first.ActivityTypeKey, first.ContractVersion, first.DescriptorKind,
            first.DescriptorPayload, first.Inputs.Values, first.Result, first.Outcomes, first.Activation,
            first.SideEffectProfile, first.SupportsBackgroundExecution);
        var explicitNull = WithCapability(first, null);

        Assert.Null(first.InvocationInput);
        Assert.Equal(first.SchemaFingerprint, second.SchemaFingerprint);
        Assert.Equal(first.SchemaFingerprint, explicitNull.SchemaFingerprint);
        Assert.Equal(JsonSerializer.Serialize(first, Options), JsonSerializer.Serialize(second, Options));
        Assert.Equal(JsonSerializer.Serialize(first, Options), JsonSerializer.Serialize(explicitNull, Options));
        Assert.False(JsonNode.Parse(JsonSerializer.Serialize(first, Options))!.AsObject().ContainsKey("invocationInput"));
        var restored = JsonSerializer.Deserialize<ActivityContract>(JsonSerializer.Serialize(first, Options), Options)!;
        Assert.Equal(first.SchemaFingerprint, restored.SchemaFingerprint);
        Assert.Null(restored.InvocationInput);
    }

    [Fact]
    public void Capability_protocol_and_version_are_part_of_the_pin_and_round_trip()
    {
        var legacy = LegacyContract();
        var first = WithCapability(legacy, new("sample.receiver", 1));
        var otherProtocol = WithCapability(legacy, new("sample.other", 1));
        var otherVersion = WithCapability(legacy, new("sample.receiver", 2));
        Assert.NotEqual(legacy.SchemaFingerprint, first.SchemaFingerprint);
        Assert.NotEqual(first.SchemaFingerprint, otherProtocol.SchemaFingerprint);
        Assert.NotEqual(first.SchemaFingerprint, otherVersion.SchemaFingerprint);
        var restored = JsonSerializer.Deserialize<ActivityContract>(JsonSerializer.Serialize(first, Options), Options)!;
        Assert.Equal(first.SchemaFingerprint, restored.SchemaFingerprint);
        Assert.Equal("sample.receiver", restored.InvocationInput!.Protocol);
        Assert.Equal(1, restored.InvocationInput.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removing_or_changing_a_capability_cannot_reuse_the_published_fingerprint(bool remove)
    {
        var pinned = WithCapability(LegacyContract(), new("sample.receiver", 1));
        var wire = JsonNode.Parse(JsonSerializer.Serialize(pinned, Options))!.AsObject();
        if (remove) wire.Remove("invocationInput");
        else wire["invocationInput"]!["version"] = 2;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ActivityContract>(wire.ToJsonString(), Options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_capability_requires_a_positive_protocol_version(int version) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityInvocationInputCapability("sample.receiver", version));

    [Fact]
    public void Capability_names_preserve_ordinal_protocol_identity()
    {
        var first = WithCapability(LegacyContract(), new("Receiver", 1));
        var second = WithCapability(LegacyContract(), new("receiver", 1));
        Assert.NotEqual(first.SchemaFingerprint, second.SchemaFingerprint);
        Assert.Equal("Receiver", first.InvocationInput!.Protocol);
    }

    [Theory]
    [InlineData("receiver-\uD800")]
    [InlineData("receiver-\uD801")]
    [InlineData("receiver-\uDC00")]
    public void Invalid_utf16_names_refuse_instead_of_colliding_in_json(string protocol) =>
        Assert.Throws<ArgumentException>(() => new ActivityInvocationInputCapability(protocol, 1));

    [Fact]
    public void Valid_supplementary_unicode_is_preserved_on_the_pin()
    {
        var protocol = "receiver-\uD83D\uDE00";
        var pinned = WithCapability(LegacyContract(), new(protocol, 1));
        var restored = JsonSerializer.Deserialize<ActivityContract>(JsonSerializer.Serialize(pinned, Options), Options)!;
        Assert.Equal(protocol, restored.InvocationInput!.Protocol);
        Assert.Equal(pinned.SchemaFingerprint, restored.SchemaFingerprint);
    }

    private static ActivityContract WithCapability(ActivityContract first, ActivityInvocationInputCapability? capability) =>
        new(first.ActivityTypeKey, first.ContractVersion, first.DescriptorKind, first.DescriptorPayload,
            first.Inputs.Values, first.Result, first.Outcomes, first.Activation, first.SideEffectProfile,
            first.SupportsBackgroundExecution, capability);

    private static ActivityContract LegacyContract() => new("sample.activity", "1", "sample",
        JsonSerializer.SerializeToElement(new { }), [],
        new ActivityResultContract(new ValueTypeDescriptor("Unit"), false, ActivityValuePolicy.Default, []),
        ["Done"], new ActivityActivationRequirement("sample", "sample.activity"));
}
