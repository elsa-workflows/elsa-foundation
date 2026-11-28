using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Cross-assembly ratchets for issue #1016. These are deliberately architectural rather than handler behavior tests:
/// historical persisted alteration wire state must stay stable without carrying CLR handler identities.
/// </summary>
public sealed class RuntimeAlterationArchitectureTests
{
    [Fact]
    public void Historical_alteration_wire_models_remain_design_free()
    {
        var assemblies = new[] { typeof(WorkflowAlterationPlanState).Assembly };

        foreach (var assembly in assemblies)
        {
            var forbidden = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .OfType<string>()
                .Where(name => name.StartsWith("Elsa.Workflows.Design.", StringComparison.Ordinal) ||
                               name.StartsWith("Elsa.Activities.Design.", StringComparison.Ordinal))
                .ToArray();
            Assert.True(forbidden.Length == 0, $"{assembly.GetName().Name} references Design assemblies:{Environment.NewLine}{string.Join(Environment.NewLine, forbidden)}");
        }
    }

    [Fact]
    public void Durable_alteration_wire_models_do_not_expose_clr_handler_identities()
    {
        var durableWireModels = new[]
        {
            typeof(ProtectedWorkflowAlterationPayload),
            typeof(WorkflowAlterationAuthorityScope),
            typeof(WorkflowAlterationOperatorProvenance),
            typeof(WorkflowAlterationTargetSelector),
            typeof(WorkflowAlterationQuerySelector),
            typeof(WorkflowAlterationPlanState),
            typeof(WorkflowAlterationCapturedConcurrency),
            typeof(WorkflowAlterationJobClaim),
            typeof(WorkflowAlterationOutcome),
            typeof(WorkflowAlterationJobState)
        };

        var properties = durableWireModels.SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)).ToArray();
        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(Type));
        Assert.DoesNotContain(properties, property => property.Name.Contains("handler", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("implementationType", StringComparison.OrdinalIgnoreCase));

        var plan = WorkflowAlterationPlanState.CreateCapturing(
            "alteration-plan-wire",
            new WorkflowAlterationAuthorityScope("tenant", "system", "operator"),
            new WorkflowAlterationOperatorProvenance("operator", "correlation"),
            "idempotency-hash",
            "request-hash",
            new ProtectedWorkflowAlterationPayload("key-1", "AES-256-GCM", "ciphertext"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"]),
            DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(plan);

        Assert.DoesNotContain("CancelWorkflowAlterationHandler", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ModifyVariableAlterationHandler", json, StringComparison.Ordinal);
        Assert.DoesNotContain("HandlerType", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Type", json, StringComparison.Ordinal);
    }

}
