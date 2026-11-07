using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableStore
{
    // This owner-specific incarnation fence is part of the caller's activation transaction. It is not a
    // root-write lease, a GC grant, or another save. Actual deletion also CASes this coordination revision.
    internal async ValueTask<bool> StageMessageInterestMaterialFenceAsync(string scope, string artifactId,
        string expectedIncarnation, CancellationToken cancellationToken)
    {
        if (RequireScope() != scope || context.Database.CurrentTransaction is null)
            throw new NotSupportedException("Material fencing requires its actual scoped owner transaction.");
        _ = MessageInterestWriteVersion.Require(context, RuntimeArtifactEfModule.SchemaFamily, RuntimeArtifactEfModule.SchemaVersion);
        var pair = await ReadPairAsync(context, scope, artifactId, cancellationToken);
        if (pair is null || pair.Value.IncarnationId != expectedIncarnation) return false;
        if (pair.Value.State.Guard is not null) throw new InvalidOperationException("The material has a deletion guard.");
        if (pair.Value.Revision == long.MaxValue) throw new InvalidOperationException("The material revision cannot advance.");
        var id = CreateId(scope, artifactId); var revision = pair.Value.Revision;
        return await context.WorkflowExecutableCoordinations.Where(row => row.Id == id && row.Revision == revision &&
                row.IncarnationId == expectedIncarnation && row.SchemaVersion == RuntimeArtifactEfModule.SchemaVersion)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, revision + 1), cancellationToken) == 1;
    }
}
