using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;

namespace Elsa.Etl.Tests;

public sealed class EtlRuntimeIdentityTests
{
    [Fact]
    public void Artifact_identity_keeps_the_existing_v1_published_encoding() =>
        Assert.Equal("etl-v1-54cc25fd04a13358a90c0874814a3f2a34abf1256463af6287ec823e3128d249",
            EtlArtifactJson.CreateArtifactId(1, "orders", 1, new string('a', 64)));

    [Fact]
    public void Scope_key_preserves_null_named_and_opaque_tenants()
    {
        var scopes = new[]
        {
            new EtlScope(null, "partition"), new EtlScope("null", "partition"),
            new EtlScope("default", "partition"), new EtlScope("tenant-\ud800", "partition"),
            new EtlScope("tenant-\ud801", "partition"), new EtlScope(null, "other-partition")
        };
        Assert.Equal(scopes.Length, scopes.Select(EtlRuntimeIdentity.ScopeKey).Distinct().Count());
    }

    [Fact]
    public void Invocation_framing_preserves_opaque_scope_and_address_identity()
    {
        var invocation = EtlRuntimeFixtures.Admission().Invocation;
        var first = invocation with { Scope = new EtlScope("a", "bc") };
        var second = invocation with { Scope = new EtlScope("ab", "c") };
        Assert.NotEqual(EtlRuntimeIdentity.InvocationFingerprint(first), EtlRuntimeIdentity.InvocationFingerprint(second));
        Assert.NotEqual(EtlRuntimeIdentity.InvocationFingerprint(invocation),
            EtlRuntimeIdentity.InvocationFingerprint(invocation with { ResultAddress = invocation.ResultAddress with { BookmarkId = "another" } }));
    }

    [Fact]
    public void Distinct_opaque_UTF16_code_units_cannot_collapse_to_one_execution_identity()
    {
        var invocation = EtlRuntimeFixtures.Admission().Invocation;
        var first = invocation with { Scope = new EtlScope("tenant-\ud800", "partition") };
        var second = invocation with { Scope = new EtlScope("tenant-\ud801", "partition") };
        Assert.NotEqual(EtlRuntimeIdentity.InvocationFingerprint(first), EtlRuntimeIdentity.InvocationFingerprint(second));
    }

    [Fact]
    public void Batch_identity_is_stable_across_culture_and_tracks_range_input_and_destination()
    {
        var admission = EtlRuntimeFixtures.Admission();
        var prepared = EtlRuntimeFixtures.Prepared();
        var first = EtlRuntimeIdentity.BatchId(admission, prepared, 0, 1, 3, 80, EtlRuntimeFixtures.Hash);
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Assert.Equal(first, EtlRuntimeIdentity.BatchId(admission, prepared, 0, 1, 3, 80, EtlRuntimeFixtures.Hash));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        Assert.NotEqual(first, EtlRuntimeIdentity.BatchId(admission, prepared, 0, 1, 4, 80, EtlRuntimeFixtures.Hash));
        Assert.NotEqual(first, EtlRuntimeIdentity.BatchId(admission, prepared, 0, 1, 3, 81, EtlRuntimeFixtures.Hash));
        var moved = admission with { Bindings = admission.Bindings with { Output = admission.Bindings.Output with { Revision = "v2" } } };
        Assert.NotEqual(first, EtlRuntimeIdentity.BatchId(moved, prepared, 0, 1, 3, 80, EtlRuntimeFixtures.Hash));
    }

    [Fact]
    public void Output_key_matches_the_file_publisher_and_routes_are_distinct()
    {
        var admission = EtlRuntimeFixtures.Admission();
        var identity = EtlRuntimeIdentity.OutputIdentity(admission, "batch", EtlOutputRoute.Main);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant() + ".jsonl";
        Assert.Equal(expected, EtlRuntimeIdentity.OutputKey(admission, "batch", EtlOutputRoute.Main));
        Assert.NotEqual(expected, EtlRuntimeIdentity.OutputKey(admission, "batch", EtlOutputRoute.Error));
        Assert.Throws<ArgumentOutOfRangeException>(() => EtlRuntimeIdentity.OutputKey(admission, "batch", (EtlOutputRoute)42));
    }
}
