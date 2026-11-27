using System.Data;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed partial class EfRuntimeKernelExecutablePinStore
{
    private async Task AssignOrdinalsAsync(PersistenceScope scope, RuntimeKernelExecutablePinEntity[] pins, CancellationToken token)
    {
        var state = await ScanState(scope).SingleOrDefaultAsync(token);
        if (state is null)
        {
            state = NewState(scope);
            context.KernelExecutablePinScans.Add(state);
        }
        RequireScanScope(state, scope);
        state.SchemaVersion = WriteScanVersion(state.SchemaVersion);
        // Zero is reserved for the frozen prefix created by the predecessor writer. The same transaction
        // commits the allocated positive ordinals and pins, so opening a pass cannot miss a late insert.
        foreach (var pin in pins)
            pin.InsertionOrdinal = state.LastOrdinal = checked(state.LastOrdinal + 1);
    }

    public async ValueTask<IRuntimeKernelExecutablePinScanPage?> ReadScanPageAsync(PersistenceScope scope, int limit,
        CancellationToken cancellationToken = default)
    {
        Bind(scope);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        var scopeKey = Encode(scope.Value);
        var pins = OpenPins(context, scope).Where(row => row.ScopeKey == scopeKey);
        var state = await ScanState(scope).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var position = state?.Through is not null ? Position(state, scope) : await OpenScanAsync(scope, pins, cancellationToken);
        if (position is null) return null;

        var eligible = pins.Where(row => row.InsertionOrdinal <= position.Through);
        if (position.AfterId is { } afterId)
            eligible = eligible.Where(row => row.InsertionOrdinal > position.AfterOrdinal ||
                row.InsertionOrdinal == position.AfterOrdinal && string.Compare(row.Id, afterId) > 0);
        var rows = await eligible.OrderBy(row => row.InsertionOrdinal).ThenBy(row => row.Id)
            .Take(limit + 1).ToArrayAsync(cancellationToken);
        var next = rows.Length > limit ? (rows[limit - 1].InsertionOrdinal, rows[limit - 1].Id) : ((long, string)?)null;
        return new ScanPage(scope, rows.Take(limit).Select(row => ReadOpen(row, scope)).ToArray(), position, next);
    }

    public async ValueTask<bool> TryAdvanceScanAsync(PersistenceScope scope, IRuntimeKernelExecutablePinScanPage page,
        CancellationToken cancellationToken = default)
    {
        Bind(scope);
        if (page is not ScanPage selected || selected.Scope != scope)
            throw new ArgumentException("The attempted executable pin page belongs to a different scan scope.", nameof(page));
        var position = selected.Position;
        var next = selected.Next;
        var through = next is null ? (long?)null : position.Through;
        var afterOrdinal = next?.Ordinal ?? 0;
        var afterId = next?.Id;
        var scopeKey = Encode(scope.Value);
        var schemaVersion = WriteScanVersion(position.SchemaVersion);
        var count = await ScanState(scope).Where(state => state.ScopeKey == scopeKey && state.SchemaVersion == position.SchemaVersion && state.Pass == position.Pass &&
                state.Through == position.Through && state.AfterOrdinal == position.AfterOrdinal && state.AfterId == position.AfterId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(state => state.Through, through)
                .SetProperty(state => state.AfterOrdinal, afterOrdinal).SetProperty(state => state.AfterId, afterId)
                .SetProperty(state => state.SchemaVersion, schemaVersion), cancellationToken);
        return count == 1;
    }

    private ValueTask<ScanPosition?> OpenScanAsync(PersistenceScope scope, IQueryable<RuntimeKernelExecutablePinEntity> pins,
        CancellationToken token)
    {
        const string operation = "Opening the executable pin scan";
        return Writes.RunUntilSettledAsync<ScanPosition?>(context, async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var state = await ScanState(scope).SingleOrDefaultAsync(token);
            if (state?.Through is not null)
            {
                var current = Position(state, scope);
                await context.RollbackAndClearAsync(transaction);
                return current;
            }
            if (state is not null) RequireScanScope(state, scope);

            if (!await pins.AnyAsync(token))
            {
                await context.RollbackAndClearAsync(transaction);
                return (ScanPosition?)null;
            }
            if (state is null)
            {
                state = NewState(scope);
                context.KernelExecutablePinScans.Add(state);
            }
            if (state.AfterOrdinal != 0 || state.AfterId is not null) throw InvalidScan();

            state.Pass = Guid.NewGuid();
            state.Through = state.LastOrdinal;
            state.SchemaVersion = WriteScanVersion(state.SchemaVersion);
            var position = Position(state, scope);
            return await context.TryCommitAndClearAsync(transaction, Writes, operation, token) is { } conflict
                ? EfWriteAttempt<ScanPosition?>.Retry(conflict) : position;
        }, error => throw new InvalidOperationException("The executable pin scan changed concurrently.", error), token);
    }

    private IQueryable<RuntimeKernelExecutablePinScanEntity> ScanState(PersistenceScope scope)
    {
        var hash = Hash(scope.Value);
        return context.KernelExecutablePinScans.Where(row => row.ScopeKeyHash == hash);
    }

    private static RuntimeKernelExecutablePinScanEntity NewState(PersistenceScope scope) => new()
    {
        ScopeKeyHash = Hash(scope.Value), ScopeKey = Encode(scope.Value)
    };

    private static void RequireScanScope(RuntimeKernelExecutablePinScanEntity state, PersistenceScope scope)
    {
        if (state.ScopeKeyHash != Hash(scope.Value) || state.ScopeKey != Encode(scope.Value) || state.LastOrdinal < 0 ||
            EfSchemaVersion.NotReadable(RuntimeKernelExecutablePinEfModule.Chain, state.SchemaVersion))
            throw InvalidScan();
    }

    private string WriteScanVersion(string current) => RuntimeSchemaWriteVersion.For(context, RuntimeKernelExecutablePinEfModule.SchemaFamily,
        RuntimeKernelExecutablePinEfModule.SchemaVersion, current);

    private static ScanPosition Position(RuntimeKernelExecutablePinScanEntity state, PersistenceScope scope)
    {
        RequireScanScope(state, scope);
        if (state.Pass == Guid.Empty || state.Through is not { } through || through < 0 || through > state.LastOrdinal ||
            state.AfterOrdinal < 0 || state.AfterOrdinal > through || state.AfterId is null && state.AfterOrdinal != 0 ||
            state.AfterId is { } after && (after.Length != 64 || after.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") >= 0))
            throw InvalidScan();
        return new(state.Pass, through, state.AfterOrdinal, state.AfterId, state.SchemaVersion);
    }

    private static InvalidDataException InvalidScan() => new("The persisted executable pin scan position is invalid.");

    private sealed record ScanPosition(Guid Pass, long Through, long AfterOrdinal, string? AfterId, string SchemaVersion);
    private sealed record ScanPage(PersistenceScope Scope, IReadOnlyList<RuntimeKernelExecutablePin> Items, ScanPosition Position,
        (long Ordinal, string Id)? Next) : IRuntimeKernelExecutablePinScanPage
    {
        public bool HasMore => Next is not null;
    }
}
