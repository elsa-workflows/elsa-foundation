using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeValueEnvelopeContractTests
{
    private static readonly ValueTypeDescriptor StringType = new(
        alias: "System.String",
        collectionKind: CollectionKind.Single);

    [Fact]
    public void ValueTypeDescriptor_UsesAliasAndOwnsSchema()
    {
        ValueTypeDescriptor descriptor;

        using (var document = JsonDocument.Parse("""{"type":"string"}"""))
        {
            descriptor = new ValueTypeDescriptor(
                alias: "System.String",
                collectionKind: CollectionKind.Single,
                schema: document.RootElement,
                schemaVersion: 2);
        }

        Assert.Equal("System.String", descriptor.Alias);
        Assert.Equal(CollectionKind.Single, descriptor.CollectionKind);
        Assert.Equal("string", descriptor.Schema!.Value.GetProperty("type").GetString());
        Assert.Equal(2, descriptor.SchemaVersion);
        Assert.Throws<ArgumentException>(() => new ValueTypeDescriptor("", CollectionKind.Single));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ValueTypeDescriptor("System.String", CollectionKind.Single, schemaVersion: 0));
    }

    [Fact]
    public void ValueEnvelope_DistinguishesAbsentNullAndPresent()
    {
        var absent = ValueEnvelope.Absent(StringType, ValueProtectionPolicy.Transient);
        var explicitNull = ValueEnvelope.Null(StringType, ValueProtectionPolicy.InstanceInline);
        var present = ValueEnvelope.Inline(
            StringType,
            JsonSerializer.SerializeToElement("hello"),
            ValueProtectionPolicy.InstanceInline);

        Assert.Equal(ValuePresence.Absent, absent.Presence);
        Assert.Equal(ValuePresence.ExplicitNull, explicitNull.Presence);
        Assert.Equal(ValuePresence.Present, present.Presence);
        Assert.Null(absent.InlineValue);
        Assert.Null(explicitNull.InlineValue);
        Assert.Equal("hello", present.InlineValue!.Value.GetString());
    }

    [Fact]
    public void ValueEnvelope_RequiresExactlyOnePresentPayloadLocation()
    {
        var external = new DurableValueExternalReference(
            StorageProfile: "encrypted",
            Locator: "values/42",
            Metadata: new Dictionary<string, string>());

        Assert.Throws<ArgumentException>(() => new ValueEnvelope(
            StringType,
            ValuePresence.Present,
            inlineValue: null,
            externalReference: null,
            ValueProtectionPolicy.InstanceInline));
        Assert.Throws<ArgumentException>(() => new ValueEnvelope(
            StringType,
            ValuePresence.Present,
            JsonSerializer.SerializeToElement("hello"),
            external,
            ValueProtectionPolicy.InstanceInline));
        Assert.Throws<ArgumentException>(() => new ValueEnvelope(
            StringType,
            ValuePresence.ExplicitNull,
            JsonSerializer.SerializeToElement("hello"),
            externalReference: null,
            ValueProtectionPolicy.InstanceInline));
    }

    [Fact]
    public void ExternalReference_ConstructorValidatesIdentity()
    {
        Assert.Equal(
            "StorageProfile",
            Assert.Throws<ArgumentException>(() => new DurableValueExternalReference("", "values/42", new Dictionary<string, string>())).ParamName);
        Assert.Equal(
            "Locator",
            Assert.Throws<ArgumentException>(() => new DurableValueExternalReference("encrypted", " ", new Dictionary<string, string>())).ParamName);
        Assert.Equal(
            "Metadata",
            Assert.Throws<ArgumentNullException>(() => new DurableValueExternalReference("encrypted", "values/42", null!)).ParamName);
    }

    [Fact]
    public void ExternalReference_SnapshotsConstructorAndWithMetadata()
    {
        var metadata = new Dictionary<string, string> { ["key"] = "original" };
        var reference = new DurableValueExternalReference("encrypted", "values/42", metadata);
        metadata["key"] = "changed";
        metadata["new"] = "value";

        Assert.Equal("original", reference.Metadata["key"]);
        Assert.False(reference.Metadata.ContainsKey("new"));

        var replacementMetadata = new Dictionary<string, string> { ["replacement"] = "before" };
        var copy = reference with { Metadata = replacementMetadata };
        replacementMetadata["replacement"] = "after";

        Assert.Equal("before", copy.Metadata["replacement"]);
        Assert.Equal("original", reference.Metadata["key"]);
    }

    [Fact]
    public void ExternalReference_WithAndObjectInitializerPreserveValidatedInitSurface()
    {
        var reference = new DurableValueExternalReference("encrypted", "values/42", new Dictionary<string, string>());
        var initialized = new DurableValueExternalReference("temporary", "temporary/42", new Dictionary<string, string>())
        {
            StorageProfile = "archive",
            Locator = "archive/42",
            Metadata = new Dictionary<string, string> { ["tier"] = "cold" }
        };
        var copy = reference with { StorageProfile = "archive", Locator = "archive/42" };

        Assert.Equal("archive", initialized.StorageProfile);
        Assert.Equal("archive/42", initialized.Locator);
        Assert.Equal("cold", initialized.Metadata["tier"]);
        Assert.Equal("archive", copy.StorageProfile);
        Assert.Equal("archive/42", copy.Locator);
        Assert.Equal(
            "StorageProfile",
            Assert.Throws<ArgumentException>(() => reference with { StorageProfile = " " }).ParamName);
        Assert.Equal(
            "Locator",
            Assert.Throws<ArgumentException>(() => reference with { Locator = "" }).ParamName);
        Assert.Equal(
            "Metadata",
            Assert.Throws<ArgumentNullException>(() => reference with { Metadata = null! }).ParamName);
    }

    [Fact]
    public void ExternalReference_DeconstructsLikeTheOriginalPositionalRecord()
    {
        var metadata = new Dictionary<string, string> { ["key"] = "value" };
        var reference = new DurableValueExternalReference("encrypted", "values/42", metadata);

        var (storageProfile, locator, deconstructedMetadata) = reference;

        Assert.Equal("encrypted", storageProfile);
        Assert.Equal("values/42", locator);
        Assert.Equal("value", deconstructedMetadata["key"]);
    }

    [Fact]
    public void ExternalReference_RoundTripsThroughJson()
    {
        var reference = new DurableValueExternalReference(
            "encrypted",
            "values/42",
            new Dictionary<string, string> { ["key"] = "value" });

        var json = JsonSerializer.Serialize(reference);
        var copy = JsonSerializer.Deserialize<DurableValueExternalReference>(json);

        Assert.NotNull(copy);
        Assert.Equal(reference.StorageProfile, copy.StorageProfile);
        Assert.Equal(reference.Locator, copy.Locator);
        Assert.Equal(reference.Metadata, copy.Metadata);
    }

    [Fact]
    public void ValueProtectionPolicy_CannotDowngradeSensitiveSource()
    {
        var sensitive = new ValueProtectionPolicy(
            DurableValueLifecycle.Instance,
            DurableValueStorage.External,
            isSensitive: true,
            requiresEncryption: true,
            redactionMode: "full");
        var ordinary = ValueProtectionPolicy.InstanceInline;

        Assert.True(sensitive.Satisfies(sensitive));
        Assert.False(ordinary.Satisfies(sensitive));
        Assert.Throws<ArgumentException>(() => new ValueProtectionPolicy(
            DurableValueLifecycle.None,
            DurableValueStorage.Inline));
    }
}
