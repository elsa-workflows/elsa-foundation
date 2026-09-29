using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// Reads and writes the finalization records and the database identity a module context maps through
/// <see cref="EfSchemaFinalization.MapSchemaFinalization"/> (spec 181, FR-001 to FR-004 and FR-019; spec 186, FR-014 to
/// FR-016 and FR-018). It keeps the record's rules, not the gate's: it decides nothing about the fleet.
/// </summary>
/// <remarks>
/// <para>
/// <b>The finalized version only moves forward.</b> It changes in one place, <see cref="CommitIntentAsync"/>, which
/// sets it to the intent in flight. An intent is recorded, and committed, only for a version after the finalized one
/// along the writer's chain, and every change names the <see cref="SchemaFinalizationRecord.Revision"/> it read, which
/// the write compares in the database. So the finalized version cannot move between an intent and its commit, and no
/// method lowers it, repeats it or removes a record.
/// </para>
/// <para>
/// <b>A hold wins against an intent.</b> Placing a hold that applies to the intent in flight abandons it in the same
/// write, so an evaluator whose confirmation began before the hold loses its compare-and-set rather than finalizing.
/// </para>
/// <para>
/// <b>The database identity is created once.</b> It is a random value, never derived from a connection string, written
/// under one fixed key. Of two hosts that create it at once, the second's insert fails on that key and it reads the
/// first's. Every record copies it when the record is created.
/// </para>
/// <para>
/// The store saves through the context it is given, and refuses one that holds unsaved changes of its own, so a
/// finalization change never commits anything else. It leaves nothing tracked behind it.
/// </para>
/// </remarks>
public sealed class EfSchemaFinalizationStore
{
    /// <summary>The width of the database identity column; the identity itself is a 32-character GUID.</summary>
    internal const int DatabaseIdentityLength = 64;

    /// <summary>The key of the one database identity row.</summary>
    internal const int DatabaseIdentityRowId = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    private readonly DbContext context;
    private readonly TimeProvider timeProvider;

    /// <param name="context">A module context whose model maps the finalization tables.</param>
    /// <param name="timeProvider">The clock history entries and intents are stamped with; the system clock when null.</param>
    public EfSchemaFinalizationStore(DbContext context, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Model.FindEntityType(typeof(EfSchemaFinalizationRecordRow)) is null ||
            context.Model.FindEntityType(typeof(EfDatabaseIdentityRow)) is null)
        {
            throw new InvalidOperationException(
                $"{context.GetType().Name} does not map the finalization record. Its OnModelCreating must call " +
                $"modelBuilder.{nameof(EfSchemaFinalization.MapSchemaFinalization)}(<its history module name>).");
        }

        this.context = context;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    private DbSet<EfSchemaFinalizationRecordRow> Records => context.Set<EfSchemaFinalizationRecordRow>();

    private DbSet<EfDatabaseIdentityRow> Identities => context.Set<EfDatabaseIdentityRow>();

    /// <summary>The module's database identity, or null when nothing has created it yet.</summary>
    public async Task<string?> FindDatabaseIdentityAsync(CancellationToken cancellationToken = default)
    {
        var row = await Identities.AsNoTracking().SingleOrDefaultAsync(identity => identity.Id == DatabaseIdentityRowId, cancellationToken);
        if (row is null)
            return null;
        EfSchemaVersion.EnsureReadable(EfSchemaFinalization.Chain, row.SchemaVersion);
        return row.DatabaseIdentity;
    }

    /// <summary>The module's database identity, created now if nothing has created it yet, and the same ever after.</summary>
    public async Task<string> GetOrCreateDatabaseIdentityAsync(CancellationToken cancellationToken = default)
    {
        if (await FindDatabaseIdentityAsync(cancellationToken) is { } existing)
            return existing;

        EnsureNoPendingChanges();
        var row = new EfDatabaseIdentityRow
        {
            Id = DatabaseIdentityRowId,
            DatabaseIdentity = Guid.NewGuid().ToString("N"),
            SchemaVersion = EfSchemaFinalization.SchemaVersion
        };
        Identities.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return row.DatabaseIdentity;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            // Another creator inserted first, and its identity is the database's.
            Detach(row);
            return await FindDatabaseIdentityAsync(cancellationToken)
                   ?? throw new InvalidOperationException("The database identity insert lost to another, yet no identity can be read.", exception);
        }
        finally
        {
            Detach(row);
        }
    }

    /// <summary>The family's record, or null when it has none in this database yet.</summary>
    public async Task<SchemaFinalizationRecord?> FindAsync(string family, CancellationToken cancellationToken = default)
    {
        ValidateFamily(family);
        var row = await Records.AsNoTracking().SingleOrDefaultAsync(record => record.Family == family, cancellationToken);
        return row is null ? null : Read(row);
    }

    /// <summary>Every family's record in this module's database, in one read, keyed by family.</summary>
    public async Task<IReadOnlyDictionary<string, SchemaFinalizationRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Records.AsNoTracking().ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Family, Read, StringComparer.Ordinal);
    }

    /// <summary>
    /// <see cref="FindAsync"/> for a caller on a synchronous path, such as a synchronous <c>SaveChanges</c> that must
    /// re-read a finalized version before it writes (spec 180, FR-015). It tracks nothing, so it leaves a context's
    /// pending changes as they were.
    /// </summary>
    public SchemaFinalizationRecord? Find(string family)
    {
        ValidateFamily(family);
        var row = Records.AsNoTracking().SingleOrDefault(record => record.Family == family);
        return row is null ? null : Read(row);
    }

    /// <summary>
    /// The family's record, created now if it has none: finalized at <paramref name="initialVersion"/>, with the
    /// completion recorded at the same version (spec 181, Edge Cases, "A database with no record yet"; spec 186,
    /// FR-016). A record that exists is returned as it is, whatever <paramref name="initialVersion"/> says.
    /// </summary>
    /// <param name="initialVersion">The oldest version the creator reads: what every row already in the database carries.</param>
    /// <param name="chain">The family's versions, oldest first, as the creator's build declares them.</param>
    public async Task<SchemaFinalizationRecord> GetOrCreateAsync(
        string family,
        string initialVersion,
        IReadOnlyList<string> chain,
        SchemaFinalizationActor creator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creator);
        if (await FindAsync(family, cancellationToken) is { } existing)
            return existing;

        SchemaVersionChain.Require(family, chain, initialVersion, "initial");
        var identity = await GetOrCreateDatabaseIdentityAsync(cancellationToken);
        EnsureNoPendingChanges();
        var at = timeProvider.GetUtcNow();
        var record = new SchemaFinalizationRecord(
            family,
            identity,
            Revision: 1,
            initialVersion,
            Intent: null,
            Holds: [],
            History: [new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.Created, initialVersion, creator, at)],
            new SchemaFinishRecord(initialVersion, VerificationStartedAt: null, VerificationEndedAt: null, creator),
            FinishHistory: [new SchemaFinishHistoryEntry(SchemaFinishTransition.Completed, initialVersion, creator, at)]);
        var row = new EfSchemaFinalizationRecordRow { Family = family, DatabaseIdentity = identity };
        Write(row, record);
        Records.Add(row);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return record;
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.UniqueKey))
        {
            // Another creator inserted this family's record first; it stands, and this one is not written.
            Detach(row);
            return await FindAsync(family, cancellationToken)
                   ?? throw new InvalidOperationException($"The record insert for '{family}' lost to another, yet no record can be read.", exception);
        }
        finally
        {
            Detach(row);
        }
    }

    /// <summary>Pending to readable everywhere: records the durable intent to finalize <paramref name="version"/> (spec 181, FR-006).</summary>
    /// <exception cref="SchemaFinalizationRefusedException">
    /// The version is not after the finalized one, an intent is already in flight, or a hold applies.
    /// </exception>
    public Task<SchemaFinalizationWrite> RecordIntentAsync(
        string family,
        long expectedRevision,
        string version,
        IReadOnlyList<string> chain,
        SchemaFinalizationMember member,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return ChangeAsync(family, expectedRevision, (record, at) =>
        {
            EnsureForward(record.Family, chain, record.FinalizedVersion, version, "finalized", "intent");
            if (record.Intent is { } inFlight)
                throw Refused(record.Family, SchemaFinalizationRefusal.IntentInFlight,
                    $"an intent to finalize '{inFlight.Version}' by {inFlight.Member} is already in flight. Commit or abandon it first.");
            EnsureNotHeld(record, version, chain);
            return record with
            {
                Intent = new SchemaFinalizationIntent(version, member, at),
                History = [.. record.History, new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.IntentRecorded, version, SchemaFinalizationActor.Of(member), at)]
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Readable everywhere to finalized: moves the finalized version to the intent in flight (spec 181, FR-007). The
    /// writer names the revision it read before its confirming membership read, so an intent that changed since is
    /// never committed.
    /// </summary>
    /// <exception cref="SchemaFinalizationRefusedException">
    /// No intent is in flight, it is not after the finalized version, or a hold applies to it.
    /// </exception>
    public Task<SchemaFinalizationWrite> CommitIntentAsync(
        string family,
        long expectedRevision,
        IReadOnlyList<string> chain,
        SchemaFinalizationMember member,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return ChangeAsync(family, expectedRevision, (record, at) =>
        {
            var intent = RequireIntent(record, "commit");
            EnsureForward(record.Family, chain, record.FinalizedVersion, intent.Version, "finalized", "intended");
            EnsureNotHeld(record, intent.Version, chain);
            return record with
            {
                FinalizedVersion = intent.Version,
                Intent = null,
                History = [.. record.History, new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.Finalized, intent.Version, SchemaFinalizationActor.Of(member), at)]
            };
        }, cancellationToken);
    }

    /// <summary>Readable everywhere to pending: abandons the intent in flight (spec 181, FR-007 and FR-008).</summary>
    /// <exception cref="SchemaFinalizationRefusedException">No intent is in flight.</exception>
    public Task<SchemaFinalizationWrite> AbandonIntentAsync(
        string family,
        long expectedRevision,
        SchemaFinalizationMember member,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return ChangeAsync(family, expectedRevision, (record, at) =>
        {
            var intent = RequireIntent(record, "abandon");
            return record with
            {
                Intent = null,
                History = [.. record.History, new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.IntentAbandoned, intent.Version, SchemaFinalizationActor.Of(member), at, reason)]
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Places an operator's hold on the family, or with <paramref name="version"/> on that version and every later one
    /// (spec 181, FR-019). An intent in flight that the hold applies to is abandoned in the same write.
    /// </summary>
    /// <exception cref="SchemaFinalizationRefusedException">
    /// The version is already finalized, so the rollback boundary has been crossed, or a hold with the same scope is in place.
    /// </exception>
    public Task<SchemaFinalizationWrite> PlaceHoldAsync(
        string family,
        long expectedRevision,
        string? version,
        string reason,
        string placedBy,
        IReadOnlyList<string> chain,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var actor = SchemaFinalizationActor.OfOperator(placedBy);
        return ChangeAsync(family, expectedRevision, (record, at) =>
        {
            // A family-wide hold needs no position: it applies to every version, and placing one only ever delays.
            if (version is not null && SchemaVersionChain.Require(record.Family, chain, version, "held") <=
                SchemaVersionChain.Require(record.Family, chain, record.FinalizedVersion, "finalized"))
                throw Refused(record.Family, SchemaFinalizationRefusal.RollbackBoundaryCrossed,
                    $"'{version}' cannot be held: the finalized version is '{record.FinalizedVersion}', so the rollback boundary " +
                    "has been crossed. Only restoring a backup taken before finalization goes back past it.");
            if (FindHold(record, version) is not null)
                throw Refused(record.Family, SchemaFinalizationRefusal.HoldAlreadyPlaced, $"{DescribeHold(version)} is already in place.");

            var hold = new SchemaFinalizationHold(version, reason, placedBy, at);
            var history = record.History.ToList();
            var intent = record.Intent;
            if (intent is not null && hold.AppliesTo(intent.Version, chain))
            {
                history.Add(new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.IntentAbandoned, intent.Version, actor, at, $"A hold was placed: {reason}"));
                intent = null;
            }

            history.Add(new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.HoldPlaced, version, actor, at, reason));
            return record with { Intent = intent, Holds = [.. record.Holds, hold], History = history };
        }, cancellationToken);
    }

    /// <summary>Releases the operator's hold with this scope: family-wide when <paramref name="version"/> is null (spec 181, FR-019).</summary>
    /// <exception cref="SchemaFinalizationRefusedException">No hold with that scope is in place.</exception>
    public Task<SchemaFinalizationWrite> ReleaseHoldAsync(
        string family,
        long expectedRevision,
        string? version,
        string releasedBy,
        CancellationToken cancellationToken = default)
    {
        var actor = SchemaFinalizationActor.OfOperator(releasedBy);
        return ChangeAsync(family, expectedRevision, (record, at) =>
        {
            var hold = FindHold(record, version)
                       ?? throw Refused(record.Family, SchemaFinalizationRefusal.NoHold, $"{DescribeHold(version)} is not in place.");
            return record with
            {
                Holds = record.Holds.Where(candidate => !ReferenceEquals(candidate, hold)).ToArray(),
                History = [.. record.History, new SchemaFinalizationHistoryEntry(SchemaFinalizationTransition.HoldReleased, version, actor, at)]
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Records that a verification pass found no row of the family below <paramref name="version"/> (spec 186, FR-014).
    /// A standing completion only moves forward, and never past the finalized version.
    /// </summary>
    /// <exception cref="SchemaFinalizationRefusedException">
    /// The version is later than the finalized one, or not after the completion that stands.
    /// </exception>
    public Task<SchemaFinalizationWrite> RecordCompletionAsync(
        string family,
        long expectedRevision,
        string version,
        DateTimeOffset verificationStartedAt,
        DateTimeOffset verificationEndedAt,
        IReadOnlyList<string> chain,
        SchemaFinalizationMember member,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (verificationEndedAt < verificationStartedAt)
            throw new ArgumentException("A verification pass cannot end before it starts.", nameof(verificationEndedAt));
        var actor = SchemaFinalizationActor.Of(member);
        return ChangeAsync(family, expectedRevision, (record, at) =>
        {
            if (SchemaVersionChain.Require(record.Family, chain, version, "completion") >
                SchemaVersionChain.Require(record.Family, chain, record.FinalizedVersion, "finalized"))
                throw Refused(record.Family, SchemaFinalizationRefusal.CompletionBeyondFinalized,
                    $"completion cannot be recorded at '{version}', later than the finalized version '{record.FinalizedVersion}'.");
            if (record.Finish is { } standing)
                EnsureForward(record.Family, chain, standing.CompletionVersion, version, "standing completion", "completion");
            return record with
            {
                Finish = new SchemaFinishRecord(version, verificationStartedAt, verificationEndedAt, actor),
                FinishHistory = [.. record.FinishHistory, new SchemaFinishHistoryEntry(SchemaFinishTransition.Completed, version, actor, at)]
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Withdraws the standing completion, for instance after an audit found a row below it (spec 186, FR-018). The
    /// finalized version does not move (FR-019).
    /// </summary>
    /// <exception cref="SchemaFinalizationRefusedException">No completion stands.</exception>
    public Task<SchemaFinalizationWrite> WithdrawCompletionAsync(
        string family,
        long expectedRevision,
        SchemaFinalizationMember member,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);
        return ChangeAsync(family, expectedRevision, (record, at) =>
        {
            var finish = record.Finish
                         ?? throw Refused(record.Family, SchemaFinalizationRefusal.NoCompletion, "no completion stands to withdraw.");
            return record with
            {
                Finish = null,
                FinishHistory = [.. record.FinishHistory, new SchemaFinishHistoryEntry(SchemaFinishTransition.Withdrawn, finish.CompletionVersion, SchemaFinalizationActor.Of(member), at, reason)]
            };
        }, cancellationToken);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the record at <paramref name="expectedRevision"/>, and writes the result as
    /// the next revision only if the database still holds that revision. A record at any other revision, before or
    /// during the write, is a lost compare-and-set: nothing is written.
    /// </summary>
    private async Task<SchemaFinalizationWrite> ChangeAsync(
        string family,
        long expectedRevision,
        Func<SchemaFinalizationRecord, DateTimeOffset, SchemaFinalizationRecord> change,
        CancellationToken cancellationToken)
    {
        ValidateFamily(family);
        EnsureNoPendingChanges();
        var row = await Records.SingleOrDefaultAsync(record => record.Family == family, cancellationToken)
                  ?? throw Refused(family, SchemaFinalizationRefusal.NoRecord, "has no finalization record in this database yet.");
        try
        {
            var current = Read(row);
            if (current.Revision != expectedRevision)
                return new SchemaFinalizationWrite(false, current);

            var next = change(current, timeProvider.GetUtcNow()) with { Revision = checked(current.Revision + 1) };
            Write(row, next);
            await context.SaveChangesAsync(cancellationToken);
            return new SchemaFinalizationWrite(true, next);
        }
        catch (Exception exception) when (EfRelationalExceptionClassifier.IsSaveConflict(exception, EfWriteConflict.Concurrency))
        {
            // Another writer changed the record between this read and this write.
            Detach(row);
            return new SchemaFinalizationWrite(false, await FindAsync(family, cancellationToken)
                ?? throw new InvalidOperationException($"The record of '{family}' changed under this write, yet can no longer be read.", exception));
        }
        finally
        {
            Detach(row);
        }
    }

    /// <summary>Refuses a move from <paramref name="from"/> to <paramref name="to"/> that is not strictly forward along the chain.</summary>
    private static void EnsureForward(string family, IReadOnlyList<string> chain, string from, string to, string fromRole, string toRole)
    {
        if (SchemaVersionChain.Require(family, chain, to, toRole) <= SchemaVersionChain.Require(family, chain, from, fromRole))
            throw Refused(family, SchemaFinalizationRefusal.NotForward,
                $"the {toRole} version '{to}' is not after the {fromRole} version '{from}'. It only moves forward along the chain.");
    }

    private static void EnsureNotHeld(SchemaFinalizationRecord record, string version, IReadOnlyList<string> chain)
    {
        if (record.Holds.FirstOrDefault(hold => hold.AppliesTo(version, chain)) is { } hold)
            throw Refused(record.Family, SchemaFinalizationRefusal.Held,
                $"'{version}' cannot finalize while {DescribeHold(hold.Version)} placed by {hold.PlacedBy} is in place: {hold.Reason}");
    }

    private static SchemaFinalizationIntent RequireIntent(SchemaFinalizationRecord record, string action) =>
        record.Intent ?? throw Refused(record.Family, SchemaFinalizationRefusal.NoIntent, $"no intent is in flight to {action}.");

    private static SchemaFinalizationHold? FindHold(SchemaFinalizationRecord record, string? version) =>
        record.Holds.FirstOrDefault(hold => StringComparer.Ordinal.Equals(hold.Version, version));

    private static string DescribeHold(string? version) => version is null ? "a family-wide hold" : $"a hold on '{version}'";

    private static SchemaFinalizationRefusedException Refused(string family, SchemaFinalizationRefusal refusal, string message) =>
        new(family, refusal, message);

    /// <summary>
    /// Reads a row as a record. The stamp is checked before anything else is read from the row, so a row a build this
    /// host does not run wrote reports skew rather than corruption, and none of its JSON is parsed; each JSON column is
    /// then upcast from the row's stamp to the current version before it is parsed.
    /// </summary>
    private static SchemaFinalizationRecord Read(EfSchemaFinalizationRecordRow row)
    {
        EfSchemaVersion.EnsureReadable(EfSchemaFinalization.Chain, row.SchemaVersion);
        return new SchemaFinalizationRecord(
            row.Family,
            row.DatabaseIdentity,
            row.Revision,
            row.FinalizedVersion,
            row.IntentJson is null ? null : Deserialize<SchemaFinalizationIntent>(row, Upcast(row, nameof(row.IntentJson), row.IntentJson)),
            Deserialize<SchemaFinalizationHold[]>(row, Upcast(row, nameof(row.HoldsJson), row.HoldsJson)),
            Deserialize<SchemaFinalizationHistoryEntry[]>(row, Upcast(row, nameof(row.HistoryJson), row.HistoryJson)),
            row.FinishJson is null ? null : Deserialize<SchemaFinishRecord>(row, Upcast(row, nameof(row.FinishJson), row.FinishJson)),
            Deserialize<SchemaFinishHistoryEntry[]>(row, Upcast(row, nameof(row.FinishHistoryJson), row.FinishHistoryJson)));
    }

    /// <summary>A JSON column of a record row, as the current version stores it. Every module maps the record table under a
    /// name of its own, so the upcaster sees the name's shared prefix.</summary>
    private static string Upcast(EfSchemaFinalizationRecordRow row, string column, string json) =>
        EfSchemaFinalization.Chain.Upcast(row.SchemaVersion, EfSchemaFinalization.RecordTablePrefix, column, json)!;

    /// <summary>Writes a record into its row; the family and its database identity are set once, when the row is created.</summary>
    private static void Write(EfSchemaFinalizationRecordRow row, SchemaFinalizationRecord record)
    {
        row.SchemaVersion = EfSchemaFinalization.SchemaVersion;
        row.Revision = record.Revision;
        row.FinalizedVersion = record.FinalizedVersion;
        row.IntentJson = record.Intent is null ? null : JsonSerializer.Serialize(record.Intent, JsonOptions);
        row.HoldsJson = JsonSerializer.Serialize(record.Holds, JsonOptions);
        row.HistoryJson = JsonSerializer.Serialize(record.History, JsonOptions);
        row.FinishJson = record.Finish is null ? null : JsonSerializer.Serialize(record.Finish, JsonOptions);
        row.FinishHistoryJson = JsonSerializer.Serialize(record.FinishHistory, JsonOptions);
    }

    private static T Deserialize<T>(EfSchemaFinalizationRecordRow row, string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                   ?? throw new InvalidDataException($"The finalization record of '{row.Family}' holds null where a {typeof(T).Name} belongs.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // A shape the record's own constructors reject is as unreadable as malformed JSON.
            throw new InvalidDataException($"The finalization record of '{row.Family}' holds a {typeof(T).Name} that cannot be read.", exception);
        }
    }

    private static void ValidateFamily(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        if (family.Length > EfSchemaFinalization.MaxFamilyLength)
            throw new ArgumentException($"A schema family name is at most {EfSchemaFinalization.MaxFamilyLength} characters.", nameof(family));
    }

    private void EnsureNoPendingChanges()
    {
        if (context.ChangeTracker.HasChanges())
            throw new InvalidOperationException(
                $"The finalization store saves through the {context.GetType().Name} it was given, which holds unsaved changes of its own. " +
                "Give the store a context with none, so a finalization change never commits anything else.");
    }

    private void Detach(object row) => context.Entry(row).State = EntityState.Detached;
}
