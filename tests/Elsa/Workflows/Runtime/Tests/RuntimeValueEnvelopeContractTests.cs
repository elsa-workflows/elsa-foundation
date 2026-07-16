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
