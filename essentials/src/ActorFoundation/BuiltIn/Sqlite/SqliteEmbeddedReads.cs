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
              qualificationFormat == StoreFormat.RootPagedPublicEventsQualification ||
