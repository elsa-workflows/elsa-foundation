using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed partial class WorkflowExecutableHashContractTests
{
    // Pins are SHA256 of independently assembled canonical UTF8 payloads from the pre-S2 V1/V2 formats.
    [Theory]
    [InlineData(false, false, "dbca97b2ad4af9f9543e1cdc41e19956192543b017a0cf1d9782e42cff784755", "e23dc15e97c03f1ad2f534d84e2d73e11902ff56cefb0e54f290f2ac7eef93c7")]
    [InlineData(true, false, "4d912a2edf11bcfac2851bd6d7e6710e709561f8d9bf16fdecb5152b5f5e5e78", "3ee8f3ef5ab05025969fbb9ab955faaf829f4a28916439a8cff531c72934bb2d")]
    [InlineData(false, false, "fc7261b5d327339ee2f48f078d9886277c5440ff1925ef4707608240aed1e993", "e23dc15e97c03f1ad2f534d84e2d73e11902ff56cefb0e54f290f2ac7eef93c7")]
    [InlineData(true, false, "75ac6cf2f03223d24aed226394e95235f7e03875a4ba44b82b82b9a13e9add65", "3ee8f3ef5ab05025969fbb9ab955faaf829f4a28916439a8cff531c72934bb2d")]
    [InlineData(false, true, "7bc5e620fb003209aa715e86c1057e7c35a4af7f18005722b6bda32885f307f9", "9bbff4706a4799e1088fc6489519162a0e98d306046bf01e5d846d35036e0042")]
    [InlineData(true, true, "8626a4c3d081258946fb6917ff5185c3de89a592adf4d0ef5a56e21a11b3ab24", "4fd2666962a6d3f5e383dc5615c0928218a3ff5d892484cb66d420ec6754fa1f")]
    public void Retained_input_hashes_verify_without_accepting_changed_nullability_or_new_policies(bool nullable, bool member, string oldPin, string newPin)
    {
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
        Assert.True(_hasher.MatchesHash(old));
        Assert.False(_hasher.MatchesHash(Artifact(!nullable, old.Identity.ArtifactHash)));
        Assert.False(_hasher.MatchesHash(Artifact(nullable, old.Identity.ArtifactHash, new(true, false))));
        Assert.Equal("sha256:" + newPin, Hash(old));
        Assert.True(_hasher.MatchesHash(Artifact(nullable, "sha256:" + newPin)));
        Assert.False(_hasher.MatchesHash(Artifact(!nullable, "sha256:" + newPin)));
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
