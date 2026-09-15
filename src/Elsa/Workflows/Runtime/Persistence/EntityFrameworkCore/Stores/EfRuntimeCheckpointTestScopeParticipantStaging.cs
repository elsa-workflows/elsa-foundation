using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Touches each newly admitted test scope once through the checkpoint's shared EF transaction.</summary>
internal static class EfRuntimeCheckpointTestScopeParticipantStaging
{
    public static async ValueTask AssertOpenAndStageAsync(
        BookmarkStateDbContext context,
        WorkflowTestScope expected,
        string ownerId,
        DateTimeOffset occurredAt,
        string accessScope,
        Dictionary<string, WorkflowTestScope> touched,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (touched.TryGetValue(expected.ScopeId, out var admitted))
        {
            if (!WorkflowTestScope.ContextEquals(admitted, expected))
                throw new InvalidOperationException($"Workflow test scope '{expected.ScopeId}' has conflicting checkpoint contexts.");
            return;
        }

        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Checkpoint test-scope admission requires the caller-owned EF transaction.");

        var row = await context.WorkflowTestScopes.SingleOrDefaultAsync(candidate =>
                candidate.Id == WorkflowTestScopeEfSupport.Id(accessScope, expected.ScopeId) &&
                candidate.AccessScopeKey == EfRelationalIdentity.Encode(accessScope) &&
                candidate.AccessScopeKeyHash == EfRelationalIdentity.Hash(accessScope) &&
                candidate.ScopeId == EfRelationalIdentity.Encode(expected.ScopeId) &&
                candidate.ScopeIdHash == EfRelationalIdentity.Hash(expected.ScopeId),
            cancellationToken);
        if (row is null)
            throw new InvalidOperationException($"Workflow test scope '{expected.ScopeId}' is not admitted for '{ownerId}'.");

        var persisted = WorkflowTestScopeEfSupport.Read(row, accessScope, expected.ScopeId);
        if (!WorkflowTestScope.ContextEquals(persisted.Scope, expected) ||
            persisted.State != WorkflowTestScopeState.Open ||
            persisted.Scope.IsExpired(occurredAt))
            throw new InvalidOperationException($"Workflow test scope '{expected.ScopeId}' is not open at checkpoint for '{ownerId}'.");

        WorkflowTestScopeEfSupport.StageAdmission(row);
        touched.Add(expected.ScopeId, expected);
    }
}
