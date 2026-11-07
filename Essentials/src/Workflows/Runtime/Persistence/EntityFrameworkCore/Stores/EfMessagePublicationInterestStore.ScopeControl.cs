using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfMessagePublicationInterestStore
{
    // Both physical owners use this exact transaction-bound control. No callbacks or unscoped grants.
    internal async ValueTask<bool> StageLockScopesAsync(IReadOnlyCollection<MessageScope> scopes, bool createMissing, CancellationToken ct)
    {
        var transaction = context.Database.CurrentTransaction ?? throw Unsupported();
        _ = MessageInterestWriteVersion.For(context);
        if (!ReferenceEquals(lockedTransaction, transaction))
        { lockedTransaction = transaction; lockedScopes.Clear(); bumpedScopes.Clear(); }
        foreach (var scope in scopes.Distinct().OrderBy(MessagePublicationInterestDocument.ScopeIdentity, StringComparer.Ordinal))
        {
            if (scope.PersistenceScope.Value != RequireScope() || scope.TestScopeId is not null) throw Unsupported();
            access.Current.EnsureTenantScope(scope.TenantId);
            var id = MessagePublicationInterestDocument.ScopeId(scope);
            if (lockedScopes.TryGetValue(id, out var cached))
            { _ = MessagePublicationInterestDocument.Read(cached, scope); continue; }
            var row = await context.MessageInterestScopeControls.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
            if (row is null)
            {
                if (!createMissing) throw MessagePublicationInterestDocument.Invalid();
                // The active writer invokes this before staging any other participant on a clean context.
                if (context.ChangeTracker.HasChanges()) throw Unsupported();
                row = NewControl(scope); context.MessageInterestScopeControls.Add(row);
                await context.SaveChangesAsync(ct); context.Entry(row).State = EntityState.Detached;
            }
            _ = MessagePublicationInterestDocument.Read(row, scope);
            if (row.Epoch == long.MaxValue || row.Revision > long.MaxValue - 2) throw new InvalidOperationException("The scope epoch cannot advance.");
            var revision = row.Revision;
            if (await context.MessageInterestScopeControls.Where(item => item.Id == id && item.Revision == revision &&
                    item.SchemaVersion == RuntimeMessageInterestEfModule.SchemaVersion && item.FullIdentity == row.FullIdentity)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.Revision, revision + 1), ct) != 1) return false;
            row.Revision = revision + 1; context.Attach(row); lockedScopes.Add(id, row);
        }
        return true;
    }
    internal long StageAdvanceScopeEpoch(MessageScope scope) => Bump(scope);
    internal long LockedScopeEpoch(MessageScope scope)
    { EnsureLocked(scope); return lockedScopes[MessagePublicationInterestDocument.ScopeId(scope)].Epoch; }
    internal void RequireActiveTuple(EfWorkflowExecutableSourceReferenceStore selectedReferences,
        EfWorkflowExecutableStore selectedMaterial, IPersistenceAccessContextAccessor selectedAccess, TimeProvider selectedClock)
    {
        RequireScope();
        if (!ReferenceEquals(references, selectedReferences) || !ReferenceEquals(material, selectedMaterial) ||
            !ReferenceEquals(access, selectedAccess) || !ReferenceEquals(clock, selectedClock)) throw Unsupported();
    }
    internal IReadOnlyList<MessageMaterialInterestDeclaration> ExtractActiveDeclarations(RuntimeExecutionMaterial current) => extractor.Extract(current).Declarations;
}
