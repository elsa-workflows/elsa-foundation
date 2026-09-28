using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.EntityFramework.SchemaFinalization;

/// <summary>
/// The finalization gate of one EF module in one shell, and so in one database (spec 181): it admits the module at
/// activation, finalizes its families once every counted member reads a newer version, and holds what this host writes
/// for each family to the finalized version it last observed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariant.</b> No row is stamped with a version some counted member cannot read, and no host that cannot read
/// a family's finalized version keeps the family's module active or writes to the family. It holds through these
/// mechanisms, each tested on its own:
/// </para>
/// <list type="number">
/// <item><b>Writers stamp only the finalized version</b> (FR-009). <see cref="StateOf"/> is the write version the
/// write check (<see cref="EfSchemaWriteGateInterceptor"/>) holds every row to; it only ever takes a finalized version
/// from the record.</item>
/// <item><b>An intent, then a confirmation.</b> An evaluation writes the intent durably and only then reads the fleet
/// fresh, committing by compare-and-set against the revision that carries the intent (FR-006, FR-007). An activation
/// publishes this host's report first and only then reads the record (FR-013). With read-after-write consistency on
/// both stores, one side always sees the other: the evaluator counts the newcomer and abandons, or the newcomer sees
/// the intent or the finalization and waits, or refuses (FR-014).</item>
/// <item><b>Finalization only moves forward</b>, which the record's store enforces (FR-003).</item>
/// <item><b>Activation refuses an unreadable finalized version</b> (FR-015), and an unreadable completion version.</item>
/// <item><b>Active hosts keep checking</b> (FR-010, FR-012): a refresh that finds a finalized version outside this
/// host's readable set refuses every write to the family. A member that has lapsed, or has not been admitted under its
/// current incarnation, keeps the write version it had and never adopts a newer one (FR-018).</item>
/// </list>
/// <para>
/// Without an <see cref="IEfSchemaFleet"/> the gate cannot count anyone, so it never finalizes a version past the one
/// its record was created at: that only delays finalization. It still creates the record, refuses an unreadable one,
/// and holds writes to what it observed.
/// </para>
/// </remarks>
public sealed class EfSchemaModuleGate
{
    private static readonly string ProcessIncarnation = Guid.NewGuid().ToString("N");

    private readonly IEfSchemaFleet? _fleet;
    private readonly EfSchemaFinalizationObservations _observations;
    private readonly EfSchemaFinalizationOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly bool _publishBeforeRead;
    private readonly object _lock = new();
    private Dictionary<string, EfSchemaFamilyWriteState> _states = new(StringComparer.Ordinal);
    private string? _databaseIdentity;
    private string? _admittedIncarnation;

    /// <param name="families">The families of the module this gate guards.</param>
    /// <param name="fleet">This host's view of the fleet, or null when the host composes none.</param>
    /// <param name="observations">What this host has read, shared by every gate of the host, for its readability report.</param>
    /// <param name="publishBeforeRead">
    /// False only for a module composed on the host container whose own activation precedes the member's join, the
    /// membership module itself: its join is its publish, so it is admitted without one and re-admitted, publish first,
    /// at the first refresh after the join, adopting no newer version until then.
    /// </param>
    public EfSchemaModuleGate(
        EfSchemaModuleFamilies families,
        IEfSchemaFleet? fleet,
        EfSchemaFinalizationObservations observations,
        EfSchemaFinalizationOptions options,
        TimeProvider? time = null,
        ILogger? logger = null,
        bool publishBeforeRead = true)
    {
        Families = families ?? throw new ArgumentNullException(nameof(families));
        _fleet = fleet;
        _observations = observations ?? throw new ArgumentNullException(nameof(observations));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _publishBeforeRead = publishBeforeRead;
    }

    public EfSchemaModuleFamilies Families { get; }

    /// <summary>The EF module this gate guards.</summary>
    public string Module => Families.Module;

    /// <summary>The module's database identity, once the gate has admitted the module.</summary>
    public string? DatabaseIdentity
    {
        get
        {
            lock (_lock)
                return _databaseIdentity;
        }
    }

    /// <summary>
    /// What this host writes for <paramref name="family"/>, or null before the module has been admitted, or for a
    /// family the module does not own.
    /// </summary>
    public EfSchemaFamilyWriteState? StateOf(string family)
    {
        lock (_lock)
            return _states.GetValueOrDefault(family);
    }

    /// <summary>
    /// Admits the module (FR-013 to FR-015, FR-021): publishes this host's report, creates any missing record, refuses
    /// a family whose finalized or completion version this host cannot read, waits out an intent to finalize a version
    /// it cannot read, and records what it observed. Then it evaluates, which finalizes at once on a host that is a
    /// cluster of one, and refreshes.
    /// </summary>
    /// <exception cref="EfSchemaActivationRefusedException">The module must not activate on this host.</exception>
    public async Task ActivateAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var standing = _fleet?.GetLocalStanding();
        var admitted = await AdmitAsync(context, _publishBeforeRead ? PublishFirst.WaitingUpToTheBound : PublishFirst.No, cancellationToken);
        Adopt(admitted.Identity, admitted.Records, mayAdvance: true);
        lock (_lock)
            _admittedIncarnation = _publishBeforeRead ? standing?.Member.Incarnation : null;

        await PublishQuietlyAsync(cancellationToken);
        await EvaluateAsync(context, cancellationToken);
        await RefreshAsync(context, cancellationToken);
    }

    /// <summary>
    /// Evaluates every family (FR-005 to FR-008): resolves an intent in flight by confirming it, or else records an
    /// intent for the newest version above the finalized one that no hold keeps and every counted member reads, reads
    /// the fleet again, and commits or abandons it. A member that is not counted right now, because it lapsed or has not
    /// been admitted under its incarnation, does not evaluate.
    /// </summary>
    public async Task EvaluateAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var identity = DatabaseIdentity;
        if (_fleet is null || identity is null || !IsCounted(_fleet.GetLocalStanding()))
            return;

        var member = _fleet.GetLocalStanding().Member;
        var store = new EfSchemaFinalizationStore(context, _time);
        foreach (var chain in Families.Chains)
        {
            if (StateOf(chain.Family) is { WritesRefused: true })
                continue;
            var record = await store.FindAsync(chain.Family, cancellationToken);
            if (record is null || !chain.IsReadable(record.FinalizedVersion))
                continue;
            if (record.Intent is not null)
            {
                await ResolveAsync(store, chain, record, identity, member, cancellationToken);
                continue;
            }

            if (await ChooseAsync(chain, record, identity, cancellationToken) is not { } target)
                continue;
            SchemaFinalizationWrite intended;
            try
            {
                intended = await store.RecordIntentAsync(chain.Family, record.Revision, target, chain.ReadableVersions, member, cancellationToken);
            }
            catch (SchemaFinalizationRefusedException refusal)
            {
                // A hold or another intent landed between the read and this write; the next evaluation decides again.
                _logger.LogDebug(refusal, "Schema family {Family} did not record an intent to finalize {Version}.", chain.Family, target);
                continue;
            }

            if (intended.Applied)
                await ResolveAsync(store, chain, intended.Record, identity, member, cancellationToken);
        }
    }

    /// <summary>
    /// Re-reads every family's record (FR-010) and adopts its finalized version as the write version, unless this member
    /// may not adopt a newer one right now (FR-018). A finalized version outside the readable set refuses every write to
    /// the family (FR-012). A member whose incarnation changed since it was admitted is admitted again first, publish
    /// first; until that succeeds it adopts nothing newer.
    /// </summary>
    public async Task RefreshAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (DatabaseIdentity is not { } identity)
            return;

        var mayAdvance = await MayAdvanceAsync(context, cancellationToken);
        var store = new EfSchemaFinalizationStore(context, _time);
        var records = new Dictionary<string, SchemaFinalizationRecord>(StringComparer.Ordinal);
        foreach (var chain in Families.Chains)
        {
            if (await store.FindAsync(chain.Family, cancellationToken) is { } record)
                records[chain.Family] = record;
            else
                _logger.LogError(
                    "Schema family {Family} of EF module {Module} has no finalization record although it had one when the module " +
                    "activated. No surface removes a record; this host keeps writing what it last observed.", chain.Family, Module);
        }

        Adopt(identity, records, mayAdvance);
    }

    /// <summary>
    /// Re-reads <paramref name="chain"/>'s record because a row about to be rewritten is stamped later than the write
    /// version (spec 180, FR-015), on the context doing the write, and returns the write version it leaves.
    /// </summary>
    internal async ValueTask<EfSchemaFamilyWriteState> ConfirmAsync(DbContext context, EfSchemaChain chain, bool synchronous, CancellationToken cancellationToken)
    {
        var store = new EfSchemaFinalizationStore(context, _time);
        var record = synchronous ? store.Find(chain.Family) : await store.FindAsync(chain.Family, cancellationToken);
        if (record is not null && DatabaseIdentity is { } identity)
            Adopt(identity, new Dictionary<string, SchemaFinalizationRecord> { [chain.Family] = record }, MayAdvanceNow());
        return StateOf(chain.Family) ?? throw new InvalidOperationException($"EF module '{Module}' has not been admitted by its finalization gate.");
    }

    /// <summary>Every family's status in this database (FR-022), with the counted members that cannot read each pending version.</summary>
    public async Task<IReadOnlyList<EfSchemaFamilyStatus>> ReadStatusAsync(DbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var store = new EfSchemaFinalizationStore(context, _time);
        var statuses = new List<EfSchemaFamilyStatus>(Families.Chains.Count);
        foreach (var chain in Families.Chains)
        {
            var record = await store.FindAsync(chain.Family, cancellationToken);
            var blockers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (record is not null && _fleet is not null && chain.IsReadable(record.FinalizedVersion))
            {
                foreach (var version in chain.ReadableVersions.SkipWhile(version => version != record.FinalizedVersion).Skip(1))
                {
                    try
                    {
                        blockers[version] = (await _fleet.CountAsync(chain.Family, version, record.DatabaseIdentity, cancellationToken)).Blockers;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        blockers[version] = [$"the fleet could not be read: {exception.GetType().Name}"];
                    }
                }
            }

            var state = StateOf(chain.Family);
            statuses.Add(EfSchemaFamilyStatus.Describe(chain, record, blockers) with
            {
                WriteVersion = state?.WriteVersion,
                WritesRefused = state?.WritesRefused ?? false
            });
        }

        return statuses;
    }

    /// <summary>
    /// Runs evaluation and refresh until <paramref name="stopping"/> fires: refresh every refresh interval, evaluation
    /// every evaluation interval and whenever the fleet signals a change, each evaluation followed by a refresh
    /// (FR-005, FR-010). A failed round is logged and the next one follows on schedule.
    /// </summary>
    /// <param name="withContext">Runs its argument with a fresh context of the module, in a scope of its own.</param>
    public async Task RunAsync(Func<Func<DbContext, Task>, CancellationToken, Task> withContext, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(withContext);
        var nextEvaluation = _time.GetUtcNow() + _options.EvaluationInterval;
        var nextRefresh = _time.GetUtcNow() + _options.RefreshInterval;
        while (!stopping.IsCancellationRequested)
        {
            var now = _time.GetUtcNow();
            var due = nextEvaluation < nextRefresh ? nextEvaluation : nextRefresh;
            var changed = await WaitAsync(due - now, stopping);
            if (stopping.IsCancellationRequested)
                return;

            now = _time.GetUtcNow();
            var evaluate = changed || now >= nextEvaluation;
            try
            {
                await withContext(async context =>
                {
                    if (evaluate)
                        await EvaluateAsync(context, stopping);
                    await RefreshAsync(context, stopping);
                }, stopping);
            }
            catch (Exception exception) when (!stopping.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "The finalization gate of EF module {Module} could not evaluate or refresh; it tries again on schedule.", Module);
            }

            if (evaluate)
                nextEvaluation = now + _options.EvaluationInterval;
            nextRefresh = now + _options.RefreshInterval;
        }
    }

    /// <summary>Waits until <paramref name="delay"/> passes or the fleet signals a change, whichever is first; true for a change.</summary>
    private async Task<bool> WaitAsync(TimeSpan delay, CancellationToken stopping)
    {
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var changed = false;
        using var registration = _fleet?.GetChangeToken().RegisterChangeCallback(_ =>
        {
            changed = true;
            // A token that has already fired calls back at once; the wait below then returns without delay.
            try { linked.Cancel(); } catch (ObjectDisposedException) { }
        }, null);
        try
        {
            await Task.Delay(delay, _time, linked.Token);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return changed;
    }

    private async Task<Admission> AdmitAsync(DbContext context, PublishFirst publish, CancellationToken cancellationToken)
    {
        var names = Families.Chains.Select(chain => chain.Family).ToArray();
        _observations.BeginActivation(names);
        try
        {
            if (publish is not PublishFirst.No)
                await PublishBeforeReadAsync(publish is PublishFirst.WaitingUpToTheBound, cancellationToken);
            var member = LocalMember();
            var store = new EfSchemaFinalizationStore(context, _time);
            var identity = await store.GetOrCreateDatabaseIdentityAsync(cancellationToken);
            var records = new Dictionary<string, SchemaFinalizationRecord>(StringComparer.Ordinal);
            foreach (var chain in Families.Chains)
                records[chain.Family] = await AdmitAsync(store, chain, identity, member, cancellationToken);
            foreach (var (family, record) in records)
                _observations.Observe(family, identity, record.FinalizedVersion);
            return new Admission(identity, records);
        }
        finally
        {
            _observations.EndActivation(names);
        }
    }

    /// <summary>One family's admission: its record, created when missing, once nothing in it is unreadable to this host.</summary>
    private async Task<SchemaFinalizationRecord> AdmitAsync(
        EfSchemaFinalizationStore store,
        EfSchemaChain chain,
        string identity,
        SchemaFinalizationMember member,
        CancellationToken cancellationToken)
    {
        var readable = chain.ReadableVersions;
        // A database with no record: every row it holds carries the oldest version this host reads (spec 181, Edge Cases).
        var record = await store.GetOrCreateAsync(chain.Family, readable[0], readable, SchemaFinalizationActor.Of(member), cancellationToken);
        var deadline = _time.GetUtcNow() + _options.IntentWaitBound;
        while (true)
        {
            if (!chain.IsReadable(record.FinalizedVersion))
                throw Refuse(chain, EfSchemaActivationRefusal.FinalizedUnreadable, record.FinalizedVersion);
            if (record.Finish is { } finish && !chain.IsReadable(finish.CompletionVersion))
                throw Refuse(chain, EfSchemaActivationRefusal.CompletionUnreadable, finish.CompletionVersion);
            if (record.Intent is not { } intent || chain.IsReadable(intent.Version))
                return record;

            // FR-014: an intent to finalize a version this host cannot read. This host is a counted member that cannot
            // read it, so resolving it abandons it (FR-008); if the evaluator commits first, the next read refuses.
            await ResolveAsync(store, chain, record, identity, member, cancellationToken);
            var reread = await store.FindAsync(chain.Family, cancellationToken)
                         ?? throw new InvalidOperationException($"The finalization record of '{chain.Family}' vanished while EF module '{Module}' was activating.");
            if (reread.Intent is { } still && !chain.IsReadable(still.Version) && reread.Revision == record.Revision)
            {
                if (_time.GetUtcNow() >= deadline)
                    throw Refuse(chain, EfSchemaActivationRefusal.IntentUnresolved, still.Version);
                await Task.Delay(_options.IntentPollInterval, _time, cancellationToken);
                reread = await store.FindAsync(chain.Family, cancellationToken) ?? reread;
            }

            record = reread;
        }
    }

    /// <summary>
    /// Confirms the intent <paramref name="record"/> carries (FR-007, FR-008): the fleet is read fresh now, after the
    /// intent is durable, and the intent is committed only if every counted member reads its version and no hold applies,
    /// by compare-and-set against the revision that carries it. Otherwise it is abandoned. A lost compare-and-set means
    /// another member resolved it or the record moved, and is not an error.
    /// </summary>
    private async Task ResolveAsync(
        EfSchemaFinalizationStore store,
        EfSchemaChain chain,
        SchemaFinalizationRecord record,
        string identity,
        SchemaFinalizationMember member,
        CancellationToken cancellationToken)
    {
        var intent = record.Intent ?? throw new ArgumentException("The record carries no intent to resolve.", nameof(record));
        string reason;
        if (!chain.IsReadable(intent.Version))
            reason = $"{member} cannot read '{intent.Version}'; it reads [{string.Join(", ", chain.ReadableVersions)}].";
        else if (record.IsHeld(intent.Version, chain.ReadableVersions))
            reason = $"a hold applies to '{intent.Version}'.";
        else if (_fleet is null)
            return; // Nothing here can count the fleet, so a member that can is left to confirm it.
        else
        {
            EfSchemaFleetAnswer answer;
            try
            {
                answer = await _fleet.CountAsync(chain.Family, intent.Version, identity, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // No answer is no confirmation. The intent stays for a later round, or another member, to resolve.
                _logger.LogWarning(exception, "Schema family {Family} could not confirm its intent to finalize {Version}: the fleet could not be read.", chain.Family, intent.Version);
                return;
            }

            if (answer.EveryCountedMemberReads)
            {
                try
                {
                    var committed = await store.CommitIntentAsync(chain.Family, record.Revision, chain.ReadableVersions, member, cancellationToken);
                    if (committed.Applied)
                        _logger.LogInformation("Schema family {Family} of EF module {Module} finalized {Version}.", chain.Family, Module, intent.Version);
                    return;
                }
                catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.Held)
                {
                    reason = $"a hold applies to '{intent.Version}'.";
                }
            }
            else
                reason = $"counted members cannot read '{intent.Version}': {string.Join("; ", answer.Blockers)}";
        }

        try
        {
            await store.AbandonIntentAsync(chain.Family, record.Revision, member, reason, cancellationToken);
        }
        catch (SchemaFinalizationRefusedException refusal) when (refusal.Refusal == SchemaFinalizationRefusal.NoIntent)
        {
            // Resolved by someone else between the read and this write.
        }
    }

    /// <summary>
    /// The newest version this host reads above the finalized one that no hold keeps and every counted member reads, or
    /// null. It never passes this host's own current version, and several versions may finalize in one step.
    /// </summary>
    private async Task<string?> ChooseAsync(EfSchemaChain chain, SchemaFinalizationRecord record, string identity, CancellationToken cancellationToken)
    {
        var readable = chain.ReadableVersions;
        var finalizedAt = SchemaVersionChain.PositionOf(readable, record.FinalizedVersion);
        for (var position = readable.Count - 1; position > finalizedAt; position--)
        {
            var version = readable[position];
            if (record.IsHeld(version, readable))
                continue;
            if ((await _fleet!.CountAsync(chain.Family, version, identity, cancellationToken)).EveryCountedMemberReads)
                return version;
        }

        return null;
    }

    /// <summary>Records what the gate read. A newer finalized version is adopted only when <paramref name="mayAdvance"/>.</summary>
    private void Adopt(string identity, IReadOnlyDictionary<string, SchemaFinalizationRecord> records, bool mayAdvance)
    {
        lock (_lock)
        {
            _databaseIdentity = identity;
            var states = new Dictionary<string, EfSchemaFamilyWriteState>(_states, StringComparer.Ordinal);
            foreach (var chain in Families.Chains)
            {
                if (!records.TryGetValue(chain.Family, out var record))
                    continue;
                var previous = states.GetValueOrDefault(chain.Family);
                var next = Next(chain, record, previous, mayAdvance);
                states[chain.Family] = next;
                if (next.WritesRefused && previous is not { WritesRefused: true })
                    _logger.LogError(
                        "Schema family {Family} of EF module {Module} is finalized at {Finalized}, which this host cannot read (it reads {Readable}). " +
                        "Every write to the family is refused (spec 181, FR-012).",
                        chain.Family, Module, record.FinalizedVersion, string.Join(", ", chain.ReadableVersions));
                _observations.Observe(chain.Family, identity, next.WriteVersion);
            }

            _states = states;
        }
    }

    private static EfSchemaFamilyWriteState Next(EfSchemaChain chain, SchemaFinalizationRecord record, EfSchemaFamilyWriteState? previous, bool mayAdvance)
    {
        // A refusal is final for this activation: a finalized version only moves forward, so it never becomes readable again.
        if (previous is { WritesRefused: true })
            return previous;
        if (!chain.IsReadable(record.FinalizedVersion))
            return new EfSchemaFamilyWriteState(previous?.WriteVersion ?? chain.ReadableVersions[0], record.FinalizedVersion, chain.ReadableVersions);
        if (previous is null || mayAdvance)
            return new EfSchemaFamilyWriteState(record.FinalizedVersion, null, chain.ReadableVersions);
        return previous;
    }

    /// <summary>
    /// Whether this member may adopt a newer finalized version now (FR-018). A member whose incarnation changed since it
    /// was admitted is admitted again, publish first; a refusal then refuses the family's writes rather than the module,
    /// which is already active.
    /// </summary>
    private async Task<bool> MayAdvanceAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (_fleet is null)
            return true;
        var standing = _fleet.GetLocalStanding();
        if (standing.HasLapsed)
            return false;
        if (IsCounted(standing))
            return true;

        try
        {
            // One attempt: a member that cannot publish yet, such as one that has not joined, tries again next refresh.
            var admitted = await AdmitAsync(context, PublishFirst.Once, cancellationToken);
            Adopt(admitted.Identity, admitted.Records, mayAdvance: true);
            lock (_lock)
                _admittedIncarnation = standing.Member.Incarnation;
            _logger.LogInformation("EF module {Module} was admitted again by its finalization gate as {Member}.", Module, standing.Member);
            await PublishQuietlyAsync(cancellationToken);
            return true;
        }
        catch (EfSchemaActivationRefusedException refusal)
        {
            // Too late to refuse the module; refuse its writes to the family instead (FR-012). An unresolved intent or an
            // unpublished report refuses nothing yet: the next refresh admits again.
            if (refusal.Refusal is EfSchemaActivationRefusal.FinalizedUnreadable && refusal.Version is { } finalized)
                RefuseWrites(refusal.Family, finalized);
            _logger.LogWarning(refusal, "EF module {Module} could not be admitted again by its finalization gate; it adopts no newer finalized version.", Module);
            return false;
        }
    }

    private bool MayAdvanceNow() => _fleet is null || IsCounted(_fleet.GetLocalStanding());

    private bool IsCounted(EfSchemaFleetStanding standing)
    {
        lock (_lock)
            return !standing.HasLapsed && _admittedIncarnation is not null && standing.Member.Incarnation == _admittedIncarnation;
    }

    private void RefuseWrites(string family, string finalizedVersion)
    {
        lock (_lock)
        {
            var chain = Families.Chains.First(candidate => candidate.Family == family);
            var previous = _states.GetValueOrDefault(family);
            _states = new Dictionary<string, EfSchemaFamilyWriteState>(_states, StringComparer.Ordinal)
            {
                [family] = new(previous?.WriteVersion ?? chain.ReadableVersions[0], finalizedVersion, chain.ReadableVersions)
            };
        }
    }

    private async Task PublishBeforeReadAsync(bool wait, CancellationToken cancellationToken)
    {
        if (_fleet is null)
            return;
        var deadline = wait ? _time.GetUtcNow() + _options.IntentWaitBound : _time.GetUtcNow();
        while (true)
        {
            try
            {
                await _fleet.PublishAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (_time.GetUtcNow() >= deadline)
                {
                    var chain = Families.Chains[0];
                    throw new EfSchemaActivationRefusedException(Module, chain.Family, EfSchemaActivationRefusal.ReportNotPublished, null, chain.ReadableVersions);
                }

                _logger.LogDebug(exception, "EF module {Module} waits to publish this host's readability report before reading its finalization records.", Module);
                await Task.Delay(_options.IntentPollInterval, _time, cancellationToken);
            }
        }
    }

    private async Task PublishQuietlyAsync(CancellationToken cancellationToken)
    {
        if (_fleet is null)
            return;
        try
        {
            await _fleet.PublishAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The report now names what this host read; until a later publish or heartbeat carries it, the entry names
            // no database, which counts this host everywhere: only ever later, never earlier.
            _logger.LogDebug(exception, "EF module {Module} could not publish the database identity it read; a later publish carries it.", Module);
        }
    }

    private SchemaFinalizationMember LocalMember() =>
        _fleet?.GetLocalStanding().Member ?? new SchemaFinalizationMember(Environment.MachineName, ProcessIncarnation);

    private EfSchemaActivationRefusedException Refuse(EfSchemaChain chain, EfSchemaActivationRefusal refusal, string version) =>
        new(Module, chain.Family, refusal, version, chain.ReadableVersions);

    private sealed record Admission(string Identity, IReadOnlyDictionary<string, SchemaFinalizationRecord> Records);

    private enum PublishFirst
    {
        No,
        Once,
        WaitingUpToTheBound
    }
}

/// <summary>
/// What a host writes for one family: <see cref="WriteVersion"/>, the finalized version it last adopted, unless
/// <see cref="UnreadableFinalizedVersion"/> is set, in which case it refuses every write to the family (FR-012).
/// </summary>
public sealed record EfSchemaFamilyWriteState(string WriteVersion, string? UnreadableFinalizedVersion, IReadOnlyList<string> ReadableVersions)
{
    public bool WritesRefused => UnreadableFinalizedVersion is not null;
}
