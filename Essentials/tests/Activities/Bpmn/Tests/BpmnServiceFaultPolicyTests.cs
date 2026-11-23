using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnServiceFaultPolicyTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("ActualNativeModelV2.json", false)]
    [InlineData("CurrentNativeModelV2.json", true)]
    public void Format2_keeps_its_absent_policy_and_accepts_only_the_current_canonical_hash(string file, bool current)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", file);
        using var fixture = JsonDocument.Parse(File.ReadAllText(path));
        var root = fixture.RootElement;
        var material = ReadMaterial(root);
        if (!current)
        {
            Assert.NotEqual(root.GetProperty("expectedContentHash").GetString(), material.ComputeContentHash());
            return;
        }
        using var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CurrentNativeModelV2.golden.json")));
        var preimage = golden.RootElement.GetProperty("canonical").GetString()!;
        Assert.Equal(golden.RootElement.GetProperty("captureSha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
        var expected = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(preimage)));
        Assert.Equal(expected, golden.RootElement.GetProperty("expectedContentHash").GetString());
        Assert.Equal(expected, root.GetProperty("expectedContentHash").GetString());
        Assert.Equal(expected, material.ComputeContentHash()); // First-stable freeze: changing this pin requires the agreed compatibility process.
    }

    internal static RuntimeModelMaterial ReadMaterial(JsonElement root)
    {
        var definition = root.GetProperty("definition").Deserialize<BpmnModelDefinition>(Web)!;
        Assert.Equal(2, definition.FormatVersion);
        Assert.Null(Assert.Single(definition.ServiceBindings!).FaultPolicy);
        var pin = root.GetProperty("identity").Deserialize<WorkflowExecutableIdentity>(Web)!;
        var serialized = JsonSerializer.SerializeToElement(definition, Web);
        Assert.DoesNotContain("faultPolicy", serialized.GetRawText());
        return new RuntimeModelMaterial(1, pin, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.NativeWorkProfile,
            serialized, new(1, []), [], definition.ServiceBindings!.Select(binding => new RuntimeModelDependency(binding.BindingId, binding.Executable)).ToArray(), []);
    }

    [Theory]
    [InlineData(BpmnServiceFaultDisposition.FinalFault, 1)]
    [InlineData(BpmnServiceFaultDisposition.HoldForRetry, 1)]
    [InlineData(BpmnServiceFaultDisposition.HoldForRetry, 2)]
    [InlineData(BpmnServiceFaultDisposition.HoldForRetry, 32)]
    public void Explicit_policies_require_format3_and_round_trip_the_complete_attempt_budget(BpmnServiceFaultDisposition disposition, int attempts)
    {
        var binding = new BpmnNativeServiceBinding("worker", new("artifact", "definition", "version", "1.0.0", "hash"),
            "worker.business-error", new(1, disposition, attempts));
        var process = JsonSerializer.SerializeToElement(new { processId = "policy" }, Web);
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(2, process, [], [binding]));
        var definition = new BpmnModelDefinition(3, process, [], [binding]);
        var saved = JsonSerializer.SerializeToElement(definition, Web);
        var restored = saved.Deserialize<BpmnModelDefinition>(Web)!;
        var policy = Assert.Single(restored.ServiceBindings!).FaultPolicy!;
        Assert.Equal(1, policy.FormatVersion);
        Assert.Equal(disposition, policy.Disposition);
        Assert.Equal(attempts, policy.MaxAttempts);
        Assert.Equal(disposition.ToString(), saved.GetProperty("serviceBindings")[0].GetProperty("faultPolicy").GetProperty("disposition").GetString());
    }

    [Fact]
    public void Format3_cannot_default_a_missing_explicit_policy()
    {
        var binding = new BpmnNativeServiceBinding("worker", new("artifact", "definition", "version", "1.0.0", "hash"), "worker.business-error");
        Assert.Throws<ArgumentException>(() => new BpmnModelDefinition(3, JsonSerializer.SerializeToElement(new { processId = "policy" }), [], [binding]));
    }

    [Theory]
    [InlineData(BpmnServiceFaultDisposition.HoldForRetry, 0)]
    [InlineData(BpmnServiceFaultDisposition.HoldForRetry, 33)]
    [InlineData(BpmnServiceFaultDisposition.FinalFault, 2)]
    [InlineData((BpmnServiceFaultDisposition)99, 2)]
    public void Unknown_or_inconsistent_attempt_policies_refuse(BpmnServiceFaultDisposition disposition, int attempts) =>
        Assert.ThrowsAny<ArgumentException>(() => new BpmnServiceFaultPolicy(1, disposition, attempts));

    [Fact]
    public void Unknown_policy_versions_refuse() =>
        Assert.Throws<NotSupportedException>(() => new BpmnServiceFaultPolicy(2, BpmnServiceFaultDisposition.HoldForRetry, 2));
}
