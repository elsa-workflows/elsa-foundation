using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

// Spec 1193 slice F (D4): the exact Executable read for other domains through Workflows Runtime Core, over the inspector
// the executable endpoint uses, with exactly what that endpoint discloses.
public sealed class WorkflowExecutableInspectionTests
{
    private readonly DateTimeOffset _now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryWorkflowExecutableStore _executables = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _references = new();
    private readonly WorkflowExecutableInspector _inspector;

    public WorkflowExecutableInspectionTests() =>
        _inspector = new WorkflowExecutableInspector(_executables, _references, new InMemoryWorkflowExecutionStateStore(), new FakeTimeProvider(_now));

    [Fact]
    public async Task Reads_what_the_executable_endpoint_returns_mapped_to_the_core_models()
    {
        await SeedAsync();
        var inspection = Create();

        var details = await inspection.GetAsync("artifact-1");
        var view = await _inspector.GetAsync("artifact-1");

        Assert.NotNull(details);
        Assert.Equal((view!.ArtifactId, view.ArtifactHash, view.CreatedAt, view.RootActivityType, view.RootActivityVersion, view.NodeCount, view.LiveSourceReferenceCount),
            (details.ArtifactId, details.ArtifactHash, details.CreatedAt, details.RootActivityType, details.RootActivityVersion, details.NodeCount, details.LiveSourceReferenceCount));
        Assert.Equal("sha256:test", details.ArtifactHash);
        Assert.Equal(view.References.Select(item => (item.SourceReferenceId, item.Scope, item.DefinitionId, item.DefinitionVersionId, item.ArtifactVersion, item.Live)),
            details.SourceReferences.Select(item => (item.SourceReferenceId, item.Scope, item.DefinitionId, item.DefinitionVersionId, item.ArtifactVersion, item.Live)));
        Assert.Equal(["published", "test-run"], details.SourceReferences.Select(item => item.SourceReferenceId).Order());
        var child = Assert.Single(Assert.Single(details.RootActivity.Slots).Nodes);
        Assert.Equal(("child", "Test.Child"), (child.ExecutableNodeId, child.ActivityType));
        var connection = Assert.Single(details.RootActivity.Connections);
        Assert.Equal(("child", "Done", "child", (string?)null), (connection.Source, connection.SourcePort, connection.Target, connection.TargetPort));
    }

    [Fact]
    public async Task Never_discloses_a_binding_value_or_source_detail()
    {
        await SeedAsync();
        var details = await Create().GetAsync("artifact-1");
        var input = Assert.Single(details!.RootActivity.Inputs);
        Assert.Equal(("secret-key", "Literal", true), (input.Name, input.Source, input.IsSensitive));
        Assert.DoesNotContain("must-not-leak", JsonSerializer.Serialize(details), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_artifact_reads_as_absent()
    {
        Assert.Null(await Create().GetAsync("missing"));
        Assert.Null(await Create().GetAsync(" "));
    }

    [Fact]
    public void The_feature_registers_one_internal_sealed_inspection()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IWorkflowExecutableInspection));
        Assert.Equal("WorkflowExecutableInspection", descriptor.ImplementationType!.Name);
        Assert.True(descriptor.ImplementationType.IsSealed);
        Assert.False(descriptor.ImplementationType.IsPublic);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void A_second_inspection_registration_fails_configuration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowExecutableInspection, Duplicate>();
        services.AddScoped<IWorkflowExecutableInspection, Duplicate>();
        var error = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeApiFeature().ConfigureServices(services));
        Assert.Contains("IWorkflowExecutableInspection has multiple registrations", error.Message);
    }

    [Fact]
    public async Task A_second_inspection_registration_fails_shell_startup()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddScoped<IWorkflowExecutableInspection, Duplicate>();
        var task = services.Where(item => item.ServiceType == typeof(IStartupTask)).Select(item => item.ImplementationInstance)
            .OfType<IStartupTask>().Single(item => item.GetType().Name == "WorkflowExecutableInspectionStartupTask");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync(CancellationToken.None));
        Assert.Contains("IWorkflowExecutableInspection has multiple registrations", error.Message);
    }

    private IWorkflowExecutableInspection Create() => (IWorkflowExecutableInspection)Activator.CreateInstance(
        RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Services.WorkflowExecutableInspection")!, _inspector)!;

    private async Task SeedAsync()
    {
        var sensitive = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var binding = new RuntimeInputBinding("secret-key", new ValueTypeDescriptor("String"), sensitive, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new ValueTypeDescriptor("String"), JsonSerializer.SerializeToElement("must-not-leak"), sensitive));
        var child = new ExecutableNode("child", "child", "Test.Child", "1.0.0",
            new RuntimeActivityDescriptor("Test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>());
        var root = new ExecutableNode("root", "root", "Test.Root", "1.0.0",
            new RuntimeActivityDescriptor("Test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding> { ["secret-key"] = binding }, new Dictionary<string, RuntimeOutputCapture>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot("activities", [child])],
            structure: new ExecutableActivityStructure("Test.Flow", "1", JsonSerializer.SerializeToElement(new
            {
                connections = new[] { new { source = new { nodeId = "child", port = "Done" }, target = new { nodeId = "child" } } }
            })));
        await _executables.SaveAsync(new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            root, new Dictionary<string, WorkflowExecutableResumeTarget>(), _now, new Dictionary<string, string>(), null, null, IncidentStrategyBuiltIns.FaultReference));
        await _references.SaveAsync(Reference("published", WorkflowExecutableReferenceScope.Published, _now.AddMinutes(-10)));
        await _references.SaveAsync(Reference("test-run", WorkflowExecutableReferenceScope.TestRun, _now.AddMinutes(-1), _now.AddHours(1)));
    }

    private static WorkflowExecutableSourceReference Reference(string id, WorkflowExecutableReferenceScope scope, DateTimeOffset createdAt, DateTimeOffset? expiresAt = null) =>
        new(id, "artifact-1", "WorkflowDefinitionVersion", "version-1", "1.0.0", "definition-1", "version-1", "1.0.0", createdAt,
            scope == WorkflowExecutableReferenceScope.Published ? createdAt : null, scope, expiresAt);

    private sealed class Duplicate : IWorkflowExecutableInspection
    {
        public ValueTask<WorkflowExecutableInspectionDetails?> GetAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutableInspectionDetails?>(null);
    }
}
