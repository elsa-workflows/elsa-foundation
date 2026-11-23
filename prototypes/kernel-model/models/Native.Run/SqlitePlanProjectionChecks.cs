using Kernel.Foundation.InMemory;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class SqlitePlanProjectionChecks
{
    private static readonly Contract.BoundScope Scope = new("migration");
    private const string OtherPlan = "second-plan-read-fixture";

    internal static async Task<int> BuildAsync(string file)
    {
        if (File.Exists(file)) throw new InvalidOperationException("Choose a new database file for this fixture.");
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stop = timer.Token;
        const long initial = 1_706_000_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var publisher = new Publisher();
        Contract.IIngress ingress = new InMemoryIngress(store, new Requests("plan"), "plan");
        Contract.IActorReads source = new ActorList(store, 32);
        var worker = new Worker("plan-owner", 0, store, new ActorAdapter(store, publisher),
            new WorkerOptions { Monotonic = () => now - initial });
        await using var owner = await new DurableHost([worker]).ActivateAsync(0, stop)
            ?? throw new InvalidOperationException("The journal producer could not activate.");
        var plan = await StartAsync("plan");
        var migrated = await StartAsync("migrated");
        var refusedActor = await StartAsync("refused");
        var unanswered = await StartAsync("unanswered");
        var extra = await StartAsync("extra");
        var projection = new SqlitePlanProjection(file, Scope, plan);
        await projection.InitializeAsync(stop);
        await RecordAsync("selection", new MigrationPlanJournal.Selected([migrated, refusedActor, unanswered]));

        // Fail during the row writes, after the cursor UPDATE and the first row INSERT have run.
        await ExecuteAsync("""
            CREATE TRIGGER abort_projection BEFORE INSERT ON plan_outcomes
            WHEN EXISTS (SELECT 1 FROM plan_outcomes)
            BEGIN SELECT RAISE(ABORT, 'projection rollback control'); END;
            """);
        var refused = false;
        try { await projection.ProjectNextAsync(source, stop); }
        catch (SqliteException error) when (error.SqliteErrorCode == 19 && error.Message.Contains("projection rollback control", StringComparison.Ordinal))
        {
            refused = true;
        }
        Check(refused && await projection.BookmarkAsync(stop) is null && await PersistedRowCountAsync() == 0 &&
            (await projection.ReadPageAsync(null, 4, stop)) is { AsOf: 0, Rows.Count: 0 },
            "A failure after the first row write must roll back the page and its cursor.");
        await ExecuteAsync("DROP TRIGGER abort_projection");

        var secondWriter = new SqlitePlanProjection(file, Scope, plan);
        var shared = new TailWitness(source, synchronize: true);
        var race = await Task.WhenAll(projection.ProjectNextAsync(shared, stop), secondWriter.ProjectNextAsync(shared, stop));
        Check(race.Count(applied => applied) == 1 && await projection.BookmarkAsync(stop) is not null &&
            (await secondWriter.ReadPageAsync(null, 4, stop)).Rows.Count == 3,
            "Two projectors with the same input page must publish it only once.");

        await AnswerAsync(migrated, "not-at-source", MigrationPlanOutcome.NotAtSource);
        await RecordAsync("close", new MigrationPlanJournal.Closed());
        while (await projection.ProjectNextAsync(source, stop)) { }
        var saved = await projection.BookmarkAsync(stop);
        var closed = await projection.ReadPageAsync(null, 4, stop);
        Check(saved is not null && closed.Closed && closed.Rows.Count(row => row.Result is null) == 2,
            "A reopened projection needs a durable close and bookmark.");

        now++;
        await RecordAsync("late-batch", new MigrationPlanJournal.Selected([extra]));
        await AnswerAsync(migrated, "late-migrated", MigrationPlanOutcome.Migrated);
        await AnswerAsync(refusedActor, "late-refused", MigrationPlanOutcome.Refused);
        await AnswerAsync(migrated, "repeat-migrated", MigrationPlanOutcome.NotAtSource);
        var reopened = new TailWitness(source);
        while (await secondWriter.ProjectNextAsync(reopened, stop)) { }
        Check(reopened.First == saved && await secondWriter.BookmarkAsync(stop) != saved,
            "The new projector must read from the stored bookmark and advance it.");
        var lastBookmark = await secondWriter.BookmarkAsync(stop);
        now++;
        Check(!await secondWriter.ProjectNextAsync(source, stop) && await secondWriter.BookmarkAsync(stop) == lastBookmark &&
            (await secondWriter.ReadPageAsync(null, 4, stop)).AsOf == now,
            "An empty source page advances the observed clock while retaining the bookmark.");

        // The view can be backed by any foundation. Exercise coincident plan refs across tenants in the physical reader.
        await AddNeighborAsync("other", plan.Id, extra);
        await AddNeighborAsync(Scope.Tenant, OtherPlan, extra);
        var results = await VerifyAsync(file, plan, stop);
        Check(results[migrated] == MigrationPlanOutcome.Migrated && results[refusedActor] == MigrationPlanOutcome.Refused &&
            results[unanswered] is null && !results.ContainsKey(extra), "The rows must keep the original selected actors and exact senders.");
        Console.WriteLine($"projected {Environment.ProcessId} {plan.Id}");
        return 0;

        async Task<Contract.ActorRef> StartAsync(string id)
        {
            var birth = await ingress.SendAsync(Scope, new Contract.Target.Start(new(publisher.Type, id)), id,
                Contract.Kinds.Start, cancellationToken: stop);
            return birth.Actor ?? throw new InvalidOperationException("No actor reference was issued.");
        }

        Task RecordAsync<T>(string operation, T entry) => SendAsync(plan, operation, "record", Json.Write(entry));
        Task AnswerAsync(Contract.ActorRef actor, string operation, MigrationPlanOutcome outcome) =>
            SendAsync(actor, operation, "send-answer", Json.Write(new AnswerInput(plan, outcome)));

        async Task SendAsync(Contract.ActorRef actor, string operation, string kind, string input)
        {
            var sent = await ingress.SendAsync(Scope, new Contract.Target.Ref(actor), operation, kind, input, cancellationToken: stop);
            Check(sent.Outcome == Contract.AdmissionOutcome.Accepted, "The fixture must admit the journal input.");
            for (var pass = 0; pass < 6; pass++)
            {
                await owner.WokenAsync(null, stop);
                await owner.RunAsync(stop);
            }
        }

        async Task<long> PersistedRowCountAsync()
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
            await connection.OpenAsync(stop);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT COUNT(*) FROM plan_cursors WHERE tenant = $tenant AND plan = $plan)
                    + (SELECT COUNT(*) FROM plan_outcomes WHERE tenant = $tenant AND plan = $plan)
                """;
            command.Parameters.AddWithValue("$tenant", Scope.Tenant);
            command.Parameters.AddWithValue("$plan", plan.Id);
            return (long)(await command.ExecuteScalarAsync(stop))!;
        }

        async Task AddNeighborAsync(string tenant, string planRef, Contract.ActorRef actor)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWrite }.ConnectionString);
            await connection.OpenAsync(stop);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO plan_cursors VALUES ($tenant, $plan, NULL, 0, $asOf);
                INSERT INTO plan_outcomes VALUES ($tenant, $plan, $actor, NULL);
                """;
            command.Parameters.AddWithValue("$tenant", tenant);
            command.Parameters.AddWithValue("$plan", planRef);
            command.Parameters.AddWithValue("$asOf", now);
            command.Parameters.AddWithValue("$actor", actor.Id);
            await command.ExecuteNonQueryAsync(stop);
        }

        async Task ExecuteAsync(string sql)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ConnectionString);
            await connection.OpenAsync(stop);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(stop);
        }
    }

    internal static async Task<int> InspectAsync(string file, string reference)
    {
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var plan = new Contract.ActorRef(reference);
        await VerifyAsync(file, plan, timer.Token);
        Console.WriteLine($"inspected {Environment.ProcessId} {plan.Id} migrated 1 refused 1 no-answer 1");
        return 0;
    }

    private static async Task<Dictionary<Contract.ActorRef, MigrationPlanOutcome?>> VerifyAsync(string file, Contract.ActorRef plan, CancellationToken stop)
    {
        var projection = new SqlitePlanProjection(file, Scope, plan);
        var first = await projection.ReadPageAsync(null, 1, stop);
        var second = await projection.ReadPageAsync(first.After, 1, stop);
        var last = await projection.ReadPageAsync(second.After, 1, stop);
        Check(first is { Closed: true, AsOf: > 0, After: not null, Rows.Count: 1 } &&
            second is { Closed: true, After: not null, Rows.Count: 1 } &&
            last is { Closed: true, After: null, Rows.Count: 1 } &&
            first.AsOf == second.AsOf && second.AsOf == last.AsOf &&
            first.After == first.Rows[0].Actor && second.After == second.Rows[0].Actor,
            "A read-only inspector must see one stable, closed result over three keyset pages.");
        var results = first.Rows.Concat(second.Rows).Concat(last.Rows).ToDictionary(row => row.Actor, row => row.Result);
        Check(results.Values.Count(outcome => outcome == MigrationPlanOutcome.Migrated) == 1 &&
            results.Values.Count(outcome => outcome == MigrationPlanOutcome.Refused) == 1 &&
            results.Values.Count(outcome => outcome is null) == 1,
            "The closed plan must report exactly one migrated, refused and unanswered actor.");
        var foreign = new SqlitePlanProjection(file, new("other"), plan);
        var unrelated = new SqlitePlanProjection(file, Scope, new(OtherPlan));
        var foreignPage = await foreign.ReadPageAsync(null, 2, stop);
        var otherPlanPage = await unrelated.ReadPageAsync(null, 2, stop);
        Check(foreignPage is { After: null, Rows: [{ Result: null }], Closed: false } &&
            otherPlanPage is { After: null, Rows: [{ Result: null }], Closed: false } &&
            !results.ContainsKey(foreignPage.Rows[0].Actor) && !results.ContainsKey(otherPlanPage.Rows[0].Actor),
            "The same physical database must isolate different tenants and plans.");
        return results;
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private sealed class TailWitness(Contract.IActorReads inner, bool synchronize = false) : Contract.IActorReads
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        internal Contract.PageToken? First { get; private set; }

        public async ValueTask<Contract.Page<Contract.JournalEntry>> TailAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default)
        {
            var page = await inner.TailAsync(scope, actor, from, cancellationToken);
            var call = Interlocked.Increment(ref _calls);
            if (call == 1) First = from;
            if (synchronize)
            {
                if (call == 2) _both.TrySetResult();
                await _both.Task.WaitAsync(cancellationToken);
            }
            return page;
        }

        public ValueTask<Contract.ActorView?> GetAsync(Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken cancellationToken = default) =>
            inner.GetAsync(scope, actor, cancellationToken);

        public ValueTask<Contract.Page<Contract.ActorView>> ListAsync(Contract.BoundScope scope, Contract.ActorQuery query,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            inner.ListAsync(scope, query, from, cancellationToken);

        public ValueTask<Contract.Page<Contract.DeliveryView>> InboxAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            inner.InboxAsync(scope, actor, from, cancellationToken);
    }

    private sealed record AnswerInput(Contract.ActorRef Plan, MigrationPlanOutcome Outcome);

    private sealed class Publisher : Contract.IActorType
    {
        public string Type => "plan-projection-fixture";

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var input in turn.Inbox)
            {
                if (input.Kind == "record") turn.Record(Json.Read<System.Text.Json.JsonElement>(input.Payload!));
                if (input.Kind == "send-answer")
                {
                    var answer = Json.Read<AnswerInput>(input.Payload!);
                    turn.Send(answer.Plan, "answer", Json.Write(answer.Outcome));
                }
                if (input.Kind == "answer" && input.Sender is { } sender)
                    turn.Record(new MigrationPlanJournal.Answered(sender, Json.Read<MigrationPlanOutcome>(input.Payload!)));
            }
            return ValueTask.CompletedTask;
        }

        public bool Withholds(Contract.ActorHeader header) => false;
        public bool ProtectsDeletion(Contract.ActorHeader header, string? state, long now) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string? state) => 0;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string? state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
    }
}
