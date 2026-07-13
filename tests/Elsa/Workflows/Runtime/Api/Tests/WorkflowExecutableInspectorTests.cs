using System.Reflection;
using System.Text.Json;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

/// <summary>RED ownership and HTTP contract for moving executable inspection out of Publishing.</summary>
public sealed class WorkflowExecutableInspectorTests
{
    [Fact]
    public void Runtime_owns_the_self_contained_executable_inspector()
    {
        var inspector = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Services.WorkflowExecutableInspector");
        Assert.NotNull(inspector);
        var dependencies = inspector!.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType.FullName).ToArray();

        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableStore", dependencies);
        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutableSourceReferenceStore", dependencies);
        Assert.Contains("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionStateStore", dependencies);
        Assert.DoesNotContain(dependencies, dependency => dependency?.Contains("Design", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData("runtime/workflows/executables")]
    [InlineData("runtime/workflows/executables/{artifactId}")]
    [InlineData("runtime/workflows/executables/{artifactId}/provenance")]
    public void Runtime_owns_each_canonical_executable_read_route(string route)
    {
        var endpoint = RuntimeApiEndpointTestFactory.FindByRoute(route);

        Assert.Contains(PermissionNames.WorkflowRuntimeRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Executable_list_exposes_retention_counts_without_definition_reads()
    {
        var response = RuntimeApiEndpointTestFactory.Contract(
            RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/executables")).Response;
        var items = response.GetProperty("Items", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(items);
        var row = ElementType(items!.PropertyType);

        AssertProperties(row, "ArtifactId", "CreatedAt", "LiveSourceReferenceCount", "RetainedExecutionCount");
    }

    [Fact]
    public void Provenance_is_read_only_and_reports_collection_protection()
    {
        var response = RuntimeApiEndpointTestFactory.Contract(
            RuntimeApiEndpointTestFactory.FindByRoute("runtime/workflows/executables/{artifactId}/provenance")).Response;

        AssertProperties(response, "ArtifactId", "SourceReferences", "RetainedExecutionCount", "ProtectedFromCollection");
    }

    [Fact]
    public async Task Inspector_reports_live_source_and_retained_execution_roots()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var executableStore = new InMemoryWorkflowExecutableStore();
        var referenceStore = new InMemoryWorkflowExecutableSourceReferenceStore();
        var executionStore = new InMemoryWorkflowExecutionStateStore();
        var executable = Executable(now);
        await executableStore.SaveAsync(executable);
        await referenceStore.SaveAsync(new WorkflowExecutableSourceReference(
            "reference-1", "artifact-1", "WorkflowDefinitionVersion", "version-1", "1.0.0",
            "definition-1", "version-1", "1.0.0", now, now,
            WorkflowExecutableReferenceScope.Published));
        await executionStore.SaveAsync(new WorkflowExecutionState(
            "execution-1", executable.Identity, WorkflowExecutionStatus.Completed, null, now, now, now, now,
            null, null, null, new Dictionary<string, string>()));
        var inspector = new WorkflowExecutableInspector(executableStore, referenceStore, executionStore, new FixedTimeProvider(now));

        var summary = Assert.Single((await inspector.ListAsync()).Items);
        var provenance = await inspector.GetProvenanceAsync("artifact-1");

        Assert.Equal(1, summary.LiveSourceReferenceCount);
        Assert.Equal(1, summary.RetainedExecutionCount);
        Assert.True(provenance!.ProtectedFromCollection);
        Assert.Equal(1, provenance.RetainedExecutionCount);
        Assert.True(Assert.Single(provenance.SourceReferences).Live);
    }

    [Fact]
    public async Task Detail_projects_nodes_without_descriptor_payloads()
    {
        var now = DateTimeOffset.UnixEpoch;
        var executableStore = new InMemoryWorkflowExecutableStore();
        var executable = Executable(now, JsonSerializer.SerializeToElement(new { secret = "must-not-leak" }));
        await executableStore.SaveAsync(executable);
        var inspector = new WorkflowExecutableInspector(
            executableStore,
            new InMemoryWorkflowExecutableSourceReferenceStore(),
            new InMemoryWorkflowExecutionStateStore(),
            new FixedTimeProvider(now));

        var detail = await inspector.GetAsync("artifact-1");
        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("descriptorPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must-not-leak", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_api_registers_the_inspector()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WorkflowExecutableInspector));
    }

    private static WorkflowExecutable Executable(DateTimeOffset now, JsonElement? descriptor = null) =>
        new(
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            new ExecutableNode(
                "root", "root", "Test.Root", "1.0.0", "Test",
                descriptor ?? JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, RuntimeInputBinding>(),
                new Dictionary<string, RuntimeOutputCapture>(),
                new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            now,
            new Dictionary<string, string>());

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Type ElementType(Type collectionType) => collectionType.GetInterfaces().Append(collectionType)
        .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)).GenericTypeArguments[0];

    private static void AssertProperties(Type type, params string[] properties) =>
        Assert.All(properties, property => Assert.NotNull(type.GetProperty(property, BindingFlags.Public | BindingFlags.Instance)));
}
