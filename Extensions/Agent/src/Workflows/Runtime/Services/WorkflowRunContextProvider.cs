using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Workflows.Runtime.Tools;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Workflows.Runtime.Services;

/// <summary>
/// The workflow Run context source (spec 1193 story 9, FR-027, FR-029): a run page's reference to one exact Run, and the
/// activity execution selected on it, becomes the server's own read of that Run through Workflows Runtime Core under the
/// host's runtime read permission, and it is read again for every turn. A selection the Run does not hold is refused; one it
/// holds but cannot show is admitted with its reason. Without a Run reference it contributes nothing. It never resolves a
/// value: values are read only by <c>workflow_read_run</c>.
/// </summary>
internal sealed class WorkflowRunContextProvider(
    IAgentSessionService sessions,
    IAgentToolAuthorizer authorizer,
    IServiceProvider services) : IAgentContextProvider
{
    // The Runtime owner is resolved only when a Run is referenced, so composing this source never builds the Runtime read
    // path a turn without a Run does not use.
    private IEnumerable<IWorkflowRunInspection> inspections => services.GetServices<IWorkflowRunInspection>();

    public AgentContextProviderDescriptor Descriptor { get; } = new(WorkflowRunContext.ContextSourceId, WorkflowRunContext.ModuleId,
        "workflow-run", [WorkflowRunContext.RunKind, WorkflowRunContext.GuidanceKind], ["1"], [WorkflowRunContext.ResourceType]);

    public async ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        var callers = request.CallerAttachments.Where(item => item.ContentType == WorkflowRunContext.RunKind).ToArray();
        if (callers.Length == 0) return new([], []);
        var session = await FindSessionAsync(request.SessionId, cancellationToken);
        if (session is null) return SessionDenied();
        var attachments = new List<AgentContextAttachment>();
        var diagnostics = new List<AgentContributionDiagnostic>();
        foreach (var caller in callers)
        {
            if (!WorkflowRunContext.TryRead(caller, out var reference, out var error))
            {
                diagnostics.Add(new(Descriptor.Id, "agent.workflow.run_context_invalid", error, true));
                continue;
            }
            if (request.Client is not null && reference.Client is not null && request.Client != reference.Client)
            {
                diagnostics.Add(new(Descriptor.Id, "agent.workflow.context_target_mismatch", "The Run context belongs to another page.", true));
                continue;
            }
            var read = await WorkflowRunContext.ReadAsync(authorizer, WorkflowRunContext.Inspection(inspections), session.ActorId, session.TenantId, reference,
                cancellationToken);
            if (Refusal(read.Outcome) is { } refusal) diagnostics.Add(refusal);
            else
            {
                var attachment = WorkflowRunContext.Attachment(read.Run!, reference.ActivityExecutionId, Admitted(caller.Sensitivity), reference.Client);
                var same = attachments.FirstOrDefault(item => item.Id == attachment.Id);
                if (same is null) attachments.Add(attachment);
                else if (same.Target != attachment.Target) diagnostics.Add(new(Descriptor.Id, "agent.workflow.run_context_invalid", "One Run is referenced with two selections.", true));
            }
        }
        if (attachments.Count > 0) attachments.Add(WorkflowRunContext.Guidance());
        return new(attachments, diagnostics);
    }

    public async ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
    {
        var session = await FindSessionAsync(request.SessionId, cancellationToken);
        if (session is null) return SessionDenied();
        var inspection = WorkflowRunContext.Inspection(inspections);
        var projection = new List<AgentContextAttachment>();
        foreach (var item in request.Context)
        {
            if (item.ModuleId != Descriptor.ModuleId || item.SchemaVersion != "1" || !Descriptor.ContextKinds.Contains(item.ContentType, StringComparer.Ordinal))
                return ContextDenied();
            if (item.ContentType == WorkflowRunContext.GuidanceKind)
            {
                if (item.Origin != AgentContextOrigin.InstalledGuidance) return ContextDenied();
                projection.Add(WorkflowRunContext.Guidance());
                continue;
            }
            // Only this module's own read is ever retained; it is read again, so a Run the actor may no longer read stops being disclosed.
            if (item.Origin != AgentContextOrigin.AuthoritativeRead || !WorkflowRunContext.TryRead(item, out var reference, out _)) return ContextDenied();
            var read = await WorkflowRunContext.ReadAsync(authorizer, inspection, session.ActorId, session.TenantId, reference, cancellationToken);
            if (Refusal(read.Outcome) is { } refusal) return new([], [refusal]);
            projection.Add(request.IsHistorical || request.IsReceiptContinuation ? item
                : WorkflowRunContext.Attachment(read.Run!, reference.ActivityExecutionId, item.Sensitivity, reference.Client));
        }
        foreach (var result in request.Results)
        {
            if (result.Access is not { ModuleId: WorkflowRunContext.ModuleId } access || result.ToolName != WorkflowReadRunTool.ToolName
                || !WorkflowRunContext.IsTarget(access.Target)) return ContextDenied();
            // The read is disclosed again only while the actor may still read the Run (and the selection) it describes.
            var target = access.Target!;
            var read = await WorkflowRunContext.ReadAsync(authorizer, inspection, session.ActorId, session.TenantId,
                new(target.ResourceId!, WorkflowRunContext.SelectedActivity(target), null), cancellationToken);
            if (Refusal(read.Outcome) is { } refusal) return new([], [refusal]);
            // Nor once it discloses more than the actor's current run access covers: incident details read under value
            // inspection, or values released under payload resolution, that the actor no longer holds.
            if (WorkflowReadRunTool.Exceeds(result.Payload, read.Run!))
                return new([], [new(Descriptor.Id, WorkflowRunContext.AccessChangedCode, WorkflowRunContext.AccessChangedMessage, true)]);
        }
        return new(projection, []);
    }

    private AgentContributionDiagnostic? Refusal(WorkflowRunContext.Outcome outcome) => outcome switch
    {
        WorkflowRunContext.Outcome.Read => null,
        WorkflowRunContext.Outcome.Denied => new(Descriptor.Id, "agent.workflow.permission_denied", WorkflowRunContext.DeniedMessage, true),
        WorkflowRunContext.Outcome.NotFound => new(Descriptor.Id, WorkflowRunContext.NotFoundCode, WorkflowRunContext.NotFoundMessage, true),
        WorkflowRunContext.Outcome.ActivityNotFound => new(Descriptor.Id, WorkflowRunContext.ActivityNotFoundCode, WorkflowRunContext.ActivityNotFoundMessage, true),
        _ => new(Descriptor.Id, WorkflowRunContext.UnavailableCode, WorkflowRunContext.UnavailableMessage, true) { OwnerUnavailable = true }
    };

    // A caller classification can restrict module-owned data; it cannot certify prior redaction.
    private static AgentContextSensitivity Admitted(AgentContextSensitivity caller) =>
        caller == AgentContextSensitivity.SecretRedacted ? AgentContextSensitivity.Secret :
        caller < AgentContextSensitivity.Internal ? AgentContextSensitivity.Internal : caller;

    private async Task<AgentSession?> FindSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        return session is { Status: AgentSessionStatus.Active } && !string.IsNullOrWhiteSpace(session.ActorId)
            && !string.IsNullOrWhiteSpace(session.TenantId) && (session.ExpiresAt is null || session.ExpiresAt > DateTimeOffset.UtcNow) ? session : null;
    }

    private AgentContextContribution SessionDenied() => new([], [new(Descriptor.Id, "agent.workflow.session_denied",
        "A current workflow session with an authenticated actor and tenant is required.", true)]);
    private AgentContextContribution ContextDenied() => new([], [new(Descriptor.Id, "agent.workflow.disclosure_invalid",
        "The retained workflow Run evidence does not match this module's supported context kinds and tools.", true)]);
}
