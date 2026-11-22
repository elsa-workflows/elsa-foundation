using Elsa.Persistence.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;

/// <summary>Typed source capture/read adapter; graph mutation remains in the Workflow commands.</summary>
public sealed class WorkflowSourceRevisions(WorkflowsDesignDbContext db, IPersistenceAccessContextAccessor access, IPayloadSerializer serializer)
{
    public const string Profile = "elsa.workflow-source/1";
    internal static SourceScope Scope(string? tenantId) => new("authoring", tenantId, "workflow", "workflow-design");
    private EfSourceRevisions Store()
    {
        if (access.Current.AcrossScopes) throw new SourceRevisionException("scope-required");
        return new(db, Scope(access.Current.Scope?.Value));
    }
    private async Task<string?> CanonicalDefinition(string definitionId, string draftId, CancellationToken ct)
    {
        _ = Store(); // Reject an administrative across-scope context before resolving a domain identity.
        var definition = await new EfWorkflowDefinitionStore(db, access).FindByIdAsync(definitionId, ct);
        return definition?.Id;
    }
    public async Task<SourceSnapshot> CaptureAsync(string definitionId, string draftId, long expectedRevision, string operationId, CancellationToken cancellationToken = default)
    {
        var canonical = await CanonicalDefinition(definitionId, draftId, cancellationToken) ?? throw new SourceRevisionException("not-found");
        return await Store().CaptureAsync(canonical, draftId, expectedRevision, operationId, cancellationToken);
    }
    public async Task<WorkflowDefinitionState?> ReadAsync(string definitionId, string draftId, CancellationToken cancellationToken = default)
    {
        var canonical = await CanonicalDefinition(definitionId, draftId, cancellationToken);
        if (canonical is null) return null;
        var row = await Store().FindAsync(canonical, draftId, cancellationToken);
        if (row is null) return null;
        if (row.Profile != Profile) throw new SourceRevisionException("profile-unsupported");
        return serializer.Deserialize<WorkflowDefinitionState>(row.Content);
    }
    public async Task<WorkflowDefinitionState?> ReadSnapshotAsync(string definitionId, string draftId, string snapshotId, CancellationToken cancellationToken = default)
    {
        var canonical = await CanonicalDefinition(definitionId, draftId, cancellationToken);
        if (canonical is null) return null;
        var row = await Store().FindSnapshotAsync(canonical, draftId, snapshotId, cancellationToken);
        if (row is null) return null;
        if (row.Profile != Profile) throw new SourceRevisionException("profile-unsupported");
        return serializer.Deserialize<WorkflowDefinitionState>(row.Content);
    }
    public async Task<IReadOnlyList<SourceSnapshot>> HistoryAsync(string definitionId, string draftId, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        var canonical = await CanonicalDefinition(definitionId, draftId, cancellationToken);
        return canonical is null ? [] : await Store().HistoryAsync(canonical, draftId, skip, take, cancellationToken);
    }
}
