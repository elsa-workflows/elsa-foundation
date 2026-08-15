using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Golden-hash guard for the Publishing → Runtime relocation of the executable hasher (spec 151,
/// FR-B-010; pinned by the 2026-08-15 architect review).
/// </summary>
/// <remarks>
/// <para>
/// The hash <b>is</b> artifact identity under ADR 0038, so the extraction had to be byte-stable:
/// identical input must hash identically before and after the move. These values were captured from
/// the relocated hasher and are corroborated independently by
/// <c>WorkflowExecutableCompilerGoldenTests</c> in the Publishing.Api suite, which pinned the same
/// algorithm end-to-end through the compiler <em>before</em> the move and still passes after it.
/// </para>
/// <para>
/// <b>If a change to the hasher makes this test fail, the change is wrong.</b> Do not re-baseline
/// these constants to make it pass: every existing artifact id in every store is derived from this
/// algorithm, and the importer's recompute-before-persist guard (FR-B-010) compares against hashes
/// produced by other engines running the same code. Re-baselining silently invalidates both.
/// </para>
/// </remarks>
public sealed class WorkflowExecutableHasherGoldenTests
{
    private const string GoldenNodeTreeHash = "sha256:f3a1497e214885029ba678f56b68e34336e5409f8bf10756e4d75b1ad4836448";

    [Fact]
    public void Node_tree_hash_is_stable_and_derives_a_content_addressed_artifact_id()
    {
        var hasher = new WorkflowExecutableHasher();

        var hash = hasher.ComputeHash(GoldenNode());

        Assert.Equal(GoldenNodeTreeHash, hash);
        Assert.StartsWith("sha256:", hash, StringComparison.Ordinal);
        // 'sha256:' + 64 hex characters.
        Assert.Equal(71, hash.Length);

        // The artifact id embeds the first 12 hex characters of the hash — this is the coupling that
        // makes ids content-addressed, and what the importer verifies before persisting.
        var artifactId = hasher.CreateArtifactId("artifact-", hash);
        Assert.Equal($"artifact-{hash["sha256:".Length..("sha256:".Length + 12)]}", artifactId);
    }

    [Fact]
    public void Identical_input_hashes_identically_across_instances()
    {
        // Determinism is the property the content-addressing invariant rests on: two engines running
        // this code must agree, or the importer's recompute guard would reject valid artifacts.
        Assert.Equal(
            new WorkflowExecutableHasher().ComputeHash(GoldenNode()),
            new WorkflowExecutableHasher().ComputeHash(GoldenNode()));
    }

    [Fact]
    public void A_behavioural_difference_changes_the_hash()
    {
        var hasher = new WorkflowExecutableHasher();

        Assert.NotEqual(
            hasher.ComputeHash(GoldenNode()),
            hasher.ComputeHash(GoldenNode(activityType: "Acme.OtherActivity")));
    }

    [Fact]
    public void CreateArtifactId_rejects_a_hash_that_is_not_in_the_canonical_format()
    {
        var hasher = new WorkflowExecutableHasher();

        Assert.Throws<ArgumentException>(() => hasher.CreateArtifactId("artifact-", "md5:abc"));
        Assert.Throws<ArgumentException>(() => hasher.CreateArtifactId("artifact-", "sha256:short"));
    }

    private static ExecutableNode GoldenNode(string activityType = "Acme.SampleActivity") =>
        new(
            "node-1",
            "authored-1",
            activityType,
            "1.0.0",
            typeof(Elsa.Primitives.Models.ClrActivityDescriptor).FullName!,
            JsonSerializer.SerializeToElement(new { typeAlias = "Acme.SampleActivity" }),
            new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
}
