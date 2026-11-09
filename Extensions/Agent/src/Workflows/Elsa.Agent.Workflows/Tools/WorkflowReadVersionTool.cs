using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Services;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Workflows.Tools;

/// <summary>
/// Reads one exact persisted workflow version, read-only (spec 1193 FR-026): the version an admitted <c>workflows.version</c>
/// context names, read again through the owning design services, with its activities by scope path (input names only) and
/// connections; and optionally the tool's own structural comparison with another exact version of the same definition.
/// Nothing is ever changed: a version is immutable.
/// </summary>
internal sealed class WorkflowReadVersionTool(
    IAgentToolAuthorizer authorizer,
    IServiceProvider services,
    IEnumerable<IWorkflowDefinitionAuthorizer>? owners = null) : IAgentTool, IAgentToolAuthorization
{
    // Resolved only when the tool is used, so listing the installed tools never builds the design read path.
    private IEnumerable<IWorkflowVersionReader> readers => services.GetServices<IWorkflowVersionReader>();
    private IEnumerable<IActivityStructureService> structures => services.GetServices<IActivityStructureService>();

    public const string ToolName = "workflow_read_version";
    internal const string ResultType = WorkflowVersionContext.VersionKind;
    private static readonly JsonSerializerOptions JsonOptions = WorkflowVersionContext.JsonOptions;
    /// <summary>The result's bound on its JSON text, well under the host's 32 KiB per-attachment budget (#3710).</summary>
    internal const int ResultBudget = 24 * 1024;

    internal static readonly AgentToolDescriptor Contract = new(ToolName, "Read workflow version",
        "Returns one exact, immutable workflow version named by an admitted workflows.version context (its versionId, or the only one): its activities by scope " +
        "path with their input names (never values) and its connections. With compareToVersionId, another version of the same definition, it also returns the " +
        "tool's own structural comparison of the two exact versions: activities added, removed and changed, and connections added, removed and changed " +
        "(connectionsChanged: the same connection rewired or with other metadata). Read-only.",
        AgentToolMutability.ReadOnly, AgentRisk.ReadOnly,
        """{"type":"object","properties":{"versionId":{"type":"string","minLength":1,"maxLength":128},"compareToVersionId":{"type":"string","minLength":1,"maxLength":128}},"additionalProperties":false}""",
        ["workflows/design:read"], ["/workflows"])
    {
        ModuleId = WorkflowVersionContext.ModuleId, ContractVersion = "1", ExecutionLocation = AgentExecutionLocation.Server,
        ResultType = ResultType, ResultSchemaVersion = "1", DisclosureSourceId = WorkflowVersionContext.ContextSourceId
    };

    public AgentToolDescriptor Descriptor => Contract;

    public async ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (WorkflowVersionContext.Reader(readers, out var reason) is null)
            return new(false, [new(WorkflowVersionContext.ReaderUnavailableCode, reason!) { OwnerUnavailable = true }]);
        // Availability: the reader is installed. A concrete version is decided by its definition's owner, the definition
        // looked up without reading the version (the host already decided this tool's design read permission).
        if (target is null) return new(true, []);
        if (!WorkflowVersionContext.IsTarget(target))
            return new(false, [new("agent.workflow.resource_required", "One exact workflow version is this tool's only target.")]);
        var (owner, _) = await WorkflowVersionContext.AuthorizeVersionAsync(null, owners, WorkflowVersionContext.Reader(readers, out _), actorId, tenantId,
            target.ResourceId!, cancellationToken);
        return owner switch
        {
            WorkflowVersionContext.Outcome.Read => new(true, []),
            WorkflowVersionContext.Outcome.OwnerUnavailable => new(false,
                [new(WorkflowDefinitionAuthorizationException.UnavailableCode, WorkflowResourceAuthority.UnavailableMessage) { OwnerUnavailable = true }]),
            WorkflowVersionContext.Outcome.AuthorityUnavailable => new(false,
                [new(WorkflowVersionContext.AuthorityUnavailableCode, WorkflowVersionContext.AuthorityUnavailableMessage) { OwnerUnavailable = true }]),
            _ => new(false, [new(WorkflowVersionContext.NotFoundCode, WorkflowVersionContext.NotFoundMessage)])
        };
    }

    public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
    {
        JsonElement arguments;
        try { arguments = JsonSerializer.SerializeToElement(invocation.Arguments, JsonOptions); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or JsonException)
        { return Failure("agent.workflow.version_arguments_invalid", "The arguments must be JSON.", 400); }
        string? versionId = null, compareTo = null;
        foreach (var property in arguments.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            if (value is not { Length: > 0 and <= WorkflowVersionContext.IdentityLimit }
                || property.Name is not ("versionId" or "compareToVersionId"))
                return Failure("agent.workflow.version_arguments_invalid", "Only versionId and compareToVersionId, each a version identity, are accepted.", 400);
            if (property.Name == "versionId") versionId = value; else compareTo = value;
        }
        // The version is bound to an admitted version context: the only one, or the one that names it.
        var contexts = invocation.Context.Where(item => item.ContentType == WorkflowVersionContext.VersionKind && item.Origin == AgentContextOrigin.AuthoritativeRead)
            .Select(item => WorkflowVersionContext.TryRead(item, out var reference, out _) ? reference : null).OfType<WorkflowVersionContext.Reference>().ToArray();
        var bound = versionId is null ? contexts.Length == 1 ? contexts[0] : null : contexts.FirstOrDefault(item => item.VersionId == versionId);
        if (bound is null)
            return Failure("agent.workflow.version_context_required",
                "One admitted workflow version context is required, and versionId must name one of them; nothing was read.", 409);
        if (compareTo == bound.VersionId)
            return Failure("agent.workflow.version_arguments_invalid", "compareToVersionId must name another version of the same workflow.", 400);
        var reader = WorkflowVersionContext.Reader(readers, out var reason);
        var read = await WorkflowVersionContext.ReadAsync(authorizer, owners, reader, invocation.ActorId, invocation.TenantId, bound.DefinitionId, bound.VersionId, true, cancellationToken);
        if (Refused(read.Outcome, reason) is { } refused) return refused;
        // The compared version belongs to the same, already authorized definition: it is read once, without asking again.
        var compared = compareTo is null ? null : await WorkflowVersionContext.ReadOwnedAsync(reader!, bound.DefinitionId, compareTo, false, cancellationToken);
        if (compared is not null && Refused(compared.Outcome, reason) is { } comparedRefused) return comparedRefused;
        if (WorkflowVersionContext.Structure(structures) is not { } structure)
            return Failure(WorkflowVersionContext.StructureUnavailableCode, "The activity structure service is unavailable, so the workflow version cannot be read.", 503);

        var version = read.Version!;
        var mine = WorkflowVersionStructure.Flatten(version.State.RootActivity, structure);
        var theirs = compared is null ? null : WorkflowVersionStructure.Flatten(compared.Version!.State.RootActivity, structure);
        var created = read.Versions!.ToDictionary(item => item.Id, item => item.CreatedAt, StringComparer.Ordinal);
        // The largest list bound whose result fits the budget; each list says when it was cut.
        JsonElement? fitted = null;
        foreach (var limit in new[] { WorkflowVersionStructure.ListLimit, 50, 25, 10, 1, 0 })
        {
            var data = JsonSerializer.SerializeToElement(new
            {
                readOnly = true, definitionId = version.DefinitionId, definitionName = WorkflowVersionContext.Bound(version.DefinitionName, 256),
                version = Section(version.VersionId, version.Version, created, WorkflowVersionStructure.Describe(mine, limit)),
                comparedTo = compared is null ? null : new
                {
                    versionId = compared.Version!.VersionId, version = compared.Version.Version,
                    createdAt = created.TryGetValue(compared.Version.VersionId, out var at) ? at : (DateTimeOffset?)null, activityCount = theirs!.Activities.Count
                },
                comparison = theirs is null ? null : WorkflowVersionStructure.Compare(mine, theirs, limit)
            }, JsonOptions);
            if (data.GetRawText().Length <= ResultBudget) { fitted = data; break; }
        }
        // Nothing unbounded is returned: if even the smallest reading does not fit, the read is refused.
        if (fitted is not { } bounded)
            return Failure("agent.workflow.version_too_large", "The workflow version's read does not fit the context budget even at its smallest bound; nothing was read.", 413);
        return AgentResult<AgentToolPreparation>.Success(new(WorkflowVersionContext.Target(version.VersionId), bounded, AgentRisk.ReadOnly,
            new(ResultType, "1", bounded), RequiresApproval: false));
    }

    public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default) =>
        Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true,
            "Read the exact workflow version through the workflow design service; nothing was changed.", new(ResultType, "1", invocation.Arguments))));

    private static object Section(string versionId, string version, IReadOnlyDictionary<string, DateTimeOffset> created, object described)
    {
        var body = JsonSerializer.SerializeToNode(described, JsonOptions)!.AsObject();
        body.Insert(0, "versionId", versionId);
        body.Insert(1, "version", version);
        body.Insert(2, "createdAt", created.TryGetValue(versionId, out var at) ? JsonSerializer.SerializeToNode(at, JsonOptions) : null);
        return body;
    }

    private static AgentResult<AgentToolPreparation>? Refused(WorkflowVersionContext.Outcome outcome, string? reason) => outcome switch
    {
        WorkflowVersionContext.Outcome.Read => null,
        WorkflowVersionContext.Outcome.Denied => Failure("agent.workflow.permission_denied", WorkflowVersionContext.DeniedMessage, 403),
        WorkflowVersionContext.Outcome.NotFound => Failure(WorkflowVersionContext.NotFoundCode, WorkflowVersionContext.NotFoundMessage, 404),
        WorkflowVersionContext.Outcome.OwnerUnavailable => Failure(WorkflowDefinitionAuthorizationException.UnavailableCode, WorkflowResourceAuthority.UnavailableMessage, 503),
        WorkflowVersionContext.Outcome.AuthorityUnavailable => Failure(WorkflowVersionContext.AuthorityUnavailableCode, WorkflowVersionContext.AuthorityUnavailableMessage, 503),
        _ => Failure(WorkflowVersionContext.ReaderUnavailableCode, reason ?? "Workflow versions are unavailable.", 503)
    };

    private static AgentResult<AgentToolPreparation> Failure(string code, string message, int status) =>
        AgentResult<AgentToolPreparation>.Failure(code, message, status);
}
