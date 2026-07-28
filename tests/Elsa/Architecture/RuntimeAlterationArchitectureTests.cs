using System.Reflection;
using System.Text.Json;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Workflows.Runtime.Api.Capabilities;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Cross-assembly ratchets for issue #1016. These are deliberately architectural rather than handler behavior tests:
/// durable wire state must stay stable without CLR handler identities, while composition must retain all trusted
/// built-ins and advertise the routes only when its store/kernel are present.
/// </summary>
public sealed class RuntimeAlterationArchitectureTests
{
    [Fact]
    public void Runtime_alteration_assemblies_remain_design_free()
    {
        var assemblies = new[]
        {
            typeof(WorkflowAlterationPlanState).Assembly,
            typeof(WorkflowAlterationPlanService).Assembly,
            typeof(WorkflowsRuntimeApiFeature).Assembly
        }.Distinct();

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

        Assert.DoesNotContain(typeof(CancelWorkflowAlterationHandler).FullName!, json, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(ModifyVariableAlterationHandler).FullName!, json, StringComparison.Ordinal);
        Assert.DoesNotContain("HandlerType", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("System.Type", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_composition_registers_every_builtin_handler_store_and_capability_route()
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        AssertDescriptor(services, typeof(IWorkflowAlterationStore), ServiceLifetime.Singleton);
        AssertDescriptor(services, typeof(IWorkflowAlterationPayloadProtector), ServiceLifetime.Scoped);
        AssertDescriptor(services, typeof(WorkflowAlterationPlanService), ServiceLifetime.Scoped);
        AssertDescriptor(services, typeof(CancelWorkflowAlterationHandler), ServiceLifetime.Scoped);
        AssertDescriptor(services, typeof(ModifyVariableAlterationHandler), ServiceLifetime.Scoped);
        AssertDescriptor(services, typeof(ScheduleActivityAlterationHandler), ServiceLifetime.Scoped);
        AssertDescriptor(services, typeof(RescheduleActivityAlterationHandler), ServiceLifetime.Scoped);
        AssertDescriptor(services, typeof(MigrateWorkflowAlterationHandler), ServiceLifetime.Scoped);

        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(WorkflowAlterationHandlerContribution))
            .Select(descriptor => Assert.IsType<WorkflowAlterationHandlerContribution>(descriptor.ImplementationInstance))
            .Select(contribution => (contribution.Descriptor.Kind, contribution.Descriptor.SchemaVersion))
            .OrderBy(identity => identity.Kind, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[] { ("CancelWorkflow", 1), ("Migrate", 1), ("ModifyVariable", 1), ("RescheduleActivity", 1), ("ScheduleActivity", 1) },
            descriptors);

        using var provider = services.BuildServiceProvider();
        var source = new RuntimeAlterationCapabilitySource(provider);
        var declaration = Assert.Single(await source.GetCapabilitiesAsync());
        Assert.Equal("WorkflowsRuntimeApi.Alterations", declaration.SourceFeatureId);
        Assert.Equal(
            new[]
            {
                "workflow-alteration-job",
                "workflow-alteration-plan",
                "workflow-alteration-plan-cancel",
                "workflow-alteration-plan-jobs-page",
                "workflow-alteration-plans"
            },
            declaration.Links.Select(link => link.Rel).Order(StringComparer.Ordinal));
    }

    private static void AssertDescriptor(IServiceCollection services, Type serviceType, ServiceLifetime lifetime) =>
        Assert.Contains(services, descriptor => descriptor.ServiceType == serviceType && descriptor.Lifetime == lifetime);
}
