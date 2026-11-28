using System.Data;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed partial class PostgresRootAdmission
{
    internal Contract.IAuthoredStandaloneEventOutbox BindAuthoredStandaloneEvents(Contract.BoundScope scope) =>
        new BoundAuthoredStandaloneEventOutbox(
            (after, token) => WithAuthoredStandaloneEventSourceAsync((db, bind) =>
                RootAuthoredStandaloneEvents.ReadAsync(db, scope, after, bind, token), token),
            (occurrence, fingerprint, token) => WithAuthoredStandaloneEventSourceAsync((db, bind) =>
                RootAuthoredStandaloneEvents.AcknowledgeAsync(db, scope, occurrence, fingerprint, bind, token), token));

    private async Task<T> WithAuthoredStandaloneEventSourceAsync<T>(
        Func<KernelDb, Func<FormattableString, FormattableString>, Task<T>> work, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesAuthoredStandaloneEvents) throw new NotSupportedException("This PostgreSQL profile cannot transfer authored standalone Events.");
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(2));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var pin = await PinRootNamespaceAsync(db, loadIssuer: false, token) ??
            throw new StoreFormatException("Authored standalone Events require the pinned PostgreSQL store.");
        await LockPublicEventStartTablesAsync(db, token);
        var result = await work(db, sql => BindPublicEventStarts(db, sql, pin));
        await transaction.CommitAsync(token);
        return result;
    }
}

internal sealed class BoundAuthoredStandaloneEventOutbox(
    Func<Contract.AuthoredStandaloneEventCursor?, CancellationToken, Task<IReadOnlyList<Contract.AuthoredStandaloneEvent>>> read,
    Func<string, string, CancellationToken, Task<bool>> acknowledge) : Contract.IAuthoredStandaloneEventOutbox
{
    public ValueTask<IReadOnlyList<Contract.AuthoredStandaloneEvent>> ReadAsync(Contract.AuthoredStandaloneEventCursor? after = null,
        CancellationToken cancellationToken = default) => new(read(after, cancellationToken));

    public ValueTask<bool> AcknowledgeAsync(string occurrence, string transferFingerprint, CancellationToken cancellationToken = default) =>
        new(acknowledge(occurrence, transferFingerprint, cancellationToken));
}
