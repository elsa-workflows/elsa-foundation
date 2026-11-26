using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnServiceFaultPolicyTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void The_actual_pre_policy_definition_keeps_its_original_material_hash_and_absent_policy_wire()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActualNativeModelV2.json")));
        var root = fixture.RootElement;
        var definition = root.GetProperty("definition").Deserialize<BpmnModelDefinition>(Web)!;
        Assert.Equal(2, definition.FormatVersion);
        Assert.Null(Assert.Single(definition.ServiceBindings!).FaultPolicy);
        var pin = root.GetProperty("identity").Deserialize<WorkflowExecutableIdentity>(Web)!;
        var serialized = JsonSerializer.SerializeToElement(definition, Web);
        Assert.DoesNotContain("faultPolicy", serialized.GetRawText());
        var material = new RuntimeModelMaterial(1, pin, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), BpmnModelProtocol.NativeWorkProfile,
            serialized, new(1, []), [], definition.ServiceBindings!.Select(binding => new RuntimeModelDependency(binding.BindingId, binding.Executable)).ToArray(), []);
        Assert.Equal(root.GetProperty("expectedContentHash").GetString(), material.ComputeContentHash());
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
