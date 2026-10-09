using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// <see cref="ValueEnvelope.HoldsValueRequiringEncryption"/>, the one predicate of spec 188, FR-010 that producer
/// withholding, the output capture guard, the checkpoint-commit backstop and the execution evidence enricher share: a
/// present value whose own policy, or the destination's policy when one is given, requires encryption.
/// </summary>
public sealed class ValueEnvelopeEncryptionPredicateTests
{
    private static readonly ValueTypeDescriptor StringType = new("String");
    private static readonly JsonElement Text = JsonSerializer.SerializeToElement("text");
    private static readonly ValueProtectionPolicy Plain = ValueProtectionPolicy.InstanceInline;
    private static readonly ValueProtectionPolicy SensitiveOnly = new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
    private static readonly ValueProtectionPolicy EncryptionRequired =
        new(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);
    private static readonly ValueProtectionPolicy ExternalEncryptionRequired =
        new(DurableValueLifecycle.Instance, DurableValueStorage.External, isSensitive: true, requiresEncryption: true);

    private static readonly IReadOnlyDictionary<string, (ValueEnvelope Value, ValueProtectionPolicy? Destination)> Rows =
        new Dictionary<string, (ValueEnvelope, ValueProtectionPolicy?)>(StringComparer.Ordinal)
        {
            ["present inline, own policy requires encryption"] = (ValueEnvelope.Inline(StringType, Text, EncryptionRequired), null),
            ["present external, own policy requires encryption"] = (ValueEnvelope.External(
                StringType, new DurableValueExternalReference("test.store", "locator-1", new Dictionary<string, string>()), ExternalEncryptionRequired), null),
            ["present inline, only the destination requires encryption"] = (ValueEnvelope.Inline(StringType, Text, Plain), EncryptionRequired),
            ["present inline, nothing requires encryption"] = (ValueEnvelope.Inline(StringType, Text, Plain), Plain),
            ["present inline, sensitive without encryption"] = (ValueEnvelope.Inline(StringType, Text, SensitiveOnly), null),
            ["explicit null, own policy requires encryption"] = (ValueEnvelope.Null(StringType, EncryptionRequired), EncryptionRequired),
            ["absent, own policy requires encryption"] = (ValueEnvelope.Absent(StringType, EncryptionRequired), EncryptionRequired),
            ["withheld marker, own policy requires encryption"] = (
                ValueEnvelope.Withheld(StringType, WithheldValue.PolicyRequiresEncryption(), EncryptionRequired), EncryptionRequired)
        };

    [Theory]
    [InlineData("present inline, own policy requires encryption", true)]
    [InlineData("present external, own policy requires encryption", true)]
    [InlineData("present inline, only the destination requires encryption", true)]
    [InlineData("present inline, nothing requires encryption", false)]
    [InlineData("present inline, sensitive without encryption", false)]
    [InlineData("explicit null, own policy requires encryption", false)]
    [InlineData("absent, own policy requires encryption", false)]
    [InlineData("withheld marker, own policy requires encryption", false)]
    public void Only_a_present_value_under_a_policy_that_requires_encryption_matches(string row, bool expected)
    {
        var (value, destination) = Rows[row];

        Assert.Equal(expected, value.HoldsValueRequiringEncryption(destination));
    }

    [Fact]
    public void Every_row_is_exercised() =>
        Assert.Equal(
            Rows.Keys.Order(StringComparer.Ordinal),
            typeof(ValueEnvelopeEncryptionPredicateTests)
                .GetMethod(nameof(Only_a_present_value_under_a_policy_that_requires_encryption_matches))!
                .GetCustomAttributes(typeof(InlineDataAttribute), inherit: false)
                .Cast<InlineDataAttribute>()
                .Select(data => (string)data.GetData(null!).Single()[0])
                .Order(StringComparer.Ordinal));

    [Fact]
    public void The_encryption_marker_carries_its_kind_and_nothing_of_the_value()
    {
        var marker = WithheldValue.PolicyRequiresEncryption();

        Assert.Equal(WithheldValueKind.PolicyRequiresEncryption, marker.Kind);
        Assert.Null(marker.Secret);
        Assert.Null(marker.ConversionPlan);
    }
}
