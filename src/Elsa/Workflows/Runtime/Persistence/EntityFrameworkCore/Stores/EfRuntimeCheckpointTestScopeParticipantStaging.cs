using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Checks and touches each test scope a checkpoint admits work into, once, through its shared EF transaction.</summary>
internal static class EfRuntimeCheckpointTestScopeParticipantStaging
{
    /// <param name="admitted">The scope records this checkpoint already read and touched, by scope ID.</param>
    public static async ValueTask AssertOpenAndStageAsync(
        RuntimeDbContext context,
        WorkflowTestScope expected,
        DateTimeOffset occurredAt,
        string accessScope,
        Dictionary<string, WorkflowTestScopeRecord> admitted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (admitted.TryGetValue(expected.ScopeId, out var persisted))
        {
            WorkflowTestScopeAdmission.EnsureOpen(persisted, expected, occurredAt);
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
        persisted = row is null ? null : WorkflowTestScopeEfSupport.Read(row, accessScope, expected.ScopeId);
        WorkflowTestScopeAdmission.EnsureOpen(persisted, expected, occurredAt);

        // The revision-only write fences this admission against a concurrent close in the same transaction.
        WorkflowTestScopeEfSupport.StageAdmission(row!);
        admitted.Add(expected.ScopeId, persisted!);
    }
}
