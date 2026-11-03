using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Events;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Workflows.Runtime.Tests;

/// <summary>A Runtime-only Weaver host: the Agent foundation, the Runtime-side workflow package and one fixture Executable inspection.</summary>
internal sealed class WorkflowRuntimeAgentTestHost : IDisposable
{
    internal const string ModuleId = "Elsa.Studio.Workflows";
    internal const string Hash = "sha256:artifact-1";
    private readonly ServiceProvider root;
    private readonly IServiceScope scope;

    public WorkflowRuntimeAgentTestHost(FixtureInspection? inspection = null, IAgentToolAuthorizer? authorizer = null, Action<IServiceCollection>? configure = null)
    {
        Inspection = inspection ?? new FixtureInspection(Executable());
        var services = new ServiceCollection();
        services.AddLogging();
        new EventsFeature().ConfigureServices(services);
        services.AddFoundationAgentAbstractions();
        new FoundationWorkflowsRuntimeAgentFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer>(_ => authorizer ?? new Authorizer()));
        services.AddSingleton<IWorkflowExecutableInspection>(Inspection);
        configure?.Invoke(services);
        root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        scope = root.CreateScope();
    }

    public FixtureInspection Inspection { get; }
    public IServiceProvider Services => scope.ServiceProvider;
    public IAgentContextProvider Source => Services.GetServices<IAgentContextProvider>().Single(item => item.Descriptor.Id == "workflows.executable-context");
    public IAgentTool Tool => Services.GetServices<IAgentTool>().Single(item => item.Descriptor.Name == "workflow_read_executable");

    public async Task<AgentSession> SessionAsync(string mode = "explain") =>
        await Services.GetRequiredService<IAgentSessionService>().CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider", mode, null,
            AgentPolicy.Default, new Dictionary<string, string>()));

    public async Task<AgentContextContribution> CollectAsync(AgentSession session, params AgentContextAttachment[] callers) =>
        await Source.CollectAsync(new(session.Id, "workflow-executable", new Dictionary<string, string>()) { CallerAttachments = callers });

    public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentSession session, IReadOnlyCollection<AgentContextAttachment> context,
        IReadOnlyDictionary<string, object?>? arguments = null) =>
        await Tool.PrepareAsync(new("call-1", "workflow_read_executable", session.Id, session.ActorId, arguments ?? new Dictionary<string, object?>(), context)
            { TenantId = session.TenantId });

    public void Dispose()
    {
        scope.Dispose();
        root.Dispose();
    }

    /// <summary>The Executable Inspector's reference to one exact Executable, bound to the hash it shows.</summary>
    public static AgentContextAttachment Reference(string artifactId = "artifact-1", string hash = Hash, object? content = null) =>
        new($"workflows.executable:{artifactId}", "workflow-executable", artifactId, "Executable", "workflows.executable", AgentContextSensitivity.Internal, "screen",
            "Workflow Executable", JsonSerializer.SerializeToElement(content ?? new { artifactId, artifactHash = hash, sourceReferenceId = (string?)null }),
            new Dictionary<string, string>())
        {
            ModuleId = ModuleId, SchemaVersion = "1", Target = new(ModuleId, "workflow-executable", artifactId, hash), Origin = AgentContextOrigin.ClientSnapshot
        };

    /// <summary>A root flowchart with two children connected Done -> In, one sensitive binding, and two Source References.</summary>
    public static WorkflowExecutableInspectionDetails Executable(string hash = Hash, int children = 2) =>
        new("artifact-1", hash, new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero), "Elsa.Flowchart", "1.0.0", children + 1, 2,
            new("root", "root", "Elsa.Flowchart", "1.0.0", "elsa.flowchart.structure", [new("secret-key", "Literal", true)],
                [new("activities", Enumerable.Range(1, children).Select(index => new WorkflowExecutableInspectionNode($"node-{index}", $"node-{index}", "Elsa.WriteLine", "1.0.0", null,
                    [new("text", "Literal", false)], [], [])).ToArray())],
                children > 1 ? [new("node-1", "Done", "node-2", "In")] : []),
            [
                new("published-1", "Published", "WorkflowDefinitionVersion", "version-1", "1.0.0", "definition-1", "version-1", "1.0.0",
                    new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 3, 11, 0, 0, TimeSpan.Zero), null, null, true),
                new("published-2", "Published", "WorkflowDefinitionVersion", "version-1", "1.0.0", "definition-1", "version-1", "1.0.0",
                    new DateTimeOffset(2026, 11, 3, 11, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 3, 11, 30, 0, TimeSpan.Zero), null, null, true)
            ],
            [new("dependency-1", "sha256:dependency")]);

    internal sealed class FixtureInspection(WorkflowExecutableInspectionDetails? executable) : IWorkflowExecutableInspection
    {
        public WorkflowExecutableInspectionDetails? Current { get; set; } = executable;
        public int Reads { get; private set; }

        public ValueTask<WorkflowExecutableInspectionDetails?> GetAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(Current?.ArtifactId == artifactId ? Current : null);
        }
    }

    /// <summary>The host's permission decisions: every permission except those listed as denied.</summary>
    internal sealed class Authorizer(params string[] denied) : IAgentToolAuthorizer
    {
        public Task<AgentPolicyDecision> AuthorizeAsync(AgentToolDescriptor tool, string actorId, string tenantId, AgentResourceTarget? target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(tool.RequiredPermissions.Any(denied.Contains)
                ? new AgentPolicyDecision(false, [new("agent.tool.permission_denied", "The current principal lacks a required permission for this resource.")])
                : new AgentPolicyDecision(true, []));
    }
}
