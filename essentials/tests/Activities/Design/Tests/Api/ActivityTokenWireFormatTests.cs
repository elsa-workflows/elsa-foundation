using Elsa.Activities.Design.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

/// <summary>
/// The approved one-time pre-stable JSON hash migration replaces the former raw-byte signatures. These
/// independently computed canonical fixtures freeze at Elsa 4's first stable release; state and signing-key
/// changes remain observable, while retained payload bytes preserve their typed wire representation.
/// </summary>
public sealed class ActivityTokenWireFormatTests
{
    private const string SigningKey = "wire-format-fixture-signing-key-32-bytes+";

    private static readonly string OtherKey = new('o', 32);

    private const string DependencyCursorFixture =
        "eyJ0ZW5hbnRTY29wZSI6InRlbmFudDp0ZW5hbnQtYSIsImF1dGhvcml6YXRpb25Qcm9maWxlRmluZ2VycHJpbnQiOiJwcm9maWxlLWZpbmdlcnByaW50Iiwicm9vdFZlcnNpb25JZCI6InZlcnNpb24tMSIsImRpcmVjdGlvbiI6Ik91dGJvdW5kIiwidHJhbnNpdGl2ZSI6dHJ1ZSwiaW5jbHVkZSI6WyJEcmFmdHMiLCJWZXJzaW9ucyJdLCJ3YXRlcm1hcmsiOiJzaGEyNTYtd2F0ZXJtYXJrIiwicG9zaXRpb24iOjd9.xl5sWfefK9oSXli-PQBwID9MYev3L1G6VZtmyNRGvnc";

    private const string ManagementCursorFixture =
        "eyJzY29wZSI6InRlbmFudDp0ZW5hbnQtYSIsIm9mZnNldCI6MjUsInNuYXBzaG90U2VxdWVuY2UiOjQyfQ.LGh4rsAEf8UrOmXXKpid3uYyT-DUetH7kIgqhczyTJo";

    private const string ForkCandidateIdFixture =
        "eyJyZXNlcnZhdGlvbklkIjoicmVzZXJ2YXRpb24tMSIsInJlcXVlc3RGaW5nZXJwcmludCI6InNoYTI1NjphYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhIiwiZXhwaXJlc0F0IjoiMjAyNi0wNy0xOFQxMjoxNTowMCswMDowMCJ9.8athtPrnbX539iuINP-eRVywyVuCp4_VnmHtsbEOUkU";

    private static readonly ActivityDependencyCursorState DependencyCursor =
        new("tenant:tenant-a", "profile-fingerprint", "version-1", "Outbound", true, ["Drafts", "Versions"], "sha256-watermark", 7);

    private static readonly ActivityManagementCursorState ManagementCursor = new("tenant:tenant-a", 25, 42);

    private static readonly ActivityForkCandidateIdState ForkCandidateId =
        new("reservation-1", "sha256:" + new string('a', 64), new DateTimeOffset(2026, 7, 18, 12, 15, 0, TimeSpan.Zero));

    [Fact]
    public void Dependency_cursor_pins_the_canonical_hash_and_retained_wire_format()
    {
        var codec = new HmacActivityDependencyCursorCodec(Signing(SigningKey));

        var decoded = codec.Decode(DependencyCursorFixture);

        Assert.Equal(DependencyCursor with { Include = decoded.Include }, decoded);
        Assert.Equal(DependencyCursor.Include, decoded.Include);
        Assert.True(DependencyCursorFixture == codec.Encode(DependencyCursor), "First-stable hash freeze: changes require an explicit identity/migration decision.");
        Assert.Throws<ActivityDependencyCursorInvalidException>(() =>
            codec.Decode(new HmacActivityDependencyCursorCodec(Signing(OtherKey)).Encode(DependencyCursor)));
    }

    [Fact]
    public void Management_cursor_pins_the_canonical_hash_and_retained_wire_format()
    {
        var codec = new HmacActivityManagementCursorCodec(Signing(SigningKey));

        Assert.Equal(ManagementCursor, codec.Decode(ManagementCursorFixture));
        Assert.True(ManagementCursorFixture == codec.Encode(ManagementCursor), "First-stable hash freeze: changes require an explicit identity/migration decision.");
        Assert.Throws<ActivityManagementCursorInvalidException>(() =>
            codec.Decode(new HmacActivityManagementCursorCodec(Signing(OtherKey)).Encode(ManagementCursor)));
    }

    [Fact]
    public void Fork_candidate_id_pins_the_canonical_hash_and_retained_wire_format()
    {
        var codec = new HmacActivityForkCandidateIdCodec(Signing(SigningKey));

        Assert.Equal(ForkCandidateId, codec.Decode(ForkCandidateIdFixture));
        Assert.True(ForkCandidateIdFixture == codec.Encode(ForkCandidateId), "First-stable hash freeze: changes require an explicit identity/migration decision.");
        Assert.Throws<ActivityForkCandidateIdInvalidException>(() =>
            codec.Decode(new HmacActivityForkCandidateIdCodec(Signing(OtherKey)).Encode(ForkCandidateId)));
    }

    [Fact]
    public void Management_cursor_refuses_the_former_raw_json_signature()
    {
        var codec = new HmacActivityManagementCursorCodec(Signing(SigningKey));
        var token = codec.Encode(ManagementCursor).Split('.');
        var encodedPayload = token[0].Replace('-', '+').Replace('_', '/');
        encodedPayload += new string('=', (4 - encodedPayload.Length % 4) % 4);
        var payload = Convert.FromBase64String(encodedPayload);
        var rawSignature = System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(SigningKey), payload);
        token[1] = Convert.ToBase64String(rawSignature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Throws<ActivityManagementCursorInvalidException>(() => codec.Decode(string.Join('.', token)));
    }

    [Fact]
    public void Each_codec_names_itself_when_the_signing_key_is_too_short()
    {
        var shortKey = Signing(new string('k', 31));

        Assert.Equal("Activity dependency cursor signing key must contain at least 32 UTF-8 bytes.",
            Assert.Throws<InvalidOperationException>(() => new HmacActivityDependencyCursorCodec(shortKey)).Message);
        Assert.Equal("Activity management cursor signing key must contain at least 32 UTF-8 bytes.",
            Assert.Throws<InvalidOperationException>(() => new HmacActivityManagementCursorCodec(shortKey)).Message);
        Assert.Equal("Activity fork candidate signing key must contain at least 32 UTF-8 bytes.",
            Assert.Throws<InvalidOperationException>(() => new HmacActivityForkCandidateIdCodec(shortKey)).Message);
    }

    private static IOptions<ActivityTokenSigningOptions> Signing(string key) =>
        Options.Create(new ActivityTokenSigningOptions { SigningKey = key });
}
