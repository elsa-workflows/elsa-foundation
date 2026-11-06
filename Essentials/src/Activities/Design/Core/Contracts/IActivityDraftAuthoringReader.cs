using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>
/// Reads persisted draft source visible to the current actor, including provider-payload permission.
/// This is a scoped single-implementation replacement authority; its owning feature rejects duplicates at startup.
/// Optional absence is valid. Consumers fail closed when none or several are installed.
/// No principal, permission, tenant or draft facts are accepted from a caller.
/// </summary>
public interface IActivityDraftAuthoringReader
{
    ValueTask<ActivityDraftAuthoringSource?> FindAsync(string draftId, CancellationToken cancellationToken = default);
}

public sealed record ActivityDraftAuthoringSource(string DraftId, long Revision, ActivityDefinitionDraftState State, string AuthorizationRevision);
