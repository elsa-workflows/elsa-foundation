using Elsa.Activities.Design.Api.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

/// <summary>
/// Clients hold these tokens across requests, so a token issued before a deploy must decode after it. The
/// fixtures were produced by the codecs before they moved onto the shared signed-token format; encoding the same
/// state must reproduce them byte for byte, and a token signed with any other key must be refused.
/// </summary>
public sealed class ActivityTokenWireFormatTests
{
    private const string SigningKey = "wire-format-fixture-signing-key-32-bytes+";

    private static readonly string OtherKey = new('o', 32);

    private const string DependencyCursorFixture =
        "eyJ0ZW5hbnRTY29wZSI6InRlbmFudDp0ZW5hbnQtYSIsImF1dGhvcml6YXRpb25Qcm9maWxlRmluZ2VycHJpbnQiOiJwcm9maWxlLWZpbmdlcnByaW50Iiwicm9vdFZlcnNpb25JZCI6InZlcnNpb24tMSIsImRpcmVjdGlvbiI6Ik91dGJvdW5kIiwidHJhbnNpdGl2ZSI6dHJ1ZSwiaW5jbHVkZSI6WyJEcmFmdHMiLCJWZXJzaW9ucyJdLCJ3YXRlcm1hcmsiOiJzaGEyNTYtd2F0ZXJtYXJrIiwicG9zaXRpb24iOjd9.PhMVv_QvNi15ldoaBNq8KmYGtiOossm51c2doD4t5ys";

    private const string ManagementCursorFixture =
        "eyJzY29wZSI6InRlbmFudDp0ZW5hbnQtYSIsIm9mZnNldCI6MjUsInNuYXBzaG90U2VxdWVuY2UiOjQyfQ.IhbNt4LfKFt2oFgaY4IV3uu6NfwFOzZpC6ik0WMscFU";

    private const string ForkCandidateIdFixture =
        "eyJyZXNlcnZhdGlvbklkIjoicmVzZXJ2YXRpb24tMSIsInJlcXVlc3RGaW5nZXJwcmludCI6InNoYTI1NjphYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhIiwiZXhwaXJlc0F0IjoiMjAyNi0wNy0xOFQxMjoxNTowMCswMDowMCJ9._UIcc8UvAhFryvHjhzUaJiJa6To86Jvu-V0o2Qm4ymE";

    private static readonly ActivityDependencyCursorState DependencyCursor =
        new("tenant:tenant-a", "profile-fingerprint", "version-1", "Outbound", true, ["Drafts", "Versions"], "sha256-watermark", 7);

    private static readonly ActivityManagementCursorState ManagementCursor = new("tenant:tenant-a", 25, 42);

    private static readonly ActivityForkCandidateIdState ForkCandidateId =
        new("reservation-1", "sha256:" + new string('a', 64), new DateTimeOffset(2026, 7, 18, 12, 15, 0, TimeSpan.Zero));

    [Fact]
    public void Dependency_cursor_issued_before_the_shared_format_still_decodes_and_encodes_identically()
    {
        var codec = new HmacActivityDependencyCursorCodec(Signing(SigningKey));

        var decoded = codec.Decode(DependencyCursorFixture);

        Assert.Equal(DependencyCursor with { Include = decoded.Include }, decoded);
        Assert.Equal(DependencyCursor.Include, decoded.Include);
        Assert.Equal(DependencyCursorFixture, codec.Encode(DependencyCursor));
        Assert.Throws<ActivityDependencyCursorInvalidException>(() =>
            codec.Decode(new HmacActivityDependencyCursorCodec(Signing(OtherKey)).Encode(DependencyCursor)));
    }

    [Fact]
    public void Management_cursor_issued_before_the_shared_format_still_decodes_and_encodes_identically()
    {
        var codec = new HmacActivityManagementCursorCodec(Signing(SigningKey));

        Assert.Equal(ManagementCursor, codec.Decode(ManagementCursorFixture));
        Assert.Equal(ManagementCursorFixture, codec.Encode(ManagementCursor));
        Assert.Throws<ActivityManagementCursorInvalidException>(() =>
            codec.Decode(new HmacActivityManagementCursorCodec(Signing(OtherKey)).Encode(ManagementCursor)));
    }

    [Fact]
    public void Fork_candidate_id_issued_before_the_shared_format_still_decodes_and_encodes_identically()
    {
        var codec = new HmacActivityForkCandidateIdCodec(Signing(SigningKey));

        Assert.Equal(ForkCandidateId, codec.Decode(ForkCandidateIdFixture));
        Assert.Equal(ForkCandidateIdFixture, codec.Encode(ForkCandidateId));
        Assert.Throws<ActivityForkCandidateIdInvalidException>(() =>
            codec.Decode(new HmacActivityForkCandidateIdCodec(Signing(OtherKey)).Encode(ForkCandidateId)));
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
