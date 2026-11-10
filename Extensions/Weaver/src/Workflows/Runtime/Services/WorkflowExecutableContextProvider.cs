using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Runtime.Tools;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Workflows.Runtime.Services;

/// <summary>
/// The workflow Executable context source (spec 1193 story 8, FR-027, FR-028): a page's reference to one exact Executable,
/// bound to its Artifact Hash, becomes the server's own read of it through Workflows Runtime Core under the host's runtime
/// read permission, and it is read again for every turn. A changed hash is stale and refused. Without an Executable
/// reference it contributes nothing.
/// </summary>
internal sealed class WorkflowExecutableContextProvider(
    IAgentSessionService sessions,
    IAgentToolAuthorizer authorizer,
    IServiceProvider services) : IAgentContextProvider
{
    // The Runtime owner is resolved only when an Executable is referenced, so composing this source never builds the
    // Runtime read path a turn without an Executable does not use.
    private IEnumerable<IWorkflowExecutableInspection> inspections => services.GetServices<IWorkflowExecutableInspection>();

    public AgentContextProviderDescriptor Descriptor { get; } = new(WorkflowExecutableContext.ContextSourceId, WorkflowExecutableContext.ModuleId,
        "workflow-executable", [WorkflowExecutableContext.ExecutableKind, WorkflowExecutableContext.GuidanceKind], ["1"], [WorkflowExecutableContext.ResourceType]);

    public async ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
    {
        var callers = request.CallerAttachments.Where(item => item.ContentType == WorkflowExecutableContext.ExecutableKind).ToArray();
        if (callers.Length == 0) return new([], []);
        var session = await FindSessionAsync(request.SessionId, cancellationToken);
        if (session is null) return SessionDenied();
        var attachments = new List<AgentContextAttachment>();
        var diagnostics = new List<AgentContributionDiagnostic>();
        foreach (var caller in callers)
        {
            if (!WorkflowExecutableContext.TryRead(caller, out var reference, out var error))
            {
                diagnostics.Add(new(Descriptor.Id, "weaver.workflow.executable_context_invalid", error, true));
                continue;
            }
            if (request.Client is not null && reference.Client is not null && request.Client != reference.Client)
            {
                diagnostics.Add(new(Descriptor.Id, "weaver.workflow.context_target_mismatch", "The Executable context belongs to another page.", true));
                continue;
            }
            var read = await WorkflowExecutableContext.ReadAsync(authorizer, WorkflowExecutableContext.Inspection(inspections), session.ActorId, session.TenantId,
                reference.ArtifactId, reference.ArtifactHash, cancellationToken);
            if (Refusal(read.Outcome) is { } refusal) diagnostics.Add(refusal);
            else
            {
                var attachment = WorkflowExecutableContext.Attachment(read.Executable!, Admitted(caller.Sensitivity), reference.Client);
                if (attachments.All(item => item.Id != attachment.Id)) attachments.Add(attachment);
            }
        }
        if (attachments.Count > 0) attachments.Add(WorkflowExecutableContext.Guidance());
        return new(attachments, diagnostics);
    }

    public async ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
    {
        var session = await FindSessionAsync(request.SessionId, cancellationToken);
        if (session is null) return SessionDenied();
        var inspection = WorkflowExecutableContext.Inspection(inspections);
        var projection = new List<AgentContextAttachment>();
        foreach (var item in request.Context)
        {
            if (item.ModuleId != Descriptor.ModuleId || item.SchemaVersion != "1" || !Descriptor.ContextKinds.Contains(item.ContentType, StringComparer.Ordinal))
                return ContextDenied();
            if (item.ContentType == WorkflowExecutableContext.GuidanceKind)
            {
                if (item.Origin != AgentContextOrigin.InstalledGuidance) return ContextDenied();
                projection.Add(WorkflowExecutableContext.Guidance());
                continue;
            }
            // Only this module's own read is ever retained; it is read again under the bound hash, so a changed Executable stops being disclosed.
            if (item.Origin != AgentContextOrigin.AuthoritativeRead || !WorkflowExecutableContext.TryRead(item, out var reference, out _)) return ContextDenied();
            var read = await WorkflowExecutableContext.ReadAsync(authorizer, inspection, session.ActorId, session.TenantId, reference.ArtifactId, reference.ArtifactHash,
                cancellationToken);
            if (Refusal(read.Outcome) is { } refusal) return new([], [refusal]);
            projection.Add(request.IsHistorical || request.IsReceiptContinuation ? item : WorkflowExecutableContext.Attachment(read.Executable!, item.Sensitivity, reference.Client));
        }
        foreach (var result in request.Results)
        {
            if (result.Access is not { ModuleId: WorkflowExecutableContext.ModuleId } access || result.ToolName != WorkflowReadExecutableTool.ToolName
                || !WorkflowExecutableContext.IsTarget(access.Target)) return ContextDenied();
            // The read is disclosed again only while the actor may still read the Executable it describes, at the same hash.
            var read = await WorkflowExecutableContext.ReadAsync(authorizer, inspection, session.ActorId, session.TenantId, access.Target!.ResourceId!,
                access.Target.Revision!, cancellationToken);
            if (Refusal(read.Outcome) is { } refusal) return new([], [refusal]);
        }
        return new(projection, []);
    }

    private AgentContributionDiagnostic? Refusal(WorkflowExecutableContext.Outcome outcome) => outcome switch
    {
        WorkflowExecutableContext.Outcome.Read => null,
        WorkflowExecutableContext.Outcome.Denied => new(Descriptor.Id, "weaver.workflow.permission_denied", WorkflowExecutableContext.DeniedMessage, true),
        WorkflowExecutableContext.Outcome.NotFound => new(Descriptor.Id, WorkflowExecutableContext.NotFoundCode, WorkflowExecutableContext.NotFoundMessage, true),
        WorkflowExecutableContext.Outcome.Stale => new(Descriptor.Id, WorkflowExecutableContext.StaleCode, WorkflowExecutableContext.StaleMessage, true),
        _ => new(Descriptor.Id, WorkflowExecutableContext.UnavailableCode, WorkflowExecutableContext.UnavailableMessage, true) { OwnerUnavailable = true }
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

    private AgentContextContribution SessionDenied() => new([], [new(Descriptor.Id, "weaver.workflow.session_denied",
        "A current workflow session with an authenticated actor and tenant is required.", true)]);
    private AgentContextContribution ContextDenied() => new([], [new(Descriptor.Id, "weaver.workflow.disclosure_invalid",
        "The retained workflow Executable evidence does not match this module's supported context kinds and tools.", true)]);
}
