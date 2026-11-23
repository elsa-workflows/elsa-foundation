using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfRuntimeCheckpointCommitStore
{
    private async ValueTask StageIngressModelAuthorityAsync(RuntimeCheckpointCommit commit, string scope, CancellationToken cancellationToken)
    {
        if (commit.ModelState?.State.IngressOrigin is not { } origin) return;
        var state = commit.ModelState.State;
        if (origin.Scope.Value != scope || state.Scope != origin.Scope)
            throw new InvalidDataException("The ingress model checkpoint changed physical scope.");
        var id = EfRuntimeOperationalStoreSupport.CompositeId(scope, state.ExecutionId);
        var row = await context.Set<RuntimeModelExecutionEntity>().SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (row is not null)
        {
            RuntimeModelIngressAuthority.ValidateContinuation(state, EfRuntimeModelExecutionReader.Read(row, scope, state.ExecutionId));
            return;
        }
        var source = await ReadIngressCheckpointSourceAsync(origin.Scope, origin.SourceOutboxItemId, cancellationToken);
        RuntimeModelIngressAuthority.ValidateInitialState(commit, RuntimeIngressAdmissionValidation.Committed(source, origin));
    }
}
