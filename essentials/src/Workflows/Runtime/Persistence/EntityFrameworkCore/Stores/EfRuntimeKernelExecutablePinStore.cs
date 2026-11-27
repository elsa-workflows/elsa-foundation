using System.Data;
using System.Data.Common;
using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal sealed class EfRuntimeKernelExecutablePinStore(RuntimeDbContext context, IPersistenceAccessContextAccessor access,
    IWorkflowExecutableStore executables, IWorkflowExecutableRootWriteLeaseManager leases, TimeProvider clock) : IRuntimeKernelExecutablePinStore
{
    private static readonly UTF8Encoding PinEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly EfWriteRetry Writes = new(EfWriteRetry.DefaultMaxAttempts, error =>
        EfRelationalExceptionClassifier.IsTransientWriteConflict(error) ||
        EfRelationalExceptionClassifier.IsSaveConflict(error, EfWriteConflict.Concurrency | EfWriteConflict.UniqueKey));

    public async ValueTask<bool> TryPinAsync(IReadOnlyCollection<RuntimeKernelExecutablePin> pins, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pins);
        RuntimeStorePageRequest.ValidateLimit(pins.Count, nameof(pins));
        var scope = Bind();
        var desired = new Dictionary<string, RuntimeKernelExecutablePin>(StringComparer.Ordinal);
        foreach (var pin in pins)
        {
            Validate(pin, scope);
            if (!desired.TryAdd(Identity(pin), pin)) throw new ArgumentException("An actor birth can be pinned only once in a batch.", nameof(pins));
        }

        if (executables.GetMaterialStore() is not EfWorkflowExecutableStore material || !ReferenceEquals(material.Context, context) ||
            context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new NotSupportedException("Kernel executable pins require one clean Runtime transaction over the selected material store.");

        return await Writes.RunAsync(context, TryWriteAsync,
            error => throw new InvalidOperationException("The Runtime executable pin batch changed concurrently.", error), cancellationToken);

        async ValueTask<bool> TryWriteAsync()
        {
            var initial = await ReadBatchAsync(desired, cancellationToken);
            if (initial.Released) return false;
            if (initial.Missing.Count == 0) return true;
            var accepted = false;
            try
            {
                // Acquire all immutable material dependencies before starting the Runtime transaction. Foundation
                // can commit the actor birth only after this transaction makes the pins visible to Runtime GC.
                await leases.ExecuteFencedAsync(initial.Missing.Select(pin => pin.Executable).Distinct().ToArray(),
                    $"kernel-executable-pin:{Guid.NewGuid():N}", async (grants, token) =>
                    {
                        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                        try
                        {
                            var current = await ReadBatchAsync(desired, token);
                            if (current.Released) return;
                            if (current.Missing.Count == 0)
                            {
                                accepted = true;
                                return;
                            }
                            var deadlines = await material.FenceRootWriteLeasesAsync(grants, clock, token);
                            context.KernelExecutablePins.AddRange(current.Missing.Select(Create));
                            await context.SaveChangesAsync(token);
                            foreach (var (grant, expires) in deadlines)
                                if (clock.GetUtcNow() >= expires)
                                    throw new WorkflowExecutableRootWriteLeaseLostException(grant.ArtifactId, grant.LeaseId);
                            await transaction.CommitAsync(token);
                            accepted = true;
                        }
                        finally { context.ChangeTracker.Clear(); }
                    }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                // Reconcile an identical concurrent insert or a commit whose acknowledgement was lost.
                var current = await ReadBatchAsync(desired, cancellationToken);
                if (current.Released) return false;
                if (current.Missing.Count != 0) throw;
                return true;
            }
            return accepted;
        }
    }

    public async ValueTask<string?> ReadOpenTailAsync(PersistenceScope scope, CancellationToken cancellationToken = default) =>
        await OpenPins(Bind(scope)).OrderByDescending(row => row.Id).Select(row => row.Id).FirstOrDefaultAsync(cancellationToken);

    public async ValueTask<RuntimeKernelExecutablePinPage> ListOpenAsync(PersistenceScope scope, string? after, string through, int limit,
        CancellationToken cancellationToken = default)
    {
        Bind(scope);
        RuntimeStorePageRequest.ValidateLimit(limit, nameof(limit));
        RequireCursor(through, nameof(through));
        if (after is not null) RequireCursor(after, nameof(after));
        var query = OpenPins(scope).Where(row => string.Compare(row.Id, through) <= 0);
        if (after is not null) query = query.Where(row => string.Compare(row.Id, after) > 0);
        var rows = await query.OrderBy(row => row.Id).Take(limit + 1).ToArrayAsync(cancellationToken);
        return new(rows.Take(limit).Select(row => ReadOpen(row, scope)).ToArray(), rows.Length > limit ? rows[limit - 1].Id : null);
    }

    public async ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
    {
        var scope = Bind();
        var roots = new HashSet<string>(StringComparer.Ordinal);
        // Read a single rowset so concurrent inserts cannot keep extending the GC pass.
        await foreach (var row in OpenPins(scope).AsAsyncEnumerable().WithCancellation(cancellationToken))
            roots.Add(ReadOpen(row, scope).Executable.ArtifactId);
        return roots;
    }

    public async ValueTask<bool> TryReleaseAsync(RuntimeKernelExecutablePin pin,
        Func<RuntimeKernelExecutablePin, CancellationToken, ValueTask<RuntimeKernelExecutablePinProof>> confirmAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmAsync);
        var scope = Bind();
        Validate(pin, scope);
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new NotSupportedException("Executable pin proof and release require a clean Runtime context.");
        var id = Identity(pin);
        var row = await FindAsync(id, cancellationToken);
        if (row is null) return false;
        if (CheckReplay(row, pin)) return true;

        RuntimeKernelExecutablePinProof proof;
        try { proof = await confirmAsync(pin, cancellationToken); }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            (error is TimeoutException or DbException { IsTransient: true } || EfRelationalExceptionClassifier.IsTransientWriteConflict(error)))
        {
            return false;
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (proof is not (RuntimeKernelExecutablePinProof.BirthRetired or RuntimeKernelExecutablePinProof.IssuanceFencedAbsent)) return false;

        var target = context.KernelExecutablePins.Where(item => item.Id == id && item.Revision == row.Revision &&
            item.ScopeKey == row.ScopeKey && item.ScopeKeyHash == row.ScopeKeyHash && item.Fingerprint == row.Fingerprint &&
            item.ArtifactId == row.ArtifactId && item.ContentJson == row.ContentJson && item.SchemaVersion == row.SchemaVersion && item.ReleasedAtUtcTicks == null);
        var schemaVersion = RuntimeSchemaWriteVersion.For(context, RuntimeKernelExecutablePinEfModule.SchemaFamily,
            RuntimeKernelExecutablePinEfModule.SchemaVersion, row.SchemaVersion);
        var now = clock.GetUtcNow().UtcTicks;
        if (await target.ExecuteUpdateAsync(update => update.SetProperty(item => item.ReleasedAtUtcTicks, now)
            .SetProperty(item => item.ArtifactId, (string?)null).SetProperty(item => item.ContentJson, (string?)null)
            .SetProperty(item => item.SchemaVersion, schemaVersion).SetProperty(item => item.Revision, 2), cancellationToken) == 1)
            return true;
        return await FindAsync(id, cancellationToken) is { } current && CheckReplay(current, pin);
    }

    private async ValueTask<(bool Released, List<RuntimeKernelExecutablePin> Missing)> ReadBatchAsync(
        Dictionary<string, RuntimeKernelExecutablePin> desired, CancellationToken token)
    {
        var ids = desired.Keys.ToArray();
        var rows = await context.KernelExecutablePins.AsNoTracking().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, token);
        var missing = new List<RuntimeKernelExecutablePin>();
        var released = false;
        foreach (var (id, pin) in desired)
        {
            if (rows.TryGetValue(id, out var row)) released |= CheckReplay(row, pin);
            else missing.Add(pin);
        }
        return (released, missing);
    }

    private IQueryable<RuntimeKernelExecutablePinEntity> OpenPins(PersistenceScope scope)
    {
        var scopeHash = Hash(scope.Value);
        return context.KernelExecutablePins.AsNoTracking().Where(row => row.ScopeKeyHash == scopeHash && row.ReleasedAtUtcTicks == null);
    }

    private Task<RuntimeKernelExecutablePinEntity?> FindAsync(string id, CancellationToken token) =>
        context.KernelExecutablePins.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);

    private RuntimeKernelExecutablePinEntity Create(RuntimeKernelExecutablePin pin) => new()
    {
        Id = Identity(pin), ScopeKey = Encode(pin.Scope.Value), ScopeKeyHash = Hash(pin.Scope.Value),
        Fingerprint = Fingerprint(pin), ArtifactId = Encode(pin.Executable.ArtifactId), ContentJson = RuntimeArtifactJson.Serialize(pin),
        SchemaVersion = RuntimeSchemaWriteVersion.For(context, RuntimeKernelExecutablePinEfModule.SchemaFamily,
            RuntimeKernelExecutablePinEfModule.SchemaVersion), Revision = 1
    };

    private static bool CheckReplay(RuntimeKernelExecutablePinEntity row, RuntimeKernelExecutablePin expected)
    {
        CheckEnvelope(row, expected.Scope);
        if (row.Id != Identity(expected) || row.Fingerprint != Fingerprint(expected))
            throw new InvalidDataException("The actor birth is already bound to a different executable pin or issuance.");
        if (row.ReleasedAtUtcTicks is null)
        {
            if (ReadOpen(row, expected.Scope) != expected) throw InvalidPin();
            return false;
        }
        if (row is not { Revision: 2, ReleasedAtUtcTicks: > 0, ArtifactId: null, ContentJson: null }) throw InvalidPin();
        return true;
    }

    private static RuntimeKernelExecutablePin ReadOpen(RuntimeKernelExecutablePinEntity row, PersistenceScope scope)
    {
        CheckEnvelope(row, scope);
        if (row is not { Revision: 1, ReleasedAtUtcTicks: null, ContentJson: { } json, ArtifactId: { } artifactId }) throw InvalidPin();
        var pin = RuntimeArtifactJson.Deserialize<RuntimeKernelExecutablePin>(json);
        Validate(pin, scope);
        if (row.Id != Identity(pin) || row.Fingerprint != Fingerprint(pin) || artifactId != Encode(pin.Executable.ArtifactId)) throw InvalidPin();
        return pin;
    }

    private static void CheckEnvelope(RuntimeKernelExecutablePinEntity row, PersistenceScope scope)
    {
        if (row.ScopeKey != Encode(scope.Value) || row.ScopeKeyHash != Hash(scope.Value) ||
            EfSchemaVersion.NotReadable(RuntimeKernelExecutablePinEfModule.Chain, row.SchemaVersion)) throw InvalidPin();
    }

    private PersistenceScope Bind(PersistenceScope? expected = null)
    {
        var scope = access.Current.RequireScope();
        if (!context.Database.IsRelational() || expected is not null && expected != scope)
            throw new InvalidOperationException("Executable pins require the selected ordinary Runtime persistence scope.");
        return scope;
    }

    private static void Validate(RuntimeKernelExecutablePin pin, PersistenceScope scope)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(pin.Executable);
        if (pin.Scope != scope || pin.Tenant != scope.Value)
            throw new InvalidOperationException("The actor tenant differs from the selected Runtime persistence scope.");
        Text(pin.Tenant, 4_096, nameof(pin.Tenant));
        Text(pin.ActorReference, 256, nameof(pin.ActorReference));
        Text(pin.Issuance, 4_096, nameof(pin.Issuance));
        foreach (var identity in new[] { pin.Executable.ArtifactId, pin.Executable.ArtifactHash, pin.Executable.ArtifactVersion,
                     pin.Executable.DefinitionId, pin.Executable.DefinitionVersionId })
            Text(identity, 4_096, nameof(pin.Executable));
    }

    private static void Text(string value, int maxBytes, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (PinEncoding.GetByteCount(value) > maxBytes || value.Any(char.IsControl))
            throw new ArgumentException("Executable pin fields must fit their bound and contain no control characters.", parameter);
    }

    private static void RequireCursor(string cursor, string parameter)
    {
        ArgumentNullException.ThrowIfNull(cursor, parameter);
        if (cursor.Length != 64 || cursor.AsSpan().IndexOfAnyExcept("0123456789ABCDEF") >= 0)
            throw new ArgumentException("Invalid executable pin cursor.", parameter);
    }

    private static string Identity(RuntimeKernelExecutablePin pin) => CompositeId(pin.Scope.Value, "kernel-executable-pin", pin.ActorReference);
    private static string Fingerprint(RuntimeKernelExecutablePin pin) => Hash(RuntimeArtifactJson.Serialize(pin));
    private static InvalidDataException InvalidPin() => new("The stored executable pin differs from its authoritative row envelope.");
}
