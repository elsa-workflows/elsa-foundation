using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableStore
{
    internal async ValueTask ProtectIngressRetentionAsync(IReadOnlyList<WorkflowExecutableRootWriteLease> grants,
        IReadOnlyList<StimulusStartRecipient> starts,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Ingress retention requires its fenced source acceptance transaction.");
        var version = RuntimeArtifactWriteVersion.For(context, requiresIngress: true);
        var scope = RequireScope();
        var encodedScope = Encode(scope);
        var scopeHash = Hash(scope);
        var protectedReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (var grant in grants.OrderBy(grant => grant.ArtifactId, StringComparer.Ordinal))
        {
            var id = CreateId(scope, grant.ArtifactId);
            var coordination = await context.WorkflowExecutableCoordinations.SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
                ?? throw new InvalidDataException("A fenced ingress root has no material coordination.");
            // A previous material save may leave this unchanged entry cached. The root fence
            // advanced its physical revision with ExecuteUpdate, which does not refresh EF tracking.
            var entry = context.Entry(coordination);
            if (entry.State != EntityState.Unchanged)
                throw new InvalidOperationException("Ingress retention cannot discard pending coordination changes.");
            await entry.ReloadAsync(cancellationToken);
            if (entry.State != EntityState.Unchanged)
                throw new InvalidDataException("A fenced ingress material coordination disappeared.");
            _ = ReadCoordination(coordination, scope, grant.ArtifactId, id);
            coordination.SchemaVersion = version;
            coordination.Revision = checked(coordination.Revision + 1);

            var encodedArtifact = Encode(grant.ArtifactId);
            var artifactHash = Hash(grant.ArtifactId);
            var sourceRows = await context.WorkflowExecutableSourceReferences
                .Where(row => (row.ScopeKeyHash == scopeHash || row.ScopeKey == encodedScope) &&
                    (row.ArtifactIdHash == artifactHash || row.ArtifactId == encodedArtifact))
                .ToArrayAsync(cancellationToken);
            foreach (var row in sourceRows)
            {
                // The unguarded legacy reference collector also reads this envelope before deletion.
                // Its old reader must refuse the retained source, not silently ignore the new family.
                if (EfSchemaVersion.NotReadable(RuntimeArtifactEfModule.Chain, row.SchemaVersion) ||
                    row.ScopeKey != encodedScope || row.ScopeKeyHash != scopeHash ||
                    row.ArtifactId != encodedArtifact || row.ArtifactIdHash != artifactHash)
                    throw new InvalidDataException("An ingress material source reference has contradictory projections.");
                row.SchemaVersion = version;
                row.Revision = checked(row.Revision + 1);
                protectedReferences.Add(EfRelationalIdentity.Decode(row.SourceReferenceId));
            }
        }
        if (starts.Any(start => start.SourceProvenance is { } provenance &&
            !protectedReferences.Contains(provenance.SourceReferenceId)))
            throw new InvalidDataException("An ingress start's captured source was removed before retention acceptance.");
    }

    private ValueTask<bool> IsIngressRetainedAsync(string scope, string artifactId, CancellationToken cancellationToken) =>
        EfStimulusIngressSourceStore.IsMaterialRetainedAsync(context, accessContextAccessor, scope, artifactId, cancellationToken);
}
