using System.Data;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Core.StandaloneTriggers.RuntimeKernelScheduleControl;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;
using static Elsa.Workflows.Runtime.Services.Executables.WorkflowActivationSwitchRules;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeKernelScheduleControlStore
{
    private static readonly EfWriteRetry ScanCommits = new(MaximumAttempts, exception =>
        EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey | EfWriteConflict.Transient));

    // The ordinary and adoption publication paths have both written their slot, source and claims before
    // either can acquire the shared ordinal row. Keep the lock through their common transaction commit.
    internal static Func<CancellationToken, Task>? AssignOrdinalsAfterSave(RuntimeDbContext db, string persistence)
    {
        var added = db.ChangeTracker.Entries<RuntimeKernelScheduleControlEntity>().Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        return added.Length == 0 ? null : AssignAsync;

        async Task AssignAsync(CancellationToken token)
        {
            var first = added[0];
            if (first.ScopeKeyHash != Hash(persistence) || added.Any(row => row.ScopeKeyHash != first.ScopeKeyHash ||
                    row.TenantKeyHash != first.TenantKeyHash || row.InsertionOrdinal != 0))
                throw new InvalidDataException("New schedule controls must share a scoped insertion order.");
            var state = await ScanState(db, first.ScopeKeyHash, first.TenantKeyHash).SingleOrDefaultAsync(token);
            if (state is null)
            {
                state = new() { ScopeKeyHash = first.ScopeKeyHash, TenantKeyHash = first.TenantKeyHash };
                db.KernelScheduleControlScans.Add(state);
            }
            else
                EfSchemaVersion.EnsureReadable(RuntimeKernelScheduleControlEfModule.Chain, state.SchemaVersion);
            StampScanState(db, state);
            foreach (var row in added)
                row.InsertionOrdinal = state.LastOrdinal = checked(state.LastOrdinal + 1);
            await db.SaveChangesAsync(token);
        }
    }

    public async ValueTask<IRuntimeKernelScheduleControlScanPage?> ReadScanPageAsync(TriggerScope scope, int limit,
        CancellationToken cancellationToken = default)
    {
        var controls = ScopedControls(scope);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        scope = CanonicalScope(scope);
        var scopeHash = Hash(scope.Persistence.Value);
        var tenantHash = TenantHash(scope);
        var state = await ScanState(context, scopeHash, tenantHash).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (state is not null)
            EfSchemaVersion.EnsureReadable(RuntimeKernelScheduleControlEfModule.Chain, state.SchemaVersion);
        var pass = state?.Through is not null ? Position(state) : await OpenScanAsync(controls, scopeHash, tenantHash, cancellationToken);
        if (pass is null) return null;

        var eligible = controls.Where(row => row.InsertionOrdinal <= pass.Through);
        if (pass.AfterId is { } afterId)
            eligible = eligible.Where(row => row.InsertionOrdinal > pass.AfterOrdinal ||
                row.InsertionOrdinal == pass.AfterOrdinal && string.Compare(row.Id, afterId) > 0);
        var rows = await eligible.OrderBy(row => row.InsertionOrdinal).ThenBy(row => row.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > limit;
        var next = hasMore ? (rows[limit - 1].InsertionOrdinal, rows[limit - 1].Id) : ((long, string)?)null;
        return new ScanPage(scope, rows.Take(limit).Select(row => ReadScoped(row, scope)).ToArray(), pass, next);
    }

    public async ValueTask<bool> TryAdvanceScanAsync(TriggerScope scope, IRuntimeKernelScheduleControlScanPage page,
        CancellationToken cancellationToken = default)
    {
        Bind(scope);
        scope = CanonicalScope(scope);
        if (page is not ScanPage selected || selected.Scope != scope)
            throw new ArgumentException("The attempted schedule page belongs to a different scan scope.", nameof(page));
        var pass = selected.Position;
        var next = selected.Next;
        var through = next is null ? (long?)null : pass.Through;
        var afterOrdinal = next?.Ordinal ?? 0;
        var afterId = next?.Id;
        var writeVersion = RuntimeSchemaWriteVersion.For(context, RuntimeKernelScheduleControlEfModule.SchemaFamily,
            RuntimeKernelScheduleControlEfModule.InitialSchemaVersion, pass.SchemaVersion);
        var count = await ScanState(context, Hash(scope.Persistence.Value), TenantHash(scope)).Where(state => state.Pass == pass.Pass &&
                state.Through == pass.Through && state.AfterOrdinal == pass.AfterOrdinal && state.AfterId == pass.AfterId &&
                state.SchemaVersion == pass.SchemaVersion)
            .ExecuteUpdateAsync(setters => setters.SetProperty(state => state.SchemaVersion, writeVersion).SetProperty(state => state.Through, through)
                .SetProperty(state => state.AfterOrdinal, afterOrdinal).SetProperty(state => state.AfterId, afterId), cancellationToken);
        return count == 1;
    }

    private ValueTask<ScanPosition?> OpenScanAsync(IQueryable<RuntimeKernelScheduleControlEntity> controls, string scopeHash,
        string tenantHash, CancellationToken token)
    {
        const string operation = "Opening the schedule control scan";
        return ScanCommits.RunUntilSettledAsync<ScanPosition?>(context, async () =>
        {
            token.ThrowIfCancellationRequested();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var state = await ScanState(context, scopeHash, tenantHash).SingleOrDefaultAsync(token);
            if (state is not null)
                EfSchemaVersion.EnsureReadable(RuntimeKernelScheduleControlEfModule.Chain, state.SchemaVersion);
            if (state is null)
            {
                if (!await controls.AnyAsync(token)) return (ScanPosition?)null;
                state = new() { ScopeKeyHash = scopeHash, TenantKeyHash = tenantHash };
                context.KernelScheduleControlScans.Add(state);
            }
            if (state.Through is not null)
            {
                var current = Position(state);
                await context.RollbackAndClearAsync(transaction);
                return current;
            }

            StampScanState(context, state);
            state.Pass = checked(state.Pass + 1);
            state.Through = state.LastOrdinal;
            var position = Position(state);
            return await context.TryCommitAndClearAsync(transaction, ScanCommits, operation, token) is { } conflict
                ? EfWriteAttempt<ScanPosition?>.Retry(conflict) : position;
        }, conflict => throw CommitFailure(operation, conflict), token);
    }

    private static IQueryable<RuntimeKernelScheduleControlScanEntity> ScanState(RuntimeDbContext db, string scopeHash, string tenantHash) =>
        db.KernelScheduleControlScans.Where(row => row.ScopeKeyHash == scopeHash && row.TenantKeyHash == tenantHash);

    private static ScanPosition Position(RuntimeKernelScheduleControlScanEntity state)
    {
        if (state.Pass <= 0 || state.Through is not { } through || through < 0 || through > state.LastOrdinal ||
            state.AfterOrdinal < 0 || state.AfterOrdinal > through || state.AfterId is null && state.AfterOrdinal != 0)
            throw new InvalidDataException("The persisted schedule scan position is invalid.");
        return new(state.Pass, through, state.AfterOrdinal, state.AfterId, state.SchemaVersion);
    }

    private static void StampScanState(RuntimeDbContext db, RuntimeKernelScheduleControlScanEntity state) =>
        state.SchemaVersion = RuntimeSchemaWriteVersion.For(db, RuntimeKernelScheduleControlEfModule.SchemaFamily,
            RuntimeKernelScheduleControlEfModule.InitialSchemaVersion, state.SchemaVersion);

    private sealed record ScanPosition(long Pass, long Through, long AfterOrdinal, string? AfterId, string SchemaVersion);
    private sealed record ScanPage(TriggerScope Scope, IReadOnlyList<RuntimeKernelScheduleControl> Items, ScanPosition Position,
        (long Ordinal, string Id)? Next) : IRuntimeKernelScheduleControlScanPage
    {
        public bool HasMore => Next is not null;
    }
}
