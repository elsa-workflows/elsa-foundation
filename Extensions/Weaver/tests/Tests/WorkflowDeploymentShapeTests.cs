using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows;
using Elsa.Weaver.Workflows.Runtime;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Weaver.Tests;

// Spec 1193 SC-009 (FR-027): the Design-only and combined deployment shapes each start, serve their supported journeys and
// report the unsupported ones as explicitly unavailable. The Runtime-only shape is proven AppDomain-wide in
// Elsa.Weaver.Workflows.Runtime.Tests, which composes no Design assembly.
public sealed class WorkflowDeploymentShapeTests
{
    private const string Module = "Elsa.Studio.Workflows";

    [Fact]
    public async Task A_design_only_host_serves_versions_and_reports_the_Executable_and_Run_journeys_as_explicitly_unavailable()
    {
        using var root = Compose(runtime: false);
        using var scope = root.CreateScope();
        var session = await SessionAsync(scope);
        var collector = scope.ServiceProvider.GetRequiredService<IAgentContextCollector>();

        var version = await collector.CollectAsync(AgentPolicy.Default, Request(session, WorkflowVersionAgentTests.Reference("definition-1", "v2")));
        Assert.True(version.Succeeded, version.Error?.Message);
        Assert.Contains(version.Value!.Attachments, item => item.ContentType == "workflows.version");

        var executable = await collector.CollectAsync(AgentPolicy.Default, Request(session, Executable()));
        Assert.False(executable.Succeeded);
        var refusal = Assert.Single(executable.Value!.Diagnostics, item => item.Required);
        Assert.Equal(("weaver.context.kind_unsupported", "No installed module admits the supplied context kind and owner."), (refusal.Code, refusal.Message));
        Assert.DoesNotContain(executable.Value.Attachments, item => item.ContentType == "workflows.executable");

        // Spec 1193 slice G (story 9 scenario 6): a Run reference is explicitly unavailable in the same way.
        var run = await collector.CollectAsync(AgentPolicy.Default, Request(session, Run()));
        Assert.False(run.Succeeded);
        var runRefusal = Assert.Single(run.Value!.Diagnostics, item => item.Required);
        Assert.Equal(("weaver.context.kind_unsupported", "No installed module admits the supplied context kind and owner."), (runRefusal.Code, runRefusal.Message));
        Assert.DoesNotContain(run.Value.Attachments, item => item.ContentType == "workflows.run");

        var tools = await Tools(scope);
        Assert.Contains("workflow_read_version", tools);
        Assert.DoesNotContain("workflow_read_executable", tools);
        Assert.DoesNotContain("workflow_read_run", tools);
        Assert.DoesNotContain(await Capabilities(scope), id => id is "workflow.executable-explain" or "workflow.run-explain");
    }

    [Fact]
    public async Task A_combined_host_serves_version_Executable_and_Run_journeys_side_by_side()
    {
        using var root = Compose(runtime: true);
        using var scope = root.CreateScope();
        var session = await SessionAsync(scope);

        var collected = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            Request(session, WorkflowVersionAgentTests.Reference("definition-1", "v2"), Executable(), Run()));

        Assert.True(collected.Succeeded, collected.Error?.Message);
        Assert.Empty(collected.Value!.Diagnostics);
        var version = Assert.Single(collected.Value.Attachments, item => item.ContentType == "workflows.version");
        var executable = Assert.Single(collected.Value.Attachments, item => item.ContentType == "workflows.executable");
        Assert.Equal(("workflows.version-context", "workflows.executable-context"), (version.ContributorId, executable.ContributorId));
        Assert.Equal(new AgentResourceTarget(Module, "workflow-executable", "artifact-1", "sha256:artifact"), executable.Target);
        var run = Assert.Single(collected.Value.Attachments, item => item.ContentType == "workflows.run");
        Assert.Equal(("workflows.run-context", new AgentResourceTarget(Module, "workflow-run", "run-1", "run-1")), (run.ContributorId, run.Target));
        var tools = await Tools(scope);
        Assert.Contains("workflow_read_version", tools);
        Assert.Contains("workflow_read_executable", tools);
        Assert.Contains("workflow_read_run", tools);
        Assert.Contains("workflow.run-explain", await Capabilities(scope));
        Assert.Contains("workflow.version-explain", await Capabilities(scope));
        Assert.Contains("workflow.executable-explain", await Capabilities(scope));
    }

    private static ServiceProvider Compose(bool runtime) => AgentTestServices.Create(services =>
    {
        new WeaverWorkflowsFeature().ConfigureServices(services);
        services.AddSingleton<IWorkflowDefinitionAuthorizer>(new WorkflowVersionAgentTests.Owner("definition-1"));
        services.AddSingleton<IWorkflowVersionReader>(new WorkflowVersionAgentTests.FixtureReader());
        services.AddScoped<IActivityStructureService>(_ => new DefaultActivityStructureService([new WorkflowVersionAgentTests.FlowHandler()]));
        if (!runtime) return;
        new WeaverWorkflowsRuntimeFeature().ConfigureServices(services);
        services.AddSingleton<IWorkflowExecutableInspection>(new Inspection());
        services.AddSingleton<IWorkflowRunInspection>(new RunInspection());
    });

    private static async Task<AgentSession> SessionAsync(IServiceScope scope) =>
        await scope.ServiceProvider.GetRequiredService<IAgentSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider", "explain", null,
            AgentPolicy.Default, new Dictionary<string, string>()));

    private static AgentContextRequest Request(AgentSession session, params AgentContextAttachment[] callers) =>
        new(session.Id, "unbound", new Dictionary<string, string>()) { CallerAttachments = callers };

    private static async Task<string[]> Tools(IServiceScope scope) =>
        (await scope.ServiceProvider.GetRequiredService<IAgentToolRegistry>().ListAsync()).Select(tool => tool.Descriptor.Name).ToArray();

    private static async Task<string[]> Capabilities(IServiceScope scope)
    {
        var all = new List<string>();
        foreach (var provider in scope.ServiceProvider.GetServices<IAgentCapabilityProvider>())
            all.AddRange((await provider.GetCapabilitiesAsync()).Select(item => item.Id));
        return [.. all];
    }

    private static AgentContextAttachment Executable() =>
        new("workflows.executable:artifact-1", "workflow-executable", "artifact-1", "Executable", "workflows.executable", AgentContextSensitivity.Internal, "screen",
            "Workflow Executable", JsonSerializer.SerializeToElement(new { artifactId = "artifact-1", artifactHash = "sha256:artifact", sourceReferenceId = (string?)null }),
            new Dictionary<string, string>())
        { ModuleId = Module, SchemaVersion = "1", Target = new(Module, "workflow-executable", "artifact-1", "sha256:artifact"), Origin = AgentContextOrigin.ClientSnapshot };

    private static AgentContextAttachment Run() =>
        new("workflows.run:run-1", "workflow-run", "run-1", "Run", "workflows.run", AgentContextSensitivity.Internal, "screen", "Workflow Run",
            JsonSerializer.SerializeToElement(new { workflowExecutionId = "run-1", activityExecutionId = (string?)null }), new Dictionary<string, string>())
        { ModuleId = Module, SchemaVersion = "1", Target = new(Module, "workflow-run", "run-1", "run-1"), Origin = AgentContextOrigin.ClientSnapshot };

    private sealed class RunInspection : IWorkflowRunInspection
    {
        public ValueTask<WorkflowRunInspectionDetails?> GetAsync(WorkflowRunInspectionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowRunInspectionDetails?>(request.WorkflowExecutionId != "run-1" ? null : new(
                new("run-1", "artifact-1", "sha256:artifact", "definition-1", "v2", "2.0.0", "Faulted", null, "Production", true, DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, 0, 0, 0, 0, null, null),
                [], new([], 0, null), null, true, true, "Durable", "activity-level", DateTimeOffset.UnixEpoch));
    }

    private sealed class Inspection : IWorkflowExecutableInspection
    {
        public ValueTask<WorkflowExecutableInspectionDetails?> GetAsync(string artifactId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowExecutableInspectionDetails?>(artifactId != "artifact-1" ? null : new("artifact-1", "sha256:artifact", DateTimeOffset.UnixEpoch,
                "Elsa.Flowchart", "1.0.0", 1, 1, new("root", "root", "Elsa.Flowchart", "1.0.0", null, [], [], []),
                [new("published-1", "Published", null, null, null, "definition-1", "v2", "2.0.0", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null, true)], []));
    }
}
