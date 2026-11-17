using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Workflows.Runtime.Tools;

/// <summary>
/// Reads one exact workflow Executable, read-only (spec 1193 FR-028): the Executable an admitted <c>workflows.executable</c>
/// context names, read again through Workflows Runtime Core at its bound Artifact Hash, with its identity, Artifact Hash,
/// compiled graph and the Source References the Runtime read discloses. A changed hash is stale and refused. Nothing is ever changed.
/// </summary>
internal sealed class WorkflowReadExecutableTool(
    IWeaverToolAuthorizer authorizer,
    IServiceProvider services) : IWeaverTool, IWeaverToolAuthorization
{
    // Resolved only when the tool is used, so listing the installed tools never builds the Runtime read path.
    private IEnumerable<IWorkflowExecutableInspection> inspections => services.GetServices<IWorkflowExecutableInspection>();

    public const string ToolName = "workflow_read_executable";
    internal const string ResultType = WorkflowExecutableContext.ExecutableKind;
    /// <summary>At most this many graph nodes are returned, depth first; a larger graph is reported as truncated.</summary>
    internal const int NodeLimit = 300;
    /// <summary>The result's bound on its JSON text, well under the host's 32 KiB per-attachment budget (#3710).</summary>
    internal const int ResultBudget = 24 * 1024;
    private const int TextLimit = 256;

    /// <summary>
    /// The bounds tried in order, each smaller than the last: graph nodes (depth first), each node's inputs and connections,
    /// and the Source References and dependencies. The first whose result fits the budget is used.
    /// </summary>
    private static readonly (int Nodes, int Inputs, int Connections, int References)[] Bounds =
        [(NodeLimit, 32, 64, 32), (150, 16, 32, 32), (80, 8, 16, 16), (40, 8, 16, 8), (20, 4, 8, 8), (10, 4, 8, 4), (1, 0, 0, 1)];
    private static readonly JsonSerializerOptions JsonOptions = WorkflowExecutableContext.JsonOptions;

    internal static readonly WeaverToolDescriptor Contract = new(ToolName, "Read workflow Executable",
        "Returns one exact, immutable workflow Executable named by an admitted workflows.executable context (its artifactId, or the only one), read at its bound " +
        "Artifact Hash: its identity, Artifact Hash, compiled graph (activity types and versions, input binding names and sources but never values, slots and " +
        "connections) and the Source References that point at it. Read-only.",
        WeaverToolMutability.ReadOnly, WeaverRisk.ReadOnly,
        """{"type":"object","properties":{"artifactId":{"type":"string","minLength":1,"maxLength":256}},"additionalProperties":false}""",
        ["workflows/runtime:read"], ["/workflows"])
    {
        ModuleId = WorkflowExecutableContext.ModuleId, ContractVersion = "1", ExecutionLocation = WeaverExecutionLocation.Server,
        ResultType = ResultType, ResultSchemaVersion = "1", DisclosureSourceId = WorkflowExecutableContext.ContextSourceId
    };

    public WeaverToolDescriptor Descriptor => Contract;

    public async ValueTask<WeaverPolicyDecision> AuthorizeAsync(string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WorkflowExecutableContext.Inspection(inspections) is not { } inspection)
            return new(false, [new(WorkflowExecutableContext.UnavailableCode, WorkflowExecutableContext.UnavailableMessage) { OwnerUnavailable = true }]);
        if (target is null) return new(true, []);
        if (!WorkflowExecutableContext.IsTarget(target))
            return new(false, [new("weaver.workflow.resource_required", "One exact workflow Executable is this tool's only target.")]);
        // The host decided the runtime read permission; the owner decides that the Executable still exists at the bound hash.
        var executable = await inspection.GetAsync(target.ResourceId!, cancellationToken);
        return executable is null ? new(false, [new(WorkflowExecutableContext.NotFoundCode, WorkflowExecutableContext.NotFoundMessage)])
            : executable.ArtifactHash != target.Revision ? new(false, [new(WorkflowExecutableContext.StaleCode, WorkflowExecutableContext.StaleMessage)])
            : new(true, []);
    }

    public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException)
        { return Failure("weaver.workflow.executable_arguments_invalid", "The arguments must be JSON.", 400); }
        string? artifactId = null;
        foreach (var property in arguments.EnumerateObject())
        {
            if (property.Name != "artifactId" || property.Value.ValueKind != JsonValueKind.String
                || property.Value.GetString() is not { Length: > 0 and <= WorkflowExecutableContext.IdentityLimit } value)
                return Failure("weaver.workflow.executable_arguments_invalid", "Only artifactId, an Executable identity, is accepted.", 400);
            artifactId = value;
        }
        var contexts = invocation.Context.Where(item => item.ContentType == WorkflowExecutableContext.ExecutableKind && item.Origin == WeaverContextOrigin.AuthoritativeRead)
            .Select(item => WorkflowExecutableContext.TryRead(item, out var reference, out _) ? reference : null).OfType<WorkflowExecutableContext.Reference>().ToArray();
        var bound = artifactId is null ? contexts.Length == 1 ? contexts[0] : null : contexts.FirstOrDefault(item => item.ArtifactId == artifactId);
        if (bound is null)
            return Failure("weaver.workflow.executable_context_required",
                "One admitted workflow Executable context is required, and artifactId must name one of them; nothing was read.", 409);
        var read = await WorkflowExecutableContext.ReadAsync(authorizer, WorkflowExecutableContext.Inspection(inspections), invocation.ActorId, invocation.TenantId,
            bound.ArtifactId, bound.ArtifactHash, cancellationToken);
        switch (read.Outcome)
        {
            case WorkflowExecutableContext.Outcome.Denied: return Failure("weaver.workflow.permission_denied", WorkflowExecutableContext.DeniedMessage, 403);
            case WorkflowExecutableContext.Outcome.NotFound: return Failure(WorkflowExecutableContext.NotFoundCode, WorkflowExecutableContext.NotFoundMessage, 404);
            case WorkflowExecutableContext.Outcome.Stale: return Failure(WorkflowExecutableContext.StaleCode, WorkflowExecutableContext.StaleMessage, 409);
            case WorkflowExecutableContext.Outcome.Unavailable: return Failure(WorkflowExecutableContext.UnavailableCode, WorkflowExecutableContext.UnavailableMessage, 503);
        }
        var executable = read.Executable!;
        // The largest bounded reading whose result fits the budget; a smaller one says what it left out. If even the smallest
        // does not fit, nothing unbounded is returned.
        JsonElement? data = null;
        foreach (var limits in Bounds)
        {
            var candidate = Result(executable, limits);
            if (candidate.GetRawText().Length <= ResultBudget) { data = candidate; break; }
        }
        if (data is not { } bounded)
            return Failure("weaver.workflow.executable_too_large", "The workflow Executable's read does not fit the context budget even at its smallest bound; nothing was read.", 413);
        return WeaverResult<WeaverToolPreparation>.Success(new(WorkflowExecutableContext.Target(executable.ArtifactId, executable.ArtifactHash), bounded, WeaverRisk.ReadOnly,
            new(ResultType, "1", bounded), RequiresApproval: false));
    }

    public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default) =>
        Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "Read the exact workflow Executable through the workflow runtime at its bound Artifact Hash; nothing was changed.", new(ResultType, "1", invocation.Arguments))));

    private static JsonElement Result(WorkflowExecutableInspectionDetails executable, (int Nodes, int Inputs, int Connections, int References) bound)
    {
        var walk = new Walk(bound.Nodes, bound.Inputs, bound.Connections);
        walk.Include(executable.RootActivity);
        var graph = walk.Node(executable.RootActivity);
        return JsonSerializer.SerializeToElement(new
        {
            readOnly = true, artifactId = executable.ArtifactId, artifactHash = executable.ArtifactHash, createdAt = executable.CreatedAt,
            rootActivityType = walk.Text(executable.RootActivityType), rootActivityVersion = walk.Text(executable.RootActivityVersion), nodeCount = executable.NodeCount,
            graph, nodesIncluded = walk.Included.Count,
            graphTruncated = Count(executable.RootActivity) > walk.Included.Count || walk.ListsCut || walk.ConnectionsOmitted > 0,
            connectionsOmitted = walk.ConnectionsOmitted,
            // Text values (types, versions, names, ports) longer than 256 characters are cut; identities never are.
            truncatedFields = walk.TruncatedFields,
            sourceReferences = WorkflowExecutableContext.References(executable).Take(bound.References).ToArray(),
            sourceReferencesTruncated = executable.SourceReferences.Count > Math.Min(bound.References, WorkflowExecutableContext.ReferenceLimit),
            dependencies = executable.Dependencies.Take(bound.References)
                .Select(dependency => new { artifactId = dependency.ArtifactId, artifactHash = dependency.ArtifactHash }).ToArray(),
            dependenciesTruncated = executable.Dependencies.Count > bound.References
        }, JsonOptions);
    }

    /// <summary>
    /// One bounded, depth-first reading of the graph. The included nodes are chosen first, so a connection is kept only when
    /// both its ends are included; a node beyond the bound is left out with its subtree, and each node's inputs and
    /// connections are bounded too. Every cut is reported, never silent.
    /// </summary>
    private sealed class Walk(int nodes, int inputs, int connections)
    {
        public HashSet<string> Included { get; } = new(StringComparer.Ordinal);
        public bool ListsCut { get; private set; }
        public int TruncatedFields { get; private set; }

        /// <summary>A text value bounded at 256 characters; each cut is counted and reported.</summary>
        public string Text(string value)
        {
            if (value.Length <= TextLimit) return value;
            TruncatedFields++;
            return value[..TextLimit];
        }
        public int ConnectionsOmitted { get; private set; }

        public void Include(WorkflowExecutableInspectionNode node)
        {
            Included.Add(node.ExecutableNodeId);
            foreach (var slot in node.Slots)
                foreach (var child in slot.Nodes)
                {
                    if (Included.Count >= nodes) return;
                    Include(child);
                }
        }

        public object Node(WorkflowExecutableInspectionNode node)
        {
            var slots = node.Slots.Select(slot => new
            {
                name = Text(slot.Name),
                nodes = slot.Nodes.Where(child => Included.Contains(child.ExecutableNodeId)).Select(Node).ToArray()
            }).ToArray();
            var kept = node.Connections.Where(item => Included.Contains(item.Source) && Included.Contains(item.Target)).ToArray();
            ConnectionsOmitted += node.Connections.Count - Math.Min(kept.Length, connections);
            if (node.Inputs.Count > inputs || kept.Length > connections) ListsCut = true;
            return new
            {
                executableNodeId = node.ExecutableNodeId, authoredActivityId = node.AuthoredActivityId, activityType = Text(node.ActivityType),
                activityTypeVersion = Text(node.ActivityTypeVersion), structureKind = node.StructureKind is null ? null : Text(node.StructureKind),
                inputs = node.Inputs.Take(inputs).Select(input => new { name = Text(input.Name), source = input.Source, sensitive = input.IsSensitive }).ToArray(),
                inputsTruncated = node.Inputs.Count > inputs,
                slots,
                connections = kept.Take(connections).Select(connection => new
                {
                    source = connection.Source, sourcePort = connection.SourcePort is null ? null : Text(connection.SourcePort), target = connection.Target,
                    targetPort = connection.TargetPort is null ? null : Text(connection.TargetPort)
                }).ToArray(),
                connectionsTruncated = node.Connections.Count > kept.Take(connections).Count()
            };
        }
    }


    private static int Count(WorkflowExecutableInspectionNode node) => 1 + node.Slots.Sum(slot => slot.Nodes.Sum(Count));

    private static WeaverResult<WeaverToolPreparation> Failure(string code, string message, int status) =>
        WeaverResult<WeaverToolPreparation>.Failure(code, message, status);
}
