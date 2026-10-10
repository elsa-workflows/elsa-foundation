using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// <see cref="SecretReferencePayload.Read"/> returns exactly one of a reference and a defect, and nothing else can create
/// a reading or a reference's members, so no caller can hold a "well-formed" reading without a name (spec 188, FR-009).
/// The acceptance rows themselves are <see cref="CredentialInputBindingTests"/>'s.
/// </summary>
public sealed class SecretReferencePayloadTests
{
    [Fact]
    public void A_well_formed_payload_reads_as_a_reference_with_its_name_kept_as_written()
    {
        var reading = SecretReferencePayload.Read(JsonSerializer.SerializeToElement(new { name = "  reference-name  ", typeName = "text" }));

        Assert.Null(reading.Defect);
        Assert.Equal(("  reference-name  ", "text", (string?)null), (reading.Reference!.Name, reading.Reference.TypeName, reading.Reference.Scope));
    }

    [Theory]
    [MemberData(nameof(CredentialInputBindingTests.RefusedSecretPayloads), MemberType = typeof(CredentialInputBindingTests))]
    public void A_malformed_payload_reads_as_a_defect_without_a_reference(string row, object? payload)
    {
        var reading = SecretReferencePayload.Read(payload);

        Assert.Null(reading.Reference);
        Assert.False(string.IsNullOrEmpty(reading.Defect), row);
    }

    [Theory]
    [InlineData(typeof(SecretReferencePayloadReading))]
    [InlineData(typeof(SecretReferenceMembers))]
    public void Only_the_payload_reader_creates_a_reading_or_a_references_members(Type type) =>
        Assert.Empty(type.GetConstructors());
}
