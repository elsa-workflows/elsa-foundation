using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Workflows.Runtime.Tools;

/// <summary>
/// Reads one exact workflow Executable, read-only (spec 1193 FR-028): the Executable an admitted <c>workflows.executable</c>
/// context names, read again through Workflows Runtime Core at its bound Artifact Hash, with its identity, Artifact Hash,
/// compiled graph and the Source References the Runtime read discloses. A changed hash is stale and refused. Nothing is ever changed.
/// </summary>
internal sealed class WorkflowReadExecutableTool(
    IAgentToolAuthorizer authorizer,
    IServiceProvider services) : IAgentTool, IAgentToolAuthorization
{
    // Resolved only when the tool is used, so listing the installed tools never builds the Runtime read path.
    private IEnumerable<IWorkflowExecutableInspection> inspections => services.GetServices<IWorkflowExecutableInspection>();

    public const string ToolName = "workflow_read_executable";
    internal const string ResultType = WorkflowExecutableContext.ExecutableKind;
    /// <summary>At most this many graph nodes are returned, depth first; a larger graph is reported as truncated.</summary>
    internal const int NodeLimit = 300;
    /// <summary>The result's bound on its JSON text, under the host's 32 KiB per-attachment budget (#3710).</summary>
    internal const int ResultBudget = 28 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = WorkflowExecutableContext.JsonOptions;

    internal static readonly AgentToolDescriptor Contract = new(ToolName, "Read workflow Executable",
        "Returns one exact, immutable workflow Executable named by an admitted workflows.executable context (its artifactId, or the only one), read at its bound " +
        "Artifact Hash: its identity, Artifact Hash, compiled graph (activity types and versions, input binding names and sources but never values, slots and " +
        "connections) and the Source References that point at it. Read-only.",
        AgentToolMutability.ReadOnly, AgentRisk.ReadOnly,
        """{"type":"object","properties":{"artifactId":{"type":"string","minLength":1,"maxLength":256}},"additionalProperties":false}""",
        ["workflows/runtime:read"], ["/workflows"])
    {
        ModuleId = WorkflowExecutableContext.ModuleId, ContractVersion = "1", ExecutionLocation = AgentExecutionLocation.Server,
        ResultType = ResultType, ResultSchemaVersion = "1", DisclosureSourceId = WorkflowExecutableContext.ContextSourceId
    };

    public AgentToolDescriptor Descriptor => Contract;

    public async ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WorkflowExecutableContext.Inspection(inspections) is not { } inspection)
            return new(false, [new(WorkflowExecutableContext.UnavailableCode, WorkflowExecutableContext.UnavailableMessage) { OwnerUnavailable = true }]);
        if (target is null) return new(true, []);
        if (!WorkflowExecutableContext.IsTarget(target))
            return new(false, [new("agent.workflow.resource_required", "One exact workflow Executable is this tool's only target.")]);
        // The host decided the runtime read permission; the owner decides that the Executable still exists at the bound hash.
        var executable = await inspection.GetAsync(target.ResourceId!, cancellationToken);
        return executable is null ? new(false, [new(WorkflowExecutableContext.NotFoundCode, WorkflowExecutableContext.NotFoundMessage)])
            : executable.ArtifactHash != target.Revision ? new(false, [new(WorkflowExecutableContext.StaleCode, WorkflowExecutableContext.StaleMessage)])
            : new(true, []);
    }

    public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException)
        { return Failure("agent.workflow.executable_arguments_invalid", "The arguments must be JSON.", 400); }
        string? artifactId = null;
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name != "artifactId" || property.Value.ValueKind != JsonValueKind.String
                || property.Value.GetString() is not { Length: > 0 and <= WorkflowExecutableContext.IdentityLimit } value)
                return Failure("agent.workflow.executable_arguments_invalid", "Only artifactId, an Executable identity, is accepted.", 400);
            artifactId = value;
        }
        var contexts = invocation.Context.Where(item => item.ContentType == WorkflowExecutableContext.ExecutableKind && item.Origin == AgentContextOrigin.AuthoritativeRead)
            .Select(item => WorkflowExecutableContext.TryRead(item, out var reference, out _) ? reference : null).OfType<WorkflowExecutableContext.Reference>().ToArray();
        var bound = artifactId is null ? contexts.Length == 1 ? contexts[0] : null : contexts.FirstOrDefault(item => item.ArtifactId == artifactId);
        if (bound is null)
            return Failure("agent.workflow.executable_context_required",
                "One admitted workflow Executable context is required, and artifactId must name one of them; nothing was read.", 409);
        var read = await WorkflowExecutableContext.ReadAsync(authorizer, WorkflowExecutableContext.Inspection(inspections), invocation.ActorId, invocation.TenantId,
            bound.ArtifactId, bound.ArtifactHash, cancellationToken);
        switch (read.Outcome)
        {
            case WorkflowExecutableContext.Outcome.Denied: return Failure("agent.workflow.permission_denied", WorkflowExecutableContext.DeniedMessage, 403);
            case WorkflowExecutableContext.Outcome.NotFound: return Failure(WorkflowExecutableContext.NotFoundCode, WorkflowExecutableContext.NotFoundMessage, 404);
            case WorkflowExecutableContext.Outcome.Stale: return Failure(WorkflowExecutableContext.StaleCode, WorkflowExecutableContext.StaleMessage, 409);
            case WorkflowExecutableContext.Outcome.Unavailable: return Failure(WorkflowExecutableContext.UnavailableCode, WorkflowExecutableContext.UnavailableMessage, 503);
        }
        var executable = read.Executable!;
        // The largest depth-first prefix of the graph whose result fits the budget; a smaller one says it is truncated.
        JsonElement data = default;
        foreach (var limit in new[] { NodeLimit, 150, 80, 40, 20, 10, 1 })
        {
            data = Result(executable, limit);
            if (data.GetRawText().Length <= ResultBudget) break;
        }
        return AgentResult<AgentToolPreparation>.Success(new(WorkflowExecutableContext.Target(executable.ArtifactId, executable.ArtifactHash), data, AgentRisk.ReadOnly,
            new(ResultType, "1", data), RequiresApproval: false));
    }

    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default) =>
        Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "Read the exact workflow Executable through the workflow runtime at its bound Artifact Hash; nothing was changed.", new(ResultType, "1", invocation.Arguments))));

    private static JsonElement Result(WorkflowExecutableInspectionDetails executable, int limit)
    {
        var included = 0;
        var graph = Node(executable.RootActivity, limit, ref included);
        return JsonSerializer.SerializeToElement(new
        {
            readOnly = true, artifactId = executable.ArtifactId, artifactHash = executable.ArtifactHash, createdAt = executable.CreatedAt,
            rootActivityType = executable.RootActivityType, rootActivityVersion = executable.RootActivityVersion, nodeCount = executable.NodeCount,
            graph, nodesIncluded = included, graphTruncated = Count(executable.RootActivity) > included,
            sourceReferences = WorkflowExecutableContext.References(executable),
            sourceReferencesTruncated = executable.SourceReferences.Count > WorkflowExecutableContext.ReferenceLimit,
            dependencies = executable.Dependencies.Take(WorkflowExecutableContext.ReferenceLimit)
                .Select(dependency => new { artifactId = dependency.ArtifactId, artifactHash = dependency.ArtifactHash }).ToArray()
        }, JsonOptions);
    }

    // Depth first, bounded: a node beyond the bound is left out with its subtree, and the result says the graph was truncated.
    private static object Node(WorkflowExecutableInspectionNode node, int limit, ref int included)
    {
        included++;
        var slots = new List<object>();
        foreach (var slot in node.Slots)
        {
            var nodes = new List<object>();
            foreach (var child in slot.Nodes)
            {
                if (included >= limit) break;
                nodes.Add(Node(child, limit, ref included));
            }
            slots.Add(new { name = slot.Name, nodes });
        }
        return new
        {
            executableNodeId = node.ExecutableNodeId, authoredActivityId = node.AuthoredActivityId, activityType = node.ActivityType,
            activityTypeVersion = node.ActivityTypeVersion, structureKind = node.StructureKind,
            inputs = node.Inputs.Select(input => new { name = input.Name, source = input.Source, sensitive = input.IsSensitive }).ToArray(),
            slots,
            connections = node.Connections.Select(connection => new
            {
                source = connection.Source, sourcePort = connection.SourcePort, target = connection.Target, targetPort = connection.TargetPort
            }).ToArray()
        };
    }

    private static int Count(WorkflowExecutableInspectionNode node) => 1 + node.Slots.Sum(slot => slot.Nodes.Sum(Count));

    private static AgentResult<AgentToolPreparation> Failure(string code, string message, int status) =>
        AgentResult<AgentToolPreparation>.Failure(code, message, status);
}
