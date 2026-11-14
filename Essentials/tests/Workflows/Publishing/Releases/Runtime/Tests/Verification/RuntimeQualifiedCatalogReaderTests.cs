using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Xunit;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Tests.Verification;

public sealed class RuntimeQualifiedCatalogReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Qualified_reader_supports_exact_legacy_and_original_evidence_profiles(bool original)
    {
        var f = new RuntimeReleaseFixture(); var value = Version(original);
        var member = f.Add(f.Codec.EncodeActivityVersion(value));
        var actual = new ReleaseRuntimeSiteReader(f.Cache).ReadActivityVersion(member.Key, f);
        Assert.Equal(value.VersionId, actual.VersionId); Assert.Equal(original, actual.Publication is not null);
    }

    [Theory]
    [InlineData("header", "runtime_site_kind_unsupported")]
    [InlineData("owner", "runtime_site_member_mismatch")]
    [InlineData("profile", "captured_runtime_codec_profile_conflict")]
    public void Qualified_reader_refuses_unsupported_headers_owner_aliases_and_relabelled_evidence(string change, string code)
    {
        var f = new RuntimeReleaseFixture(); var encoded = f.Codec.EncodeActivityVersion(Version(true)); var d = encoded.Descriptor;
        var header = new ReleaseBundleMember(d.Kind, d.Identity, d.Codec, change == "header" ? 3 : change == "profile" ? 1 : d.CodecVersion, d.PayloadDigest, d.ByteLength);
        var member = f.Add(new(header, change == "owner" ? new("semver", "9.0.0", null) : encoded.OwnerVersion, encoded.GetPayloadBytes()));
        var error = Assert.Throws<ReleaseRuntimeArtifactException>(() => new ReleaseRuntimeSiteReader(f.Cache).ReadActivityVersion(member.Key, f));
        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void Qualified_template_reader_checks_kind_identity_and_unversioned_owner()
    {
        var f = new RuntimeReleaseFixture(); var value = RuntimeReleaseFixture.Template(); var encoded = f.Codec.EncodeTemplate(new(RuntimeReleaseFixture.Scope, value));
        var member = f.Add(encoded); Assert.Equal(value.TemplateId, new ReleaseRuntimeSiteReader(f.Cache).ReadTemplate(member.Key, f).Template.TemplateId);
        var bad = new RuntimeReleaseFixture(); bad.Add(new(encoded.Descriptor, new("semver", "1.0.0", null), encoded.GetPayloadBytes()));
        Assert.Equal("runtime_site_member_mismatch", Assert.Throws<ReleaseRuntimeArtifactException>(() => new ReleaseRuntimeSiteReader(bad.Cache).ReadTemplate(member.Key, bad)).Code);
        Assert.Equal("runtime_site_kind_unsupported", Assert.Throws<ReleaseRuntimeArtifactException>(() => new ReleaseRuntimeSiteReader(f.Cache).ReadActivityVersion(member.Key, f)).Code);
    }
    private static ReleaseActivityVersion Version(bool original) => new(RuntimeReleaseFixture.Scope, "definition", "version", "1.0.0", "fixture", "AuthorableActivity",
        RuntimeReleaseFixture.Json("{}"), "fixture", "1", RuntimeReleaseFixture.Json("{}"), "fingerprint", "template", "hash", "source-reference", "fixture", "source",
        original ? new(true, null, "{}", "{\"providerKey\":\"fixture\",\"schemaVersion\":\"1\",\"payload\":{}}", RuntimeReleaseFixture.Json("{}")) : null);
}
