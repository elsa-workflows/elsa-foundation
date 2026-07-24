using Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests.Fixtures;
using Groundwork.Documents.Serialization;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class OpenIddictGroundworkCodecTests
{
    public static IEnumerable<object[]> RecordKinds() =>
        OpenIddictGroundworkContractFixture.CodecCases.Select(codecCase => new object[] { codecCase });

    [Theory]
    [MemberData(nameof(RecordKinds))]
    public void Canonical_json_and_descriptor_round_trip_are_deterministic_for_every_record_kind(
        OpenIddictGroundworkContractFixture.CodecCase codecCase)
    {
        var codec = OpenIddictGroundworkContractFixture.RequiredCodec();
        var policy = OpenIddictGroundworkContractFixture.PolicyFor(codecCase.Role);
        var descriptor = codecCase.CreateDescriptor();

        var first = OpenIddictGroundworkContractFixture.Serialize(codec, policy.DocumentKind, descriptor);
        var second = OpenIddictGroundworkContractFixture.Serialize(codec, policy.DocumentKind, descriptor);
        var roundTripped = OpenIddictGroundworkContractFixture.Deserialize(
            codec,
            OpenIddictGroundworkContractFixture.Envelope(
                policy.DocumentKind,
                first.SchemaVersion,
                first.ContentJson),
            descriptor.GetType());

        Assert.Equal(first, second);
        codecCase.AssertEquivalent(descriptor, roundTripped);
    }

    [Fact]
    public void All_four_record_kinds_declare_valid_current_and_minimum_readable_versions()
    {
        var policies = OpenIddictGroundworkContractFixture.RequiredPolicies();

        Assert.Equal(4, policies.Count);
        Assert.Equal(4, policies.Select(policy => policy.DocumentKind).Distinct(StringComparer.Ordinal).Count());
        Assert.All(policies, policy =>
        {
            Assert.True(policy.MinimumReadableVersion >= 1);
            Assert.True(policy.CurrentVersion >= policy.MinimumReadableVersion);
        });
        Assert.All(
            new[] { "application", "authorization", "scope", "token" },
            role => Assert.Single(policies.Where(policy =>
                policy.DocumentKind.Contains(role, StringComparison.OrdinalIgnoreCase))));
    }

    [Fact]
    public void Every_readable_non_current_version_has_one_contiguous_upcaster_step()
    {
        var policies = OpenIddictGroundworkContractFixture.RequiredPolicies();
        var upcasters = OpenIddictGroundworkContractFixture.RequiredUpcasters();

        foreach (var policy in policies)
        {
            var expectedSteps = Enumerable.Range(
                policy.MinimumReadableVersion,
                policy.CurrentVersion - policy.MinimumReadableVersion);
            var actualSteps = upcasters
                .Where(upcaster => upcaster.DocumentKind == policy.DocumentKind)
                .Select(upcaster => upcaster.FromVersion)
                .Order()
                .ToArray();

            Assert.Equal(expectedSteps, actualSteps);
        }
    }

    [Theory]
    [MemberData(nameof(RecordKinds))]
    public void Future_payloads_are_rejected_before_descriptor_deserialization(
        OpenIddictGroundworkContractFixture.CodecCase codecCase)
    {
        var codec = OpenIddictGroundworkContractFixture.RequiredCodec();
        var policy = OpenIddictGroundworkContractFixture.PolicyFor(codecCase.Role);
        var descriptor = codecCase.CreateDescriptor();
        var current = OpenIddictGroundworkContractFixture.Serialize(codec, policy.DocumentKind, descriptor);
        var futureStamp = OpenIddictGroundworkContractFixture.ShiftVersionStamp(
            current.SchemaVersion,
            1);

        var exception = Assert.Throws<DocumentSchemaVersionException>(() =>
            OpenIddictGroundworkContractFixture.Deserialize(
                codec,
                OpenIddictGroundworkContractFixture.Envelope(
                    policy.DocumentKind,
                    futureStamp,
                    current.ContentJson),
                descriptor.GetType()));

        Assert.Equal(DocumentSchemaVersionFailure.Future, exception.Failure);
        Assert.Equal(policy.DocumentKind, exception.DocumentKind);
    }

    [Theory]
    [MemberData(nameof(RecordKinds))]
    public void Corrupt_current_payloads_are_rejected_with_record_context(
        OpenIddictGroundworkContractFixture.CodecCase codecCase)
    {
        var codec = OpenIddictGroundworkContractFixture.RequiredCodec();
        var policy = OpenIddictGroundworkContractFixture.PolicyFor(codecCase.Role);
        var descriptor = codecCase.CreateDescriptor();
        var current = OpenIddictGroundworkContractFixture.Serialize(codec, policy.DocumentKind, descriptor);

        var exception = Assert.Throws<DocumentSchemaVersionException>(() =>
            OpenIddictGroundworkContractFixture.Deserialize(
                codec,
                OpenIddictGroundworkContractFixture.Envelope(
                    policy.DocumentKind,
                    current.SchemaVersion,
                    "{not-json"),
                descriptor.GetType()));

        Assert.Equal(DocumentSchemaVersionFailure.InvalidContent, exception.Failure);
        Assert.Equal(policy.DocumentKind, exception.DocumentKind);
        Assert.Equal("contract-record-1", exception.DocumentId);
        Assert.NotNull(exception.InnerException);
    }
}
