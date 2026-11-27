using Kernel.Foundation.Hosting;
using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public static partial class SqliteEmbeddedHosting
{
    internal static async ValueTask<FoundationChildBirthProof> ConfirmChildBirthAsync(string path, StoreFormat? qualificationFormat,
        Contract.BoundScope scope, Contract.ActorRef child, string issuance, CancellationToken token)
    {
        using var file = AcquireFile(path);
        KernelDb Open() => KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions, qualificationFormat);
        return await EfStore.ConfirmRootChildBirthAsync(Open, scope, child, issuance, token);
    }

    internal static Contract.IActorReads CreateFoundationReads(string databasePath, int pageSize = 100, StoreFormat? qualificationFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(pageSize, int.MaxValue);
        using var file = AcquireFile(databasePath);
        using var db = KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions, qualificationFormat);
        return new SqliteEmbeddedReads(file.Path, pageSize, qualificationFormat, db.UsesRootParkRecovery);
    }

    internal static async ValueTask<T> RunRetentionPolicyAsync<T>(string path, StoreFormat? qualificationFormat,
        Func<EfStore, Task<T>> operation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var file = AcquireFile(path);
            KernelDb Open() => KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions, qualificationFormat);
            return await operation(new EfStore(Open, postgres: false, fenceInBatch: true));
        }
        catch (SqliteException error) when (error.SqliteErrorCode is 5 or 6)
        {
            token.ThrowIfCancellationRequested();
            throw new TimeoutException("The SQLite retention policy could not acquire a store lock.", error);
        }
    }

    private sealed class SqliteEmbeddedReads(string path, int pageSize, StoreFormat? qualificationFormat, bool supportsParkInbox)
        : Contract.IActorReads, Contract.IActorTerminalOutputReads, Contract.IActorSequencedJournalReads, Contract.IActorPlaybackReads,
          Contract.IActorRetentionReads, Contract.IActorParkInboxReads, ICommittedRootStateReads
    {
        public bool SupportsParkInbox => supportsParkInbox;

        public ValueTask<Contract.Page<Contract.ParkInboxEntry>?> ReadParkInboxAsync(Contract.BoundScope scope,
            Contract.ActorRef actor, string selection, Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadParkInboxAsync(scope, actor, selection, from, cancellationToken),
                cancellationToken, normalizeLockTimeout: true);

        public bool SupportsSequencedJournal => qualificationFormat is null
            ? TakesSequencedJournal
            : qualificationFormat is { } format &&
                (format == StoreFormat.RootSequencedJournalQualification || format == StoreFormat.RootLiveEventQualification || SupportsPlayback);

        public bool SupportsPlayback => qualificationFormat is null
            ? RootProfiles.Shipped.HasFlag(RootCapabilities.Playback)
            : qualificationFormat == StoreFormat.RootPlaybackQualification || SupportsDescendantPlayback;

        public bool SupportsDescendantPlayback => qualificationFormat is null
            ? RootProfiles.Shipped.HasFlag(RootCapabilities.PlaybackDescendants)
            : qualificationFormat == StoreFormat.RootPlaybackDescendantsQualification ||
              qualificationFormat == StoreFormat.RootManagedSchedulesQualification ||
              qualificationFormat == StoreFormat.RootPreparedStartsQualification ||
              qualificationFormat == StoreFormat.RootInlinePromotionQualification ||
              qualificationFormat == StoreFormat.RootAuthoredEventsQualification || qualificationFormat == StoreFormat.RootEventStartsQualification ||
              qualificationFormat == StoreFormat.RootEventStartReceiptsQualification || qualificationFormat == StoreFormat.RootEventStartScanQualification ||
              qualificationFormat == StoreFormat.RootDirectTestRunQualification || qualificationFormat == StoreFormat.RootSharedActivationQualification ||
              qualificationFormat == StoreFormat.RootPublicEventStartsQualification || qualificationFormat == StoreFormat.RootPublicEventHostQualification ||
              qualificationFormat == StoreFormat.RootKeyedStartsQualification;

        public ValueTask<Contract.ActorPlaybackPage?> ReadPlaybackAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.ActorPlaybackQuery query, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadPlaybackAsync(scope, actor, query, cancellationToken), cancellationToken, normalizeLockTimeout: true);

        public ValueTask<Contract.ActorSequencedJournalPage?> ReadSequencedJournalAsync(Contract.BoundScope scope,
            Contract.ActorRef actor, long afterSequence, long? upperSequence, int limit, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadSequencedJournalAsync(scope, actor, afterSequence, upperSequence, limit, cancellationToken),
                cancellationToken, normalizeLockTimeout: true);

        public ValueTask<Contract.ActorRetentionQuote> ReadRetentionQuoteAsync(Contract.BoundScope scope, string actorType,
            Contract.ActorRetentionWindows previous, Contract.ActorRetentionWindows proposed,
            KeyValuePair<string, string>? definitionLabel = null, long? asOf = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadRetentionQuoteAsync(scope, actorType, previous, proposed, definitionLabel, asOf, cancellationToken),
                cancellationToken, normalizeLockTimeout: true);

        public ValueTask<Contract.ActorTerminalOutput?> ReadTerminalOutputAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            long endedAt, bool includeValues, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadTerminalOutputAsync(scope, actor, endedAt, includeValues, cancellationToken), cancellationToken);

        public ValueTask<Contract.ActorView?> GetAsync(Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.GetAsync(scope, actor, cancellationToken), cancellationToken, normalizeLockTimeout: SupportsSequencedJournal);

        public async ValueTask<(string TypeData, string? State, Contract.ActorStatus Status)?> ReadCommittedStateAsync(
            Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(scope);
            if (!scope.IsHostIsolated) return null;
            return await ReadAsync(reads => reads.ReadCommittedStateAsync(scope, actor, cancellationToken),
                cancellationToken, normalizeLockTimeout: true);
        }

        public ValueTask<Contract.Page<Contract.DeliveryView>> InboxAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.InboxAsync(scope, actor, from, cancellationToken), cancellationToken);

        public ValueTask<Contract.Page<Contract.ActorView>> ListAsync(Contract.BoundScope scope, Contract.ActorQuery query,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ListAsync(scope, query, from, cancellationToken), cancellationToken,
                normalizeLockTimeout: SupportsSequencedJournal);

        public ValueTask<Contract.Page<Contract.JournalEntry>> TailAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.TailAsync(scope, actor, from, cancellationToken), cancellationToken);

        private async ValueTask<T> ReadAsync<T>(Func<GeneratedRootReads, ValueTask<T>> read, CancellationToken token,
            bool normalizeLockTimeout = false)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var file = AcquireFile(path);
                KernelDb Open() => KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions, qualificationFormat);
                await using var db = Open();
                await EfStore.RequireSqliteRootAdmissionAsync(db, file.AdmissionDefinitions, token);
                var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
                var store = new EfStore(Open, postgres: false, fenceInBatch: true);
                return await read(new GeneratedRootReads(store, cursor, pageSize, provider: "sqlite"));
            }
            catch (SqliteException error) when (normalizeLockTimeout && error.SqliteErrorCode is 5 or 6)
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("The SQLite reader could not acquire its read lock.", error);
            }
        }
    }
}
