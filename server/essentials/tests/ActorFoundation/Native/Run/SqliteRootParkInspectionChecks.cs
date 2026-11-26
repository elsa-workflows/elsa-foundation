using System.Data.Common;
using Elsa.ActorFoundation.Hosting;
using Elsa.ActorFoundation.TestSupport;

using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    internal static async Task<int> RunParkInspectionAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = budget.Token;
        var directory = Directory.CreateTempSubdirectory("sqlite-park-inspection");
        try
        {
            var file = Path.Combine(directory.FullName, "park.db");
            if (fenceInBatch)
            {
                await using var utf16 = OpenFoundationDb(file);
                await utf16.Database.OpenConnectionAsync(token);
                await utf16.Database.ExecuteSqlRawAsync("PRAGMA encoding = 'UTF-16le'", token);
                await EfStore.InstallSqliteShippedAsync(utf16, 1, token);
                Require(await utf16.Database.SqlQueryRaw<string>("""SELECT encoding AS "Value" FROM pragma_encoding""")
                    .SingleAsync(token) == "UTF-16le", "the in-batch inspection fixture uses a UTF-16 SQLite file");
            }
            else await InstallFoundationAsync(file, token);
            var scope = EmbeddedTenant.Bind("sqlite-park-inspection");
            var other = ContractScopes.Bind("sqlite-park-inspection-other");
            var ingress = await SqliteEmbeddedHosting.OpenFoundationAsync(file, ParkKind, cancellationToken: token);
            var reads = SqliteEmbeddedHosting.CreateFoundationReads(file, pageSize: 1);
            var start = new Contract.Target.Start(new(ParkKind, "inspect"));
            var actor = (await ingress.SendAsync(scope, start, "actor", Contract.Kinds.Start, cancellationToken: token)).Actor!.Value;
            var sibling = (await ingress.SendAsync(scope, start, "sibling", Contract.Kinds.Start, cancellationToken: token)).Actor!.Value;
            var target = new Contract.Target.Ref(actor);
            await using var db = OpenFoundationDb(file);
            var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
            var probe = new ParkOpeningActor();
            var definitions = new SqliteAdmissionDefinitions();
            var raw = new EfStore(() => OpenFoundationDb(file, definitions), postgres: false, fenceInBatch);
            Contract.ActorView park;
            Contract.Page<Contract.DeliveryView> first;
            long earlierEpoch;
            await using (var owner = Host("park-reader-one", probe))
            {
                await owner.StartAsync(token);
                await WaitForRootAsync(raw, scope.Tenant, actor, 1, token);
                Require(await reads.GetAsync(scope, actor, token) is { Header.Status: Contract.ActorStatus.Running, Park: null, Selection: { } running } &&
                    PostgresRootSelection.MatchesRunning(cursor, running, scope.Tenant, actor.Id),
                    "the SQLite point read seals the running selection from the qualified projection");
                earlierEpoch = (await FenceAsync(file, token)).Epoch;
                var observation = new ParkReadObservation();
                var observing = new EfStore(() => OpenFoundationDb(file, definitions, observation), postgres: false, fenceInBatch);
                var previous = observing.ReadRootActorAsync(scope.Tenant, actor.Id, token);
                await observation.ClockSampled.Task.WaitAsync(token);
                var later = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000;
                Require(await ingress.SendAsync(scope, target, "poison", "fail", "\"poison\"", cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the inspection fixture admits its faulting input");
                await probe.FaultEntered.Task.WaitAsync(token);
                // This later input is admitted before the fault turn lands and remains pending at the Park.
                Require(await ingress.SendAsync(scope, target, "later", "continue", options: new(DueAt: later), cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Accepted }, "the inspection fixture admits a scheduled input beside the fault");
                probe.ReleaseFault.TrySetResult();
                await WaitForRootAsync(raw, scope.Tenant, actor, 2, token);
                park = (await reads.GetAsync(scope, actor, token))!;
                observation.Resume.TrySetResult();
                var earlier = await previous;
                Require(earlier is { Item: { Status: InstanceStatus.Running, ParkAt: null } } && earlier.Value.AsOf < park.Park!.At,
                    "a read that pins its clock before a concurrent Park returns the earlier snapshot and time");
                Require(park is { Header: { Status: Contract.ActorStatus.Faulted, Fault: ContinuationFault, EndedAt: null }, Park.At: > 0, Selection: not null } &&
                    park.AsOf >= park.Park.At && PostgresRootSelection.OpenPark(cursor, park.Selection, scope.Tenant, actor.Id) == new FoundationParkMark(2, park.Park.At),
                    "the SQLite point read returns the durable fault and exact Park selection");
                Require(await reads.GetAsync(other, actor, token) is null &&
                    await reads.InboxAsync(other, actor, cancellationToken: token) is { Items.Count: 0, Next: null, AsOf: > 0 },
                    "a different tenant cannot read the root or enumerate its delivery metadata");
                first = await reads.InboxAsync(scope, actor, cancellationToken: token);
                Require(first is { Items: [{ Id: "poison", Kind: "fail", State: Contract.DeliveryState.Consumed, Size: > 0, Sender: null }], Next: not null } &&
                    first.AsOf >= park.Park!.At, "the first inbox page reports the consumed fault with a continuation token");
                Require(await reads.InboxAsync(scope, actor, first.Next, token) is
                    { Items: [{ Id: "later", Kind: "continue", State: Contract.DeliveryState.Pending, Due: var due }], Next: null } && due == later,
                    "the next page reports the still-pending delivery with its original due time");
                await observing.ReadRootInboxAsync(scope.Tenant, actor.Id, 0, "", 1, token);
                Require(observation.Plan.Any(step => step.Contains("IX_FoundationRootInbox_Inspectable", StringComparison.Ordinal) &&
                    step.Contains("Target=?", StringComparison.Ordinal)),
                    "the qualified inbox performs an actor-scoped seek through its partial index");
                await ExpectFoundationFailureAsync<ArgumentException>(() => reads.InboxAsync(other, actor, first.Next, token).AsTask(),
                    "an inbox cursor cannot be reused under another tenant");
                await ExpectFoundationFailureAsync<ArgumentException>(() => reads.InboxAsync(scope, sibling, first.Next, token).AsTask(),
                    "an inbox cursor cannot be reused for another reference");
                await owner.StopAsync(token);
                Require(owner.LastError is null, "inspection leaves the first owner able to stop cleanly");
            }

            await using (var successor = Host("park-reader-two", new ParkOpeningActor()))
            {
                await successor.StartAsync(token);
                var marker = (await ingress.SendAsync(scope, start, "marker", Contract.Kinds.Start, cancellationToken: token)).Actor!.Value;
                await WaitForRootAsync(raw, scope.Tenant, marker, 1, token);
                var same = await reads.GetAsync(scope, actor, token);
                var again = await reads.InboxAsync(scope, actor, cancellationToken: token);
                Require((await FenceAsync(file, token)).Epoch > earlierEpoch && same is not null && same.Park == park.Park && same.Header.Fault == park.Header.Fault &&
                    again.Items.Single().Handle == first.Items.Single().Handle &&
                    (await reads.InboxAsync(scope, actor, first.Next, token)).Items.Single().Id == "later",
                    "the Park, consumed delivery handle and cursor persist across owner replacement");
                await successor.StopAsync(token);
                Require(successor.LastError is null, "inspection leaves replacement ownership able to stop cleanly");
            }

            var physical = (await raw.ReadFirstRootAsync(scope.Tenant, actor.Id, token))!.Actor.Id;
            await InsertLaterOperatorAsync(db, actor.Id, physical, token);
            var pending = await reads.InboxAsync(scope, actor, first.Next, token);
            Require(pending.Next is not null && await reads.InboxAsync(scope, actor, pending.Next, token) is
                { Items: [{ Id: "later-park-terminate", Kind: Contract.Kinds.Terminate, State: Contract.DeliveryState.Pending }], Next: null },
                "inspection reports the stored Park-selected control as pending before its runtime stage");
            await CheckReplyProjectionAsync(physical);
            if (!OperatingSystem.IsWindows())
            {
                var alternate = Path.Combine(directory.FullName, "alternate.db");
                await InstallFoundationAsync(alternate, token);
                var link = Path.Combine(directory.FullName, "view.db");
                File.CreateSymbolicLink(link, file);
                var throughLink = SqliteEmbeddedHosting.CreateFoundationReads(link);
                File.Delete(link);
                File.CreateSymbolicLink(link, alternate);
                Require(await throughLink.GetAsync(scope, actor, token) is { Header.Fault: ContinuationFault },
                    "an existing read edge remains bound to its original file when its symlink is retargeted");
            }
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER kernel_project_root_actor_list", token);
            await ExpectFoundationFailureAsync<StoreFormatException>(() => reads.GetAsync(scope, actor, token).AsTask(),
                "the point read refuses changed actor projection definitions");
            await ExpectFoundationFailureAsync<StoreFormatException>(() => reads.InboxAsync(scope, actor, cancellationToken: token).AsTask(),
                "the inbox read refuses changed store definitions");
            return 0;

            async Task CheckReplyProjectionAsync(string physical)
            {
                var (header, _, _) = RootAdmissionWire.EncodeDirect("later", 1, "continue", null, due: null, held: true);
                var route = ReplyTo.Of("inspection", new string('r', 1_048_576), 9_000);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Start" = {header}, "Payload" = printf('%.*c', 1048576, 'x'), "ReplyTo" = {route}
                    WHERE "Target" = {physical} AND "Kind" = 'continue' AND NOT "Done"
                    """, token);
                Require(await reads.InboxAsync(scope, actor, first.Next, token) is { Items: [{ Id: "later", Size: > 1048576 }] },
                    "a large held route and payload leave only their bounded delivery metadata in the page");
                await CheckInvalidRouteAsync("inspection|9223372036854775808|request", "a reply deadline beyond the supported range");
                await CheckInvalidRouteAsync("inspection|1|", "an empty reply request");
                await CheckInvalidRouteAsync("inspection|1||early", "an empty early reply request");

                async Task CheckInvalidRouteAsync(string invalidRoute, string witness)
                {
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE "Messages" SET "ReplyTo" = {invalidRoute}
                        WHERE "Target" = {physical} AND "Kind" = 'continue' AND NOT "Done"
                        """, token);
                    await ExpectFoundationFailureAsync<StoreFormatException>(() => reads.InboxAsync(scope, actor, first.Next, token).AsTask(),
                        $"the metadata projection refuses {witness}");
                }
            }

            SqliteEmbeddedHostedService Host(string node, ParkOpeningActor executor) => SqliteEmbeddedHostedService.ForFoundation(file,
                node, executor, reason => throw new InvalidOperationException(reason), Loops, fenceInBatch);
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class ParkReadObservation : DbCommandInterceptor
    {
        public TaskCompletionSource ClockSampled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Plan { get; } = [];

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("root-read-snapshot-clock", StringComparison.Ordinal) && ClockSampled.TrySetResult())
                await Resume.Task.WaitAsync(cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("WITH page AS", StringComparison.Ordinal)) return result;
            await using var explain = command.Connection!.CreateCommand();
            explain.Transaction = command.Transaction;
            explain.CommandText = "EXPLAIN QUERY PLAN " + command.CommandText;
            foreach (DbParameter parameter in command.Parameters)
            {
                var copy = explain.CreateParameter();
                copy.ParameterName = parameter.ParameterName;
                copy.DbType = parameter.DbType;
                copy.Value = parameter.Value;
                explain.Parameters.Add(copy);
            }
            await using var plan = await explain.ExecuteReaderAsync(cancellationToken);
            while (await plan.ReadAsync(cancellationToken)) Plan.Add(plan.GetString(3));
            return result;
        }
    }
}
