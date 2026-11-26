using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Core.Resources;

/// <summary>Replacement contract for exactly one trusted current-actor source. Missing/duplicate registration is a composition error.</summary>
public interface IDashboardActorContext
{
    ValueTask<DashboardActor> GetAsync(CancellationToken cancellationToken = default);
}

public interface IDashboardResourceStore
{
    ValueTask<DashboardResourceDocument?> FindAsync(string tenantId, string kind, string id, CancellationToken cancellationToken = default);
    /// <summary>Read at most 16 current resource documents without history, ordered by store key, continuing after a previously returned logical resource ID. Never materialize a whole tenant collection.</summary>
    ValueTask<IReadOnlyList<DashboardResourceDocument>> ReadPageAsync(string tenantId, string kind, string? afterId = null, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceDocument?> FindCreatedAsync(string tenantId, string actorId, string kind, string operationKey,
        string fingerprint, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceWriteResult> WriteAsync(DashboardResourceWrite write, CancellationToken cancellationToken = default);
}

public interface IDashboardDefinitionValidator
{
    ValueTask ValidateAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default);
    ValueTask<bool> CanDiscloseAsync(DashboardActor actor, DashboardQueryRequest definition, CancellationToken cancellationToken = default);
}

public interface IDashboardAudienceDirectory
{
    ValueTask<bool> IsValidAsync(DashboardActor actor, DashboardAudienceGrant grant, CancellationToken cancellationToken = default);
    ValueTask<DashboardAudienceCandidatePage> SearchAsync(DashboardActor actor, string kind, string? search,
        string? continuationToken = null, CancellationToken cancellationToken = default);
}

public interface IDashboardResourceService
{
    ValueTask<DashboardResourcePage> ListAsync(DashboardActor actor, string kind, string? ownership = null, string? search = null,
        string? continuationToken = null, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceView> GetAsync(DashboardActor actor, string kind, string id, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceDocument> GetAuthorizedQuestionAsync(DashboardActor actor, string id, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceView> CreateAsync(DashboardActor actor, string kind, DashboardResourceInput input, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceView> UpdateAsync(DashboardActor actor, string kind, string id, string expectedRevision, DashboardResourceInput input,
        CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(DashboardActor actor, string kind, string id, string expectedRevision, CancellationToken cancellationToken = default);
    ValueTask<DashboardAudiencePage> GetAudienceAsync(DashboardActor actor, string kind, string id, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceView> SetAudienceAsync(DashboardActor actor, string kind, string id, string expectedRevision,
        IReadOnlyList<DashboardAudienceGrant> audience, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceView> CopyAsync(DashboardActor actor, string kind, string id, string expectedRevision, DashboardCopyInput input,
        CancellationToken cancellationToken = default);
    ValueTask<DashboardHistoryPage> HistoryAsync(DashboardActor actor, string kind, string id, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceView> GetRevisionAsync(DashboardActor actor, string kind, string id, string revision, CancellationToken cancellationToken = default);
    ValueTask<DashboardResourceView> RestoreAsync(DashboardActor actor, string kind, string id, string expectedRevision, string revision,
        CancellationToken cancellationToken = default);
}
