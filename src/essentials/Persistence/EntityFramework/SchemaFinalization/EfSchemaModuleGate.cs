using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Elsa.Persistence.Schema.SchemaFinalization;

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
/// <item><b>Activation refuses an unreadable finalized version</b> (FR-015), and an unreadable completion version. A
/// record it creates starts no lower than the version any contracting migration applied to the database names, since the
/// schema serves nothing below that, and a version this host cannot place refuses the module
/// (<see cref="EfContractingMigrationCheck.SeedVersionAsync"/>, spec 185 FR-023).</item>
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
public sealed class EfSchemaModuleGate : IEfSchemaModuleGate
{
    /// <summary>This process's incarnation, for the member a host that composes no fleet, or the persistence tool, names itself by.</summary>
    internal static readonly string ProcessIncarnation = Guid.NewGuid().ToString("N");

    private readonly IEfSchemaFleet? _fleet;
    private readonly EfSchemaFinalizationObservations _observations;
    private readonly EfSchemaFinalizationOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly bool _publishBeforeRead;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _onDemand = new(1, 1);
    private Dictionary<string, EfSchemaFamilyWriteState> _states = new(StringComparer.Ordinal);
    private Dictionary<string, (SchemaFinalizationRecord Record, DateTimeOffset At)> _observed = new(StringComparer.Ordinal);
    private string? _databaseIdentity;
    private string? _admittedIncarnation;
    private bool _deactivated;
    private DateTimeOffset _refreshedAt = DateTimeOffset.MinValue;
    private Func<Func<DbContext, Task>, CancellationToken, Task>? _withContext;
    private IEfSchemaBackfillStatusSource? _backfill;

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
    /// The record of <paramref name="family"/> as this host last read it, and when, or null before the module has been
    /// admitted: the holds, intent and finish record behind the write version, so spec 182's shared dormancy check can say
    /// why a version is not available yet without reading the record again (spec 182, FR-003; spec 186, FR-017).
    /// </summary>
    public (SchemaFinalizationRecord Record, DateTimeOffset At)? ObservedRecordOf(string family)
    {
        lock (_lock)
            return _observed.TryGetValue(family, out var observed) ? observed : null;
    }

    /// <inheritdoc />
    public EfSchemaFamilyStatus? Observe(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return Families.Chains.FirstOrDefault(chain => StringComparer.Ordinal.Equals(chain.Family, family)) is { } owned ? Observe(owned) : null;
    }

    /// <inheritdoc />
    public IReadOnlyList<EfSchemaFamilyStatus> Observe() => Families.Chains.Select(Observe).ToArray();

    /// <summary>The family's status from what this gate last read and what it writes, with no I/O.</summary>
    private EfSchemaFamilyStatus Observe(EfSchemaChain chain)
    {
        var observed = ObservedRecordOf(chain.Family);
        return Status(chain, observed?.Record, observed?.At);
    }

    /// <summary><paramref name="record"/> described against <paramref name="chain"/>, with what this host writes for the family.</summary>
    private EfSchemaFamilyStatus Status(
        EfSchemaChain chain,
        SchemaFinalizationRecord? record,
        DateTimeOffset? observedAt,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? blockers = null)
    {
        var state = StateOf(chain.Family);
        return EfSchemaFamilyStatus.Describe(chain.Family, chain.Module, chain.ReadableVersions, record, blockers) with
        {
            WriteVersion = state?.WriteVersion,
            WritesRefused = state?.WritesRefused ?? false,
            ObservedAt = observedAt,
            Backfill = _backfill?.StatusOf(chain.Family)
        };
    }

    /// <summary>
    /// Makes this gate's status carry what <paramref name="source"/> reports of each family's post-finalization backfill
    /// (spec 186, FR-021). The module's backfill supplies it when it is built over this gate; the gate knows the backfill
    /// through nothing else.
    /// </summary>
    internal void ReportBackfillFrom(IEfSchemaBackfillStatusSource source) =>
        _backfill = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// Gives the gate a way to open a fresh context of its module on demand, as <see cref="RunAsync"/>'s argument does, so
    /// <see cref="RefreshIfOlderThanAsync"/> and <see cref="ReadStatusAsync(CancellationToken)"/> can serve a caller that
    /// has none. The module's migrator supplies it before it registers the gate.
    /// </summary>
    public void UseContexts(Func<Func<DbContext, Task>, CancellationToken, Task> withContext)
    {
        ArgumentNullException.ThrowIfNull(withContext);
        _withContext = withContext;
    }

    /// <summary>
    /// Refreshes (FR-010) unless the last refresh was less than <paramref name="maxAge"/> ago, which is spec 182's FR-014:
    /// before a dormancy refusal, a host re-reads what may have finalized since. Concurrent callers share one refresh, so a
    /// burst of refused requests costs at most one read per <paramref name="maxAge"/>. Returns whether it read the records.
    /// Nothing is read before the module has been admitted, or when the gate has no way to open a context.
    /// </summary>
    public async Task<bool> RefreshIfOlderThanAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        if (_withContext is not { } withContext || DatabaseIdentity is null || !IsOlderThan(maxAge))
            return false;

        await _onDemand.WaitAsync(cancellationToken);
        try
        {
            // Another caller may have refreshed while this one waited.
            if (!IsOlderThan(maxAge))
                return false;
            await withContext(context => RefreshAsync(context, cancellationToken), cancellationToken);
            return true;
        }
        finally
        {
            _onDemand.Release();
        }
    }

    /// <summary>
    /// Every family's status (FR-022), read now on a fresh context of the module, for a caller that has none. Empty when the
    /// gate has no way to open one.
    /// </summary>
    public async Task<IReadOnlyList<EfSchemaFamilyStatus>> ReadStatusAsync(CancellationToken cancellationToken = default)
    {
        if (_withContext is not { } withContext)
            return [];
        IReadOnlyList<EfSchemaFamilyStatus> statuses = [];
        await withContext(async context => statuses = await ReadStatusAsync(context, cancellationToken), cancellationToken);
        return statuses;
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
        // A refused admission throws here, and was never active. One that is admitted is active from the end of its
        // admission, before the write version below is visible, the first moment this gate lets a row be written.
        var admitted = await AdmitAsync(context, _publishBeforeRead ? PublishFirst.WaitingUpToTheBound : PublishFirst.No, cancellationToken);
        try
        {
            Adopt(admitted.Identity, admitted.Records, mayAdvance: true);
            lock (_lock)
                _admittedIncarnation = _publishBeforeRead ? standing?.Member.Incarnation : null;

            await PublishQuietlyAsync(cancellationToken);
            try
            {
                await EvaluateAsync(context, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The module is admitted: what it may write is settled. An evaluation that cannot read the fleet now only
                // delays finalization to the next round, so it does not refuse the activation.
                _logger.LogWarning(exception, "EF module {Module} was admitted, but its first evaluation failed; the next round tries again.", Module);
            }

            await RefreshAsync(context, cancellationToken);
        }
        catch
        {
            // An activation that fails or is cancelled leaves no module for the migrator to stop, so nothing else would
            // ever end the activity reported above.
            Deactivate();
            throw;
        }
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
        // One read of every family's record, since nearly every round finds nothing to do.
        var current = await store.ListAsync(cancellationToken);
        foreach (var chain in Families.Chains)
        {
            if (StateOf(chain.Family) is { WritesRefused: true })
                continue;
            if (!current.TryGetValue(chain.Family, out var record) || !chain.IsReadable(record.FinalizedVersion))
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
        var current = await store.ListAsync(cancellationToken);
        var records = new Dictionary<string, SchemaFinalizationRecord>(StringComparer.Ordinal);
        foreach (var chain in Families.Chains)
        {
            if (current.TryGetValue(chain.Family, out var record))
                records[chain.Family] = record;
            else
                _logger.LogError(
                    "Schema family {Family} of EF module {Module} has no finalization record although it had one when the module " +
                    "activated. No surface removes a record; this host keeps writing what it last observed.", chain.Family, Module);
        }

        // The readability report carries the version this host writes (spec 183, FR-019), so a change is published now
        // rather than left to a provider that publishes only when asked.
        var changed = Adopt(identity, records, mayAdvance);
        lock (_lock)
            _refreshedAt = _time.GetUtcNow();
        if (changed)
            await PublishQuietlyAsync(cancellationToken);
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

            statuses.Add(Status(chain, record, ObservedRecordOf(chain.Family)?.At, blockers));
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
            catch (Exception) when (stopping.IsCancellationRequested)
            {
                // Stopping, as the shell or host is: whatever the round was doing is abandoned, not failed.
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                // An arbitrary store or provider failure must not kill the loop; it is logged and the next round tries
                // again on schedule. OperationCanceledException reaching here (stopping not requested) and
                // OutOfMemoryException are not this round's business to swallow.
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
            try
            {
                linked.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The wait already returned and disposed the linked source; the change is still recorded above,
                // so the caller sees it on its next call. Nothing else to do.
                _logger.LogDebug("The finalization gate of EF module {Module} saw a fleet change after its wait had already ended.", Module);
            }
        }, null);
        try
        {
            await Task.Delay(delay, _time, linked.Token);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            // Not a stop: the fleet's change token fired and cancelled the linked source. `changed` already
            // carries that, so falling through to return it below is the whole handling.
            _logger.LogDebug("The finalization gate of EF module {Module} woke early because the fleet signalled a change.", Module);
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        return changed;
    }

    /// <summary>
    /// Ends what <see cref="ActivateAsync"/> reported: the module is no longer active in this host, as its report says from
    /// the next publish, and this gate records nothing more it reads. Its migrator calls it once the gate's loops have
    /// stopped, and <see cref="ActivateAsync"/> when it fails; a repeat changes nothing.
    /// </summary>
    internal void Deactivate()
    {
        // Set under the lock every recording takes, so nothing this gate reads afterwards, a refresh on demand among it,
        // brings back the observation the deactivation forgets.
        lock (_lock)
            _deactivated = true;
        _observations.Deactivate(this, FamilyNames());
    }

    /// <summary>Reports the module active in <paramref name="identity"/>, unless this gate has been deactivated, so an on-demand refresh after the stop cannot bring the activity back.</summary>
    private void MarkActive(string identity, string[] families)
    {
        lock (_lock)
        {
            if (!_deactivated)
                _observations.Activate(this, identity, families);
        }
    }

    /// <summary>Records what this gate read, unless it has been deactivated.</summary>
    private void RecordObservation(string family, string identity, string version)
    {
        lock (_lock)
        {
            if (!_deactivated)
                _observations.Observe(family, identity, version);
        }
    }

    private string[] FamilyNames() => Families.Chains.Select(chain => chain.Family).ToArray();

    private async Task<Admission> AdmitAsync(DbContext context, PublishFirst publish, CancellationToken cancellationToken)
    {
        var names = FamilyNames();
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
                records[chain.Family] = await AdmitAsync(context, store, chain, identity, member, cancellationToken);
            foreach (var (family, record) in records)
                RecordObservation(family, identity, record.FinalizedVersion);
            // Before the activation stops being pending, so the report never has a moment in which the module is neither.
            MarkActive(identity, names);
            return new Admission(identity, records);
        }
        finally
        {
            _observations.EndActivation(names);
        }
    }

    /// <summary>One family's admission: its record, created when missing, once nothing in it is unreadable to this host.</summary>
    private async Task<SchemaFinalizationRecord> AdmitAsync(
        DbContext context,
        EfSchemaFinalizationStore store,
        EfSchemaChain chain,
        string identity,
        SchemaFinalizationMember member,
        CancellationToken cancellationToken)
    {
        // A database with no record: every row it holds carries the oldest version this host reads (spec 181, Edge Cases),
        // and the record starts there, unless a contracting migration applied before any gate admitted the module left
        // the schema serving only a later version, which it starts at instead (spec 185, FR-023; #2136).
        var record = await store.FindAsync(chain.Family, cancellationToken)
                     ?? await store.GetOrCreateAsync(
                         chain.Family,
                         await EfContractingMigrationCheck.SeedVersionAsync(context, Module, chain, cancellationToken),
                         chain.ReadableVersions,
                         SchemaFinalizationActor.Of(member),
                         cancellationToken);
        var deadline = _time.GetUtcNow() + _options.IntentWaitBound;
        while (true)
        {
            if (EfSchemaFinalizationCheck.Refusal(Module, chain, record) is { } refusal)
                throw refusal;
            if (record.Intent is not { } intent || chain.IsReadable(intent.Version))
                return record;

            // FR-014: an intent to finalize a version this host cannot read. This host is a counted member that cannot
            // read it, so resolving it abandons it (FR-008); if the evaluator commits first, the next read refuses.
            await ResolveAsync(store, chain, record, identity, member, cancellationToken);
            var reread = await store.FindAsync(chain.Family, cancellationToken)
                         ?? throw new InvalidOperationException($"The finalization record of '{chain.Family}' vanished while EF module '{Module}' was activating.");
            if (reread.Intent is { } still && !chain.IsReadable(still.Version) && reread.Revision == record.Revision)
            {
                await WaitOrRefuseAsync(deadline, Refuse(chain, EfSchemaActivationRefusal.IntentUnresolved, still.Version), cancellationToken);
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

    /// <summary>
    /// Records what the gate read. A newer finalized version is adopted only when <paramref name="mayAdvance"/>. Returns
    /// whether what this host writes for any family changed.
    /// </summary>
    private bool Adopt(string identity, IReadOnlyDictionary<string, SchemaFinalizationRecord> records, bool mayAdvance)
    {
        lock (_lock)
        {
            var changed = false;
            _databaseIdentity = identity;
            var states = new Dictionary<string, EfSchemaFamilyWriteState>(_states, StringComparer.Ordinal);
            var observed = new Dictionary<string, (SchemaFinalizationRecord, DateTimeOffset)>(_observed, StringComparer.Ordinal);
            var at = _time.GetUtcNow();
            // Only a chain the caller actually read a record for is adopted; the rest keeps whatever it last wrote.
            foreach (var chain in Families.Chains.Where(chain => records.ContainsKey(chain.Family)))
            {
                var record = records[chain.Family];
                observed[chain.Family] = (record, at);
                var previous = states.GetValueOrDefault(chain.Family);
                var next = Next(chain, record, previous, mayAdvance);
                changed |= next != previous;
                states[chain.Family] = next;
                if (next.WritesRefused && previous is not { WritesRefused: true })
                    _logger.LogError(
                        "Schema family {Family} of EF module {Module} is finalized at {Finalized}, which this host cannot read (it reads {Readable}). " +
                        "Every write to the family is refused (spec 181, FR-012).",
                        chain.Family, Module, record.FinalizedVersion, string.Join(", ", chain.ReadableVersions));
                RecordObservation(chain.Family, identity, next.WriteVersion);
            }

            _states = states;
            _observed = observed;
            return changed;
        }
    }

    private bool IsOlderThan(TimeSpan maxAge)
    {
        lock (_lock)
            return _time.GetUtcNow() - _refreshedAt >= maxAge;
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
            // A member that has not joined yet, such as the membership module's own before its join, cannot publish; that
            // is expected until it joins, not worth a warning each round.
            _logger.Log(
                refusal.Refusal is EfSchemaActivationRefusal.ReportNotPublished ? LogLevel.Debug : LogLevel.Warning,
                refusal,
                "EF module {Module} could not be admitted again by its finalization gate; it adopts no newer finalized version.",
                Module);
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
                var poll = PollWithinBound(deadline, cancellationToken)
                           ?? throw new EfSchemaActivationRefusedException(
                               Module, Families.Chains[0].Family, EfSchemaActivationRefusal.ReportNotPublished, null, Families.Chains[0].ReadableVersions);
                _logger.LogDebug(exception, "EF module {Module} waits to publish this host's readability report before reading its finalization records.", Module);
                await poll;
            }
        }
    }

    /// <summary>
    /// One round of a wait bounded by <see cref="EfSchemaFinalizationOptions.IntentWaitBound"/>: the
    /// <see cref="EfSchemaFinalizationOptions.IntentPollInterval"/> to wait before trying again, or null once
    /// <paramref name="deadline"/> has passed, when the caller refuses instead.
    /// </summary>
    private Task? PollWithinBound(DateTimeOffset deadline, CancellationToken cancellationToken) =>
        _time.GetUtcNow() >= deadline ? null : Task.Delay(_options.IntentPollInterval, _time, cancellationToken);

    /// <summary>
    /// Waits one <see cref="EfSchemaFinalizationOptions.IntentPollInterval"/> if <paramref name="deadline"/> has not
    /// passed yet; once it has, throws <paramref name="refusal"/> instead of waiting.
    /// </summary>
    private Task WaitOrRefuseAsync(DateTimeOffset deadline, EfSchemaActivationRefusedException refusal, CancellationToken cancellationToken) =>
        _time.GetUtcNow() >= deadline ? throw refusal : Task.Delay(_options.IntentPollInterval, _time, cancellationToken);

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

    /// <summary>This process's member, as the record names it in history entries and claims.</summary>
    internal SchemaFinalizationMember LocalMember() =>
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
