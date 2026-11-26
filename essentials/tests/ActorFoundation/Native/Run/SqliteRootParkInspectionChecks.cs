using Elsa.ActorFoundation.Hosting;
using Elsa.ActorFoundation.TestSupport;

using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
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
            await InstallFoundationAsync(file, token);
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

            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER kernel_project_root_actor_list", token);
            await ExpectFoundationFailureAsync<StoreFormatException>(() => reads.GetAsync(scope, actor, token).AsTask(),
                "the point read refuses changed actor projection definitions");
            await ExpectFoundationFailureAsync<StoreFormatException>(() => reads.InboxAsync(scope, actor, cancellationToken: token).AsTask(),
                "the inbox read refuses changed store definitions");
            return 0;

            SqliteEmbeddedHostedService Host(string node, ParkOpeningActor executor) => SqliteEmbeddedHostedService.ForFoundation(file,
                node, executor, reason => throw new InvalidOperationException(reason), Loops, fenceInBatch);
        }
        finally { directory.Delete(recursive: true); }
    }
}
