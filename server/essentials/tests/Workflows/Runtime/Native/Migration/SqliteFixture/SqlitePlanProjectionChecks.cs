using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Kernel.Foundation.InMemory;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.Native.Migration.SqliteFixture;

internal static class SqlitePlanProjectionChecks
{
    private static Contract.BoundScope Scope => EmbeddedTenant.Bind("migration");
    private const string OtherPlan = "second-plan-read-fixture";
    private const string OpaqueReferencePlan = "opaque-reference-plan";

    internal static async Task<int> BuildAsync(string file)
    {
        if (File.Exists(file)) throw new InvalidOperationException("Choose a new database file for this fixture.");
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stop = timer.Token;
        const long initial = 1_706_000_000_000;
        var now = initial;
        var store = new InMemoryStore(1, () => now);
        var publisher = new Publisher();
        var fixture = new InMemoryActorFixture(store, "plan", 32);
        var ingress = fixture.Ingress;
        var source = fixture.Reads;
        var worker = new Worker("plan-owner", 0, store, fixture.Bind(publisher),
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
        var emptyPlan = await StartAsync("empty-plan");
        var empty = new SqlitePlanProjection(file, Scope, emptyPlan);
        await CheckCancelledReadAsync(empty, emptyPlan, source, expectEmpty: true);
        var emptyRetry = new TailWitness(source);
        Check(!await empty.ProjectNextAsync(emptyRetry, stop) && emptyRetry.Calls == 1 && emptyRetry.First is null &&
            await empty.BookmarkAsync(stop) is null &&
            (await new SqlitePlanProjection(file, Scope, emptyPlan).ReadPageAsync(null, 1, stop)).AsOf == now,
            "A first empty tail must persist its observed time with the null bookmark.");

        now++;
        var delayedEmpty = new TailWitness(source, afterFirstRead: async () =>
        {
            now++;
            await SendAsync(emptyPlan, "first-record", "record", Json.Write("progress"));
            Check(await empty.ProjectNextAsync(source, stop), "A competing projector must advance the null bookmark.");
        });
        Check(!await empty.ProjectNextAsync(delayedEmpty, stop) && delayedEmpty.Calls == 2 &&
            (await empty.ReadPageAsync(null, 1, stop)).AsOf == now,
            "An empty read that loses its bookmark must retry against the advanced cursor.");

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
        Check(refused && await projection.BookmarkAsync(stop) is null && await PersistedRowCountAsync(plan) == 0 &&
            (await projection.ReadPageAsync(null, 4, stop)) is { AsOf: 0, Rows.Count: 0 },
            "A failure after the first row write must roll back the page and its cursor.");
        await ExecuteAsync("DROP TRIGGER abort_projection");

        var secondWriter = new SqlitePlanProjection(file, Scope, plan);
        var shared = new TailWitness(source, synchronize: true);
        var race = await Task.WhenAll(projection.ProjectNextAsync(shared, stop), secondWriter.ProjectNextAsync(shared, stop));
        Check(race.Count(applied => applied) == 1 && await projection.BookmarkAsync(stop) is not null &&
            (await secondWriter.ReadPageAsync(null, 4, stop)).Rows.Count == 3,
            "Two projectors with the same input page must publish it only once.");

        var lastCaughtUp = (await projection.ReadPageAsync(null, 4, stop)).AsOf;
        now++;
        await AnswerAsync(migrated, "not-at-source", MigrationPlanOutcome.NotAtSource);
        await RecordAsync("close", new MigrationPlanJournal.Closed());
        var one = fixture.OpenReads(1);
        var beforeClose = await projection.BookmarkAsync(stop);
        await CheckCancelledReadAsync(projection, plan, one, expectEmpty: false);
        var closeRetry = new TailWitness(one);
        Check(await projection.ProjectNextAsync(closeRetry, stop) && beforeClose is not null &&
            closeRetry.First == beforeClose &&
            (await projection.ReadPageAsync(null, 4, stop)) is { Closed: false } partial && partial.AsOf == lastCaughtUp,
            "A committed close on a later page leaves the partial view at its last confirmed catch-up time.");
        while (await projection.ProjectNextAsync(source, stop)) { }
        var saved = await projection.BookmarkAsync(stop);
        var closed = await projection.ReadPageAsync(null, 4, stop);
        Check(saved is not null && closed.Closed && closed.AsOf == now && closed.Rows.Count(row => row.Result is null) == 2,
            "A reopened projection needs a durable close and bookmark.");

        now++;
        await RecordAsync("late-batch", new MigrationPlanJournal.Selected([extra]));
        await AnswerAsync(migrated, "late-migrated", MigrationPlanOutcome.Migrated);
        await AnswerAsync(refusedActor, "late-refused", MigrationPlanOutcome.Refused);
        await AnswerAsync(migrated, "repeat-migrated", MigrationPlanOutcome.NotAtSource);
        var reopened = new TailWitness(fixture.OpenReads(1));
        Check(await secondWriter.ProjectNextAsync(reopened, stop) &&
            (await new SqlitePlanProjection(file, Scope, plan).ReadPageAsync(null, 4, stop)).AsOf == closed.AsOf,
            "A partial catch-up must retain the previously confirmed time, including after reopening SQLite.");
        while (await secondWriter.ProjectNextAsync(reopened, stop)) { }
        Check((await secondWriter.ReadPageAsync(null, 4, stop)).AsOf == now,
            "The empty page after the late outcomes confirms the new catch-up time.");
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
        await AddNeighborAsync(Scope.Tenant, OpaqueReferencePlan, new("short"));
        await AddNeighborAsync(Scope.Tenant, OpaqueReferencePlan, new("résumé-店"));
        var issuedBookmark = reopened.LastNext ?? throw new InvalidOperationException("The source did not issue a bookmark.");
        SqlitePlanProjection.Outcome[] expected =
            [new(migrated, MigrationPlanOutcome.Migrated), new(refusedActor, MigrationPlanOutcome.Refused), new(unanswered, null)];
        var digest = Digest(plan, true, now, issuedBookmark, expected);
        await VerifyAsync(file, plan, digest, stop);
        await VerifyOperatorAsync(file, plan, expected.ToDictionary(row => row.Actor, row => row.Result), now, stop);
        Console.WriteLine($"projected {Environment.ProcessId} {plan.Id} {digest}");
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

        async Task CheckCancelledReadAsync(SqlitePlanProjection view, Contract.ActorRef actor, Contract.IActorReads reads, bool expectEmpty)
        {
            var before = await view.BookmarkAsync(stop);
            var snapshot = await view.ReadPageAsync(null, 4, stop);
            var count = await PersistedRowCountAsync(actor);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stop);
            var witness = new TailWitness(reads, afterFirstRead: () => { cancel.Cancel(); return Task.CompletedTask; });
            try
            {
                await view.ProjectNextAsync(witness, cancel.Token);
                throw new InvalidOperationException("A cancelled projection returned normally.");
            }
            catch (OperationCanceledException error) when (error.CancellationToken == cancel.Token && !stop.IsCancellationRequested) { }

            var reopenedView = new SqlitePlanProjection(file, Scope, actor);
            var after = await reopenedView.ReadPageAsync(null, 4, stop);
            Check(witness.Calls == 1 && witness.First == before && witness.FirstEmpty == expectEmpty &&
                await reopenedView.BookmarkAsync(stop) == before && await PersistedRowCountAsync(actor) == count &&
                snapshot.Closed == after.Closed && snapshot.AsOf == after.AsOf && snapshot.After == after.After &&
                snapshot.Rows.SequenceEqual(after.Rows), "Cancellation after a journal read must leave its entire SQLite page unchanged.");
        }

        async Task<long> PersistedRowCountAsync(Contract.ActorRef actor)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
            await connection.OpenAsync(stop);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT COUNT(*) FROM plan_cursors WHERE tenant = $tenant AND plan = $plan)
                    + (SELECT COUNT(*) FROM plan_outcomes WHERE tenant = $tenant AND plan = $plan)
                """;
            command.Parameters.AddWithValue("$tenant", Scope.Tenant);
            command.Parameters.AddWithValue("$plan", actor.Id);
            return (long)(await command.ExecuteScalarAsync(stop))!;
        }

        async Task AddNeighborAsync(string tenant, string planRef, Contract.ActorRef actor)
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWrite }.ConnectionString);
            await connection.OpenAsync(stop);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO plan_cursors VALUES ($tenant, $plan, NULL, 0, $asOf) ON CONFLICT (tenant, plan) DO NOTHING;
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

    internal static async Task<int> InspectAsync(string file, string reference, string expectedDigest)
    {
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var plan = new Contract.ActorRef(reference);
        await VerifyAsync(file, plan, expectedDigest, timer.Token);
        Console.WriteLine($"inspected {Environment.ProcessId} {plan.Id} migrated 1 refused 1 no-answer 1");
        return 0;
    }

    private static string Digest(Contract.ActorRef plan, bool closed, long asOf, Contract.PageToken bookmark,
        IEnumerable<SqlitePlanProjection.Outcome> rows)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Plan = plan.Id, Closed = closed, AsOf = asOf, Bookmark = bookmark.Value,
            Rows = rows.OrderBy(row => row.Actor.Id, StringComparer.Ordinal).Select(row => new { row.Actor.Id, row.Result })
        });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static async Task VerifyAsync(string file, Contract.ActorRef plan, string expectedDigest, CancellationToken stop)
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
        var unrelated = new SqlitePlanProjection(file, Scope, new(OtherPlan));
        var otherPlanPage = await unrelated.ReadPageAsync(null, 2, stop);
        Check(otherPlanPage is { After: null, Rows: [{ Result: null }], Closed: false } &&
            !results.ContainsKey(otherPlanPage.Rows[0].Actor),
            "The same physical database must isolate different tenants and plans.");
        await VerifyNeighborProcessAsync(file, plan, otherPlanPage.Rows[0].Actor, stop);
        var bookmark = await projection.BookmarkAsync(stop) ?? throw new InvalidOperationException("The stored bookmark is missing.");
        Check(Digest(plan, first.Closed, first.AsOf, bookmark, first.Rows.Concat(second.Rows).Concat(last.Rows)) == expectedDigest,
            "The persisted actors, outcomes, closed state, catch-up time and bookmark must match the producer receipt.");
    }

    private static async Task VerifyNeighborProcessAsync(string file, Contract.ActorRef plan, Contract.ActorRef actor,
        CancellationToken stop, Contract.PageToken? token = null)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        foreach (var value in new[] { System.Reflection.Assembly.GetEntryAssembly()!.Location, "sqlite-plan-projection",
                     "inspect-neighbor", file, plan.Id, actor.Id })
            start.ArgumentList.Add(value);
        if (token is { } foreign) start.ArgumentList.Add(foreign.Value);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the other tenant reader.");
        try
        {
            await child.WaitForExitAsync(stop);
            Check(child.ExitCode == 0, "The other tenant must see its own selected actor under the same plan reference.");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
        }
    }

    internal static async Task<int> InspectNeighborAsync(string file, string plan, string actor, string? foreignToken)
    {
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var scope = EmbeddedTenant.Bind("other");
        var reference = new Contract.ActorRef(plan);
        var page = await new SqlitePlanProjection(file, scope, reference).ReadPageAsync(null, 2, timer.Token);
        Check(page is { After: null, Rows: [{ Result: null }], Closed: false } && page.Rows[0].Actor.Id == actor,
            "The other tenant must see its exact fixture row.");
        if (foreignToken is not null)
        {
            IMigrationPlanReads reads = await SqliteMigrationPlanReads.OpenAsync(file, timer.Token);
            var projected = await reads.ListAsync(scope, reference, 1, cancellationToken: timer.Token);
            Check(!projected.Closed && projected.Results is { Items: [{ Outcome: null }], Next: null } &&
                projected.Results.Items[0].Actor.Id == actor && projected.Results.AsOf == page.AsOf,
                "The operator read must return the other tenant's own projection.");
            await RefusesAsync(reads, scope, reference, 1, new(foreignToken), "another tenant", timer.Token);
        }
        return 0;
    }

    internal static async Task<int> ListAsync(string file, string planRef, int size, string? pageToken)
    {
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stop = timer.Token;
        IMigrationPlanReads reads = await SqliteMigrationPlanReads.OpenAsync(file, stop);
        var page = await reads.ListAsync(Scope, new(planRef), size, pageToken is null ? null : new Contract.PageToken(pageToken), stop);
        Console.WriteLine(Json.Write(new
        {
            page.Closed,
            page.Results.AsOf,
            Items = page.Results.Items.Select(row => new
            {
                Actor = row.Actor.Id,
                Outcome = row.Outcome?.ToString() ?? (page.Closed ? "NoAnswer" : "Pending")
            }),
            Next = page.Results.Next?.Value
        }));
        return 0;
    }

    private static async Task VerifyOperatorAsync(string file, Contract.ActorRef plan,
        IReadOnlyDictionary<Contract.ActorRef, MigrationPlanOutcome?> expected, long asOf, CancellationToken stop)
    {
        IMigrationPlanReads original = await SqliteMigrationPlanReads.OpenAsync(file, stop);
        var first = await original.ListAsync(Scope, plan, 1, cancellationToken: stop);
        var firstToken = first.Results.Next ?? throw new InvalidOperationException("The first operator page must supply a continuation.");
        await new SqlitePlanProjection(file, Scope, plan).InitializeAsync(stop);
        IMigrationPlanReads reopened = await SqliteMigrationPlanReads.OpenAsync(file, stop);
        var second = await reopened.ListAsync(Scope, plan, 1, firstToken, stop);
        var secondToken = second.Results.Next ?? throw new InvalidOperationException("The second operator page must supply a continuation.");
        var third = await reopened.ListAsync(Scope, plan, 1, secondToken, stop);
        var all = first.Results.Items.Concat(second.Results.Items).Concat(third.Results.Items).ToDictionary(row => row.Actor, row => row.Outcome);
        Check(first.Closed && second.Closed && third.Closed && third.Results.Next is null && all.Count == expected.Count &&
            first.Results.AsOf == asOf && second.Results.AsOf == asOf && third.Results.AsOf == asOf &&
            expected.All(row => all.TryGetValue(row.Key, out var outcome) && outcome == row.Value),
            "A reopened read API must traverse the same closed plan at the persisted catch-up time.");
        var neighbor = await reopened.ListAsync(Scope, new(OtherPlan), 1, cancellationToken: stop);
        Check(!neighbor.Closed && neighbor.Results.Items is [{ Outcome: null }] && neighbor.Results.Next is null,
            "The neighboring plan must return its own selected actor.");
        await VerifyNeighborProcessAsync(file, plan, neighbor.Results.Items[0].Actor, stop, firstToken);
        await RefusesAsync(reopened, Scope, new(OtherPlan), 1, firstToken, "another plan", stop);
        await RefusesAsync(reopened, Scope, plan, 2, firstToken, "another page size", stop);
        await RefusesAsync(reopened, Scope, plan, 1, new("invalid-page-token"), "an invalid token", stop);

        var opaque = new Contract.ActorRef(OpaqueReferencePlan);
        var unicodeRef = await reopened.ListAsync(Scope, opaque, 1, cancellationToken: stop);
        var shortRef = await reopened.ListAsync(Scope, opaque, 1, unicodeRef.Results.Next, stop);
        Check(unicodeRef.Results.Items is [{ Actor.Id: "résumé-店" }] && unicodeRef.Results.Next is not null &&
            shortRef.Results.Items is [{ Actor.Id: "short" }] && shortRef.Results.Next is null,
            "Operator pages must carry short and Unicode references without interpreting their shape.");

    }

    private static async Task RefusesAsync(IMigrationPlanReads reads, Contract.BoundScope scope, Contract.ActorRef plan,
        int size, Contract.PageToken from, string context, CancellationToken stop)
    {
        try { await reads.ListAsync(scope, plan, size, from, stop); }
        catch (ArgumentException error) when (error.ParamName == "from") { return; }
        throw new InvalidOperationException($"The operator read must reject a continuation for {context}.");
    }

    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
    }

    private sealed class TailWitness(Contract.IActorReads inner, bool synchronize = false, Func<Task>? afterFirstRead = null) : Contract.IActorReads
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        internal int Calls => _calls;
        internal bool FirstEmpty { get; private set; }
        internal Contract.PageToken? LastNext { get; private set; }
        internal Contract.PageToken? First { get; private set; }

        public async ValueTask<Contract.Page<Contract.JournalEntry>> TailAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default)
        {
            var page = await inner.TailAsync(scope, actor, from, cancellationToken);
            if (page.Next is { } next) LastNext = next;
            var call = Interlocked.Increment(ref _calls);
            if (call == 1)
            {
                First = from;
                FirstEmpty = page.Items.Count == 0;
                if (afterFirstRead is not null) await afterFirstRead();
            }
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
