using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableStore
{
    public async ValueTask<RuntimeMaterialSourceSnapshot?> FindMaterialBoundedAsync(string artifactId, PersistenceScope expectedScope,
        RuntimeSourceReadLimits limits, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId); ArgumentNullException.ThrowIfNull(expectedScope);
        if (RequireScope() != expectedScope.Value) throw new RuntimeSourceReadException("runtime_source_scope_mismatch");
        var id = CreateId(expectedScope.Value, artifactId);
        var artifact = new RuntimeBoundedSourceRead.Row(typeof(WorkflowExecutableEntity), id);
        var coordination = new RuntimeBoundedSourceRead.Row(typeof(WorkflowExecutableCoordinationEntity), id);
        try
        {
            return await RuntimeBoundedSourceRead.ExecuteAsync<RuntimeMaterialSourceSnapshot?>(context, limits, [artifact, coordination], async (lengths, token) =>
            {
                if (RequireScope() != expectedScope.Value) throw new RuntimeSourceReadException("runtime_source_scope_mismatch");
                var (value, coordinationValue) = await FindBoundedSourcePairAsync(id, token);
                if (value is null && coordinationValue is null) return null;
                if (value is null || coordinationValue is null) throw new RuntimeSourceReadException("runtime_source_material_pair_invalid");
                EnsureMatchingIncarnation(value, coordinationValue, id);
                _ = ReadCoordination(coordinationValue, expectedScope.Value, artifactId, id, limits);
                var material = ReadMaterial(value, expectedScope.Value, artifactId, id, limits);
                return new(material, expectedScope, checked(lengths[artifact] + lengths[coordination]));
            }, cancellationToken);
        }
        catch (RuntimeArtifactEntityFrameworkPersistenceException) { throw; }
        catch (InvalidDataException) { throw new RuntimeSourceReadException("runtime_source_stored_content_invalid"); }
        catch (Exception exception) when (exception is IOException or OperationCanceledException || Elsa.Persistence.EntityFramework.EfRelationalExceptionClassifier.IsProviderFailure(exception))
        { cancellationToken.ThrowIfCancellationRequested(); throw NormalizeProviderFailure("reading bounded material", artifactId, exception, expectedScope.Value); }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException or InvalidOperationException)
        { throw new RuntimeSourceReadException("runtime_source_stored_content_invalid"); }
    }

    // Use the exact physical IDs preflighted in this transaction. Tracked unchanged rows can predate
    // the raw snapshot; ordinary legacy queries deliberately retain their existing tracking behavior.
    private async Task<(WorkflowExecutableEntity? Artifact, WorkflowExecutableCoordinationEntity? Coordination)> FindBoundedSourcePairAsync(string id, CancellationToken token)
    {
        var artifact = await context.WorkflowExecutables.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        var coordination = await context.WorkflowExecutableCoordinations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        return (artifact, coordination);
    }

}
