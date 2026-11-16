using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Workflows.Tools;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Workflows.Services;

/// <summary>
/// The workflow version context source (spec 1193 story 8, FR-005, FR-026): a page's reference to one exact persisted version
/// becomes the server's own read of that version through the owning design services (<see cref="IWorkflowVersionReader"/>),
/// under the host's design read permission and the owning definition authorizer's read decision, and it is read again for
/// every turn. Without a version reference it contributes nothing.
/// </summary>
internal sealed class WorkflowVersionContextProvider(
    IWeaverSessionService sessions,
    IWeaverToolAuthorizer authorizer,
    IServiceProvider services,
    IEnumerable<IWorkflowDefinitionAuthorizer>? owners = null) : IWeaverContextProvider
{
    // The owning reader and structure service are resolved only when a version is referenced, so composing this source
    // never builds the design read path a turn without a version does not use.
    private IEnumerable<IWorkflowVersionReader> readers => services.GetServices<IWorkflowVersionReader>();
    private IEnumerable<IActivityStructureService> structures => services.GetServices<IActivityStructureService>();

    public WeaverContextProviderDescriptor Descriptor { get; } = new(WorkflowVersionContext.ContextSourceId, WorkflowVersionContext.ModuleId, "workflow-version",
        [WorkflowVersionContext.VersionKind, WorkflowVersionContext.GuidanceKind], ["1"], [WorkflowVersionContext.ResourceType]);

    public async ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
    {
        var callers = request.CallerAttachments.Where(item => item.ContentType == WorkflowVersionContext.VersionKind).ToArray();
        if (callers.Length == 0) return new([], []);
        var session = await FindSessionAsync(request.SessionId, cancellationToken);
        if (session is null) return SessionDenied();
        var attachments = new List<WeaverContextAttachment>();
        var diagnostics = new List<WeaverContributionDiagnostic>();
        foreach (var caller in callers)
        {
            if (!WorkflowVersionContext.TryRead(caller, out var reference, out var error))
            {
                diagnostics.Add(new(Descriptor.Id, "weaver.workflow.version_context_invalid", error, true));
                continue;
            }
            if (request.Client is not null && reference.Client is not null && request.Client != reference.Client)
            {
                diagnostics.Add(new(Descriptor.Id, "weaver.workflow.context_target_mismatch", "The version context belongs to another page.", true));
                continue;
            }
            var (attachment, refusal) = await AdmitAsync(session, reference, Admitted(caller.Sensitivity), cancellationToken);
            if (refusal is not null) diagnostics.Add(refusal);
            else if (attachments.All(item => item.Id != attachment!.Id)) attachments.Add(attachment!);
        }
        if (attachments.Count > 0) attachments.Add(WorkflowVersionContext.Guidance());
        return new(attachments, diagnostics);
    }

    public async ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
    {
        var session = await FindSessionAsync(request.SessionId, cancellationToken);
        if (session is null) return SessionDenied();
        var projection = new List<WeaverContextAttachment>();
        foreach (var item in request.Context)
        {
            if (item.ModuleId != Descriptor.ModuleId || item.SchemaVersion != "1" || !Descriptor.ContextKinds.Contains(item.ContentType, StringComparer.Ordinal))
                return ContextDenied();
            if (item.ContentType == WorkflowVersionContext.GuidanceKind)
            {
                if (item.Origin != WeaverContextOrigin.InstalledGuidance) return ContextDenied();
                projection.Add(WorkflowVersionContext.Guidance());
                continue;
            }
            // Only this module's own read is ever retained; it is read again, so a withheld or deleted version stops being disclosed.
            if (item.Origin != WeaverContextOrigin.AuthoritativeRead || !WorkflowVersionContext.TryRead(item, out var reference, out _)) return ContextDenied();
            var (fresh, refusal) = await AdmitAsync(session, reference, item.Sensitivity, cancellationToken);
            if (refusal is not null) return new([], [refusal]);
            // A historical projection is what was disclosed then; the current turn discloses the current read (identical
            // for an unchanged version and version list).
            projection.Add(request.IsHistorical || request.IsReceiptContinuation ? item : fresh!);
        }
        foreach (var result in request.Results)
        {
            if (result.Access is not { ModuleId: WorkflowVersionContext.ModuleId } access || result.ToolName != WorkflowReadVersionTool.ToolName
                || !WorkflowVersionContext.IsTarget(access.Target)) return ContextDenied();
            // The read is disclosed again only while the actor may still read the version it describes: the host's design read,
            // then the definition's owner (its definition looked up without reading the version), then the version, read once.
            var versionId = access.Target!.ResourceId!;
            var reader = WorkflowVersionContext.Reader(readers, out _);
            var (owner, definitionId) = await WorkflowVersionContext.AuthorizeVersionAsync(authorizer, owners, reader, session.ActorId, session.TenantId,
                versionId, cancellationToken);
            var read = owner == WorkflowVersionContext.Outcome.Read
                ? await WorkflowVersionContext.ReadOwnedAsync(reader!, definitionId!, versionId, false, cancellationToken)
                : new WorkflowVersionContext.Read(owner);
            if (Refusal(read.Outcome) is { } withheld) return new([], [withheld]);
        }
        return new(projection, []);
    }

    /// <summary>The admitted read of one referenced version, or the required refusal (denied, unavailable or not found).</summary>
    private async Task<(WeaverContextAttachment? Attachment, WeaverContributionDiagnostic? Refusal)> AdmitAsync(WeaverSession session,
        WorkflowVersionContext.Reference reference, WeaverContextSensitivity sensitivity, CancellationToken cancellationToken)
    {
        var reader = WorkflowVersionContext.Reader(readers, out _);
        var read = await WorkflowVersionContext.ReadAsync(authorizer, owners, reader, session.ActorId, session.TenantId,
            reference.DefinitionId, reference.VersionId, true, cancellationToken);
        if (Refusal(read.Outcome) is { } refusal) return (null, refusal);
        if (WorkflowVersionContext.Structure(structures) is not { } structure)
            return (null, new(Descriptor.Id, WorkflowVersionContext.StructureUnavailableCode,
                "The activity structure service is unavailable, so the workflow version cannot be read.", true) { OwnerUnavailable = true });
        var flattened = WorkflowVersionStructure.Flatten(read.Version!.State.RootActivity, structure);
        return (WorkflowVersionContext.Attachment(read.Version, read.Versions!, flattened.Activities.Count, flattened.Truncated, flattened.Incomplete, sensitivity, reference.Client), null);
    }

    private WeaverContributionDiagnostic? Refusal(WorkflowVersionContext.Outcome outcome) => outcome switch
    {
        WorkflowVersionContext.Outcome.Read => null,
        WorkflowVersionContext.Outcome.Denied => new(Descriptor.Id, "weaver.workflow.permission_denied", WorkflowVersionContext.DeniedMessage, true),
        WorkflowVersionContext.Outcome.NotFound => new(Descriptor.Id, WorkflowVersionContext.NotFoundCode, WorkflowVersionContext.NotFoundMessage, true),
        // The owner could not decide: the Design API's own code, marked so the engine reports unavailable (503), never denied.
        WorkflowVersionContext.Outcome.OwnerUnavailable => new(Descriptor.Id, WorkflowDefinitionAuthorizationException.UnavailableCode,
            WorkflowResourceAuthority.UnavailableMessage, true) { OwnerUnavailable = true },
        WorkflowVersionContext.Outcome.AuthorityUnavailable => new(Descriptor.Id, WorkflowVersionContext.AuthorityUnavailableCode,
            WorkflowVersionContext.AuthorityUnavailableMessage, true) { OwnerUnavailable = true },
        _ => new(Descriptor.Id, WorkflowVersionContext.ReaderUnavailableCode,
            "Exactly one owning workflow version reader is required; workflow versions are unavailable.", true) { OwnerUnavailable = true }
    };

    // A caller classification can restrict module-owned data; it cannot certify prior redaction.
    private static WeaverContextSensitivity Admitted(WeaverContextSensitivity caller) =>
        caller == WeaverContextSensitivity.SecretRedacted ? WeaverContextSensitivity.Secret :
        caller < WeaverContextSensitivity.Internal ? WeaverContextSensitivity.Internal : caller;

    private async Task<WeaverSession?> FindSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessions.FindAsync(sessionId, cancellationToken);
        return session is { Status: WeaverSessionStatus.Active } && !string.IsNullOrWhiteSpace(session.ActorId)
            && !string.IsNullOrWhiteSpace(session.TenantId) && (session.ExpiresAt is null || session.ExpiresAt > DateTimeOffset.UtcNow) ? session : null;
    }

    private WeaverContextContribution SessionDenied() => new([], [new(Descriptor.Id, "weaver.workflow.session_denied",
        "A current workflow session with an authenticated actor and tenant is required.", true)]);
    private WeaverContextContribution ContextDenied() => new([], [new(Descriptor.Id, "weaver.workflow.disclosure_invalid",
        "The retained workflow version evidence does not match this module's supported context kinds and tools.", true)]);
}
