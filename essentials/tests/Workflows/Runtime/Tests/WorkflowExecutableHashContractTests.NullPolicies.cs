using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class WorkflowExecutableHashContractTests
{
    private const string CurrentInputHashFixturePrefix = "runtime-current-null-policy/";
    private const string CurrentInputHashFreeze = "First-stable hash freeze: changes require an explicit identity/migration decision; S-9 Q11 verifies only the current canonical recipe.";

    // Old pins remain refusal controls. Current pins are independently generated from the six literal
    // compiler-hash-inputs.json preimages; two historical alias pairs have the same current semantics.
    [Theory]
    [InlineData(false, false, "dbca97b2ad4af9f9543e1cdc41e19956192543b017a0cf1d9782e42cff784755", "7fecf8879670cd1961a3e0ae0e71a711fff0e2bc864a6856e4fd2dc7d125e251", "nonnull-v1")]
    [InlineData(true, false, "4d912a2edf11bcfac2851bd6d7e6710e709561f8d9bf16fdecb5152b5f5e5e78", "788acd9eb47d0fd3bbf34d380033705758f141b301823ba4c3fa66029c136c06", "nullable-v1")]
    [InlineData(false, false, "fc7261b5d327339ee2f48f078d9886277c5440ff1925ef4707608240aed1e993", "7fecf8879670cd1961a3e0ae0e71a711fff0e2bc864a6856e4fd2dc7d125e251", "nonnull-v2-alias")]
    [InlineData(true, false, "75ac6cf2f03223d24aed226394e95235f7e03875a4ba44b82b82b9a13e9add65", "788acd9eb47d0fd3bbf34d380033705758f141b301823ba4c3fa66029c136c06", "nullable-v2-alias")]
    [InlineData(false, true, "7bc5e620fb003209aa715e86c1057e7c35a4af7f18005722b6bda32885f307f9", "11f78f6804ca0e392d87654bb2ae3288302f50d5ad6db6c9fd0bb2a453fa7a66", "member-nonnull")]
    [InlineData(true, true, "8626a4c3d081258946fb6917ff5185c3de89a592adf4d0ef5a56e21a11b3ab24", "0c58e9fa8cd484d059cf1a44c0bed61bb37e9eac0581ea36229df600ce91a0f5", "member-nullable")]
    public void Legacy_input_hashes_are_refused_and_current_pins_bind_nullability_and_value_policies(bool nullable, bool member, string oldPin, string newPin, string fixtureName)
    {
        var vector = CurrentInputHashVectors().Single(row => row.GetProperty("name").GetString() == CurrentInputHashFixturePrefix + fixtureName);
        Assert.True(vector.GetProperty("legacyPin").GetString() == oldPin && vector.GetProperty("sha256").GetString() == newPin,
            CurrentInputHashFreeze);
        Assert.Equal(nullable, vector.GetProperty("nullable").GetBoolean());
        Assert.Equal(member, vector.GetProperty("member").GetBoolean());
        var bindings = new Dictionary<string, RuntimeInputBinding>();
        if (member) bindings["value"] = new("value", new("String"), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequestMember, selection: new RuntimeReadSelection(1, ["id"], RuntimeReadSelectionMode.DeferredFit),
            workflowRequest: new("input"));
        var root = new ExecutableNode("root", "root", "Test", "1", new RuntimeActivityDescriptor("test", "1", JsonSerializer.SerializeToElement(new { })),
            bindings, new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
        WorkflowExecutable Artifact(bool acceptsNull, string hash, WorkflowValuePolicies? policies = null) => new(
            new("artifact", "definition", "version", "1", hash), root, new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(), new(1, [new("input", new TypeReference(member ? "Any" : "String"), false, isNullable: acceptsNull)]), [], null, null,
            IncidentStrategyBuiltIns.FaultReference, null, null, policies);
        var old = Artifact(nullable, "sha256:" + oldPin);
        Assert.False(_hasher.MatchesHash(old), CurrentInputHashFreeze);
        Assert.False(_hasher.MatchesHash(Artifact(!nullable, old.Identity.ArtifactHash)), CurrentInputHashFreeze);
        Assert.False(_hasher.MatchesHash(Artifact(nullable, old.Identity.ArtifactHash, new(true, false))), CurrentInputHashFreeze);
        Assert.True(Hash(old) == "sha256:" + newPin, CurrentInputHashFreeze);
        Assert.True(_hasher.MatchesHash(Artifact(nullable, "sha256:" + newPin)), CurrentInputHashFreeze);
        Assert.False(_hasher.MatchesHash(Artifact(!nullable, "sha256:" + newPin)), CurrentInputHashFreeze);
        foreach (var policies in new WorkflowValuePolicies[] { new(true, false), new(false, true), new(true, true) })
            Assert.False(_hasher.MatchesHash(Artifact(nullable, "sha256:" + newPin, policies)), CurrentInputHashFreeze);
    }

    [Fact]
    public void Six_legacy_refusal_fixtures_have_four_current_semantic_preimages_and_pins()
    {
        var vectors = CurrentInputHashVectors();
        Assert.True(vectors.Count == 6, CurrentInputHashFreeze);
        Assert.True(vectors.Select(row => row.GetProperty("legacyPin").GetString()).Distinct(StringComparer.Ordinal).Count() == 6, CurrentInputHashFreeze);
        Assert.True(vectors.Select(row => row.GetProperty("canonical").GetString()).Distinct(StringComparer.Ordinal).Count() == 4, CurrentInputHashFreeze);
        Assert.True(vectors.Select(row => row.GetProperty("sha256").GetString()).Distinct(StringComparer.Ordinal).Count() == 4, CurrentInputHashFreeze);
        foreach (var nullable in new[] { false, true })
        {
            var aliases = vectors.Where(row => row.GetProperty("nullable").GetBoolean() == nullable && !row.GetProperty("member").GetBoolean()).ToArray();
            Assert.Equal(2, aliases.Length);
            Assert.True(aliases[0].GetProperty("canonical").GetString() == aliases[1].GetProperty("canonical").GetString(), CurrentInputHashFreeze);
        }
    }

    private static IReadOnlyList<JsonElement> CurrentInputHashVectors()
    {
        using var stream = typeof(WorkflowExecutableHashContractTests).Assembly.GetManifestResourceStream("compiler-hash-vectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray()
            .Where(row => row.GetProperty("name").GetString()!.StartsWith(CurrentInputHashFixturePrefix, StringComparison.Ordinal))
            .Select(row => row.Clone()).ToArray();
    }

    [Fact]
    public void Each_policy_and_the_input_type_nullability_participate_in_identity()
    {
        var source = LegacyWorkflowHashFixture.Load();
        string Compute(WorkflowValuePolicies policies, WorkflowExecutableInputContract? contract = null) =>
            _hasher.ComputeHash(source.RootActivity, contract ?? source.InputContract!, source.Dependencies,
                source.PersistenceMode, source.WorkflowVariables, source.IncidentStrategy, policies);

        Assert.Equal(Hash(source), Compute(WorkflowValuePolicies.Off));
        var hashes = new[] { Compute(new()), Compute(new(true, false)), Compute(new(false, true)), Compute(new(true, true)) };
        Assert.Equal(4, hashes.Distinct(StringComparer.Ordinal).Count());
        var nullable = new WorkflowExecutableInputContract(1, [new("input", new TypeReference("String"), false, isNullable: true)]);
        var nonNullable = new WorkflowExecutableInputContract(1, [new("input", new TypeReference("Int32"), false)]);
        Assert.NotEqual(Compute(new(), nullable), Compute(new(), nonNullable));
        var sameTypeNonNullable = new WorkflowExecutableInputContract(1, [new("input", new TypeReference("String"), false)]);
        Assert.NotEqual(Compute(new(), nullable), Compute(new(), sameTypeNonNullable));
    }
}
