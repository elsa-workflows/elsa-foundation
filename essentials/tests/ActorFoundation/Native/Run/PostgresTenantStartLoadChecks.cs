using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresTenantStartLoadChecks
{
    private const int RequiredHeavyStarts = 100_000;
    private const int HeavyCallers = 32;
    private const string Node = "tenant-start-load";
    private static readonly TimeSpan QuietInterval = TimeSpan.FromMilliseconds(50);

    internal static async Task<int> RunAsync(bool limited, int heavyStarts = RequiredHeavyStarts)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heavyStarts);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(heavyStarts, RequiredHeavyStarts);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var token = deadline.Token;
        var actor = new FirstTurnActor();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(
            Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
            throw new InvalidOperationException("Supply an empty dedicated PostgreSQL fixture.")).Options;
        KernelDb Open() => PostgresPreviewHosting.CreateShippedContext(options, actor.Type, protectsStarts: false);
        await using (var empty = Open())
            Require(await PostgresFixtureDatabase.IsEmptyAsync(empty, token), "The fixture database must start empty.");
        await PostgresPreviewHosting.InstallShippedAsync(options, actor.Type, protectsStarts: false, token);

        var pressure = limited ? RootAdmissionPressure.CreateDefault() : new RootAdmissionPressure(
            new(250_000, 200_000, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
            ordinaryCapacity: 256, operatorCapacity: 32, ordinaryRate: new(250_000, 250_000), operatorRate: new(100, 100));
        var requests = PostgresPreviewHosting.CreateRequests(Open, Node, actor.Type, deduplicationWindow: null, pressure);
        var ingress = await PostgresPreviewHosting.OpenFoundationAsync(Open, actor.Type, requests, cancellationToken: token);
        await using var owner = await PostgresPreviewHosting.StartAsync(Open, Node, actor,
            cause => throw new InvalidOperationException(cause), cancellationToken: token,
            onFault: error => Console.Error.WriteLine(error.Message), requests: requests);
        await using var observer = Open();
        var marker = await observer.StoreVersions.SingleAsync(row => row.Id == StoreVersionEntity.MarkerId, token);
        var partitionCount = await observer.Partitions.CountAsync(token);
        Require(marker.Format == StoreFormat.RootShipped && observer.UsesRootDueBacklog && partitionCount > 0,
            "Expected the shipped pressure profile and installed partitions.");
        while (await observer.Partitions.CountAsync(row => row.Owner == Node, token) != partitionCount)
            await Task.Delay(100, token);

        var heavy = ContractScopes.Bind("load-heavy");
        var quiet = ContractScopes.Bind("load-quiet");
        var heavyTarget = new Contract.Target.Start(new(actor.Type, "heavy"));
        var quietTarget = new Contract.Target.Start(new(actor.Type, TypeData: FirstTurnActor.Quiet));
        var quietClock = new Dictionary<string, long>(StringComparer.Ordinal);
        var quietRefusals = new Dictionary<Contract.RefusalReason, int>();
        var heavyRefusals = new int[Enum.GetValues<Contract.RefusalReason>().Length];
        int offeredHeavy = 0, acceptedHeavy = 0, offeredQuiet = 0;

        Console.WriteLine($"starting {(limited ? "limited" : "control")} at {marker.Format}, {partitionCount} owned partitions, " +
            $"{heavyStarts} heavy starts across {HeavyCallers} callers, and a quiet start every {QuietInterval.TotalMilliseconds} ms");
        var startedAt = Stopwatch.GetTimestamp();
        var burst = System.Threading.Tasks.Parallel.ForAsync(0, heavyStarts,
            new ParallelOptions { MaxDegreeOfParallelism = HeavyCallers, CancellationToken = token },
            async (index, ct) =>
            {
                var admission = await StartAsync(heavy, heavyTarget, index, ct);
                if (admission.Outcome == Contract.AdmissionOutcome.Accepted)
                    Interlocked.Increment(ref acceptedHeavy);
                else
                    Interlocked.Increment(ref heavyRefusals[(int)RequireRefusal(admission)]);
                var offered = Interlocked.Increment(ref offeredHeavy);
                if (offered % 5_000 == 0)
                    Console.WriteLine($"progress heavy {offered}/{heavyStarts} accepted {Volatile.Read(ref acceptedHeavy)} " +
                        $"elapsed {Stopwatch.GetElapsedTime(startedAt).TotalSeconds:F1}s");
            });
        using (var ticks = new PeriodicTimer(QuietInterval))
        {
            do
            {
                var requestedAt = Stopwatch.GetTimestamp();
                var admission = await StartAsync(quiet, quietTarget, offeredQuiet++, token);
                if (admission is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference })
                    quietClock.Add(reference.Id, requestedAt);
                else
                {
                    var reason = RequireRefusal(admission);
                    quietRefusals[reason] = quietRefusals.GetValueOrDefault(reason) + 1;
                }
            } while (await ticks.WaitForNextTickAsync(token) && !burst.IsCompleted);
        }
        await burst;
        var offerTime = Stopwatch.GetElapsedTime(startedAt);
        Require(offeredHeavy == heavyStarts && offeredQuiet == quietClock.Count + quietRefusals.Values.Sum() && quietClock.Count > 0,
            "The fixture must account for every real offer and at least one quiet completion.");
        Require(acceptedHeavy + heavyRefusals.Sum() == heavyStarts, "Heavy admissions must account for every offer.");
        Console.WriteLine($"offered {heavyStarts} heavy and {offeredQuiet} quiet starts in {offerTime.TotalSeconds:F1}s, waiting for committed turns");

        var lastDrainProgress = Stopwatch.GetTimestamp();
        while (true)
        {
            var finishedHeavy = await CountFinishedAsync(heavy);
            var finishedQuiet = await CountFinishedAsync(quiet);
            Require(finishedHeavy <= acceptedHeavy && finishedQuiet <= quietClock.Count, "A refused or duplicate root reached its first turn.");
            if (finishedHeavy == acceptedHeavy && finishedQuiet == quietClock.Count) break;
            Require(owner.LastError is null, "The shipped owner failed while draining accepted roots.");
            if (Stopwatch.GetElapsedTime(lastDrainProgress) >= TimeSpan.FromSeconds(5))
            {
                Console.WriteLine($"drain heavy {finishedHeavy}/{acceptedHeavy}, quiet {finishedQuiet}/{quietClock.Count}, " +
                    $"elapsed {Stopwatch.GetElapsedTime(startedAt).TotalSeconds:F1}s");
                lastDrainProgress = Stopwatch.GetTimestamp();
            }
            await Task.Delay(1_000, token);
        }

        var store = new EfStore(Open, postgres: true);
        var latency = new List<double>(quietClock.Count);
        foreach (var (reference, requestedAt) in quietClock)
        {
            if (await store.ReadFirstRootAsync(quiet.Tenant, reference, token) is not
                { Status: InstanceStatus.Finished, Revision: 1, State: { } state })
                throw new InvalidOperationException($"Quiet root {reference} must have one committed first turn.");
            var firstTurn = long.Parse(state, CultureInfo.InvariantCulture);
            Require(firstTurn >= requestedAt, "The first turn preceded its start offer.");
            latency.Add(Stopwatch.GetElapsedTime(requestedAt, firstTurn).TotalMilliseconds);
        }
        latency.Sort();
        var heavyReasons = Enum.GetValues<Contract.RefusalReason>().Where(reason => heavyRefusals[(int)reason] != 0)
            .ToDictionary(reason => reason.ToString(), reason => heavyRefusals[(int)reason]);
        Require(limited || heavyReasons.Count + quietRefusals.Count == 0, "The unthrottled control must admit its entire offered load.");
        Require(owner.Errors == 0, "The shipped owner must complete without loop errors.");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            Lane = limited ? "limited" : "control", Partitions = partitionCount, HeavyCallers,
            HeavyOffered = offeredHeavy, HeavyCommitted = acceptedHeavy, HeavyRefusals = heavyReasons,
            QuietOffered = offeredQuiet, QuietCommitted = quietClock.Count,
            QuietRefusals = quietRefusals.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value),
            QuietP99Milliseconds = latency[(int)Math.Ceiling(latency.Count * 0.99) - 1],
            OfferSeconds = offerTime.TotalSeconds, TotalSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds
        }));
        return 0;

        ValueTask<Contract.Admission> StartAsync(Contract.BoundScope scope, Contract.Target.Start target, int index, CancellationToken ct) =>
            ingress.SendAsync(scope, target, "start-" + index.ToString(CultureInfo.InvariantCulture), Contract.Kinds.Start, cancellationToken: ct);

        Task<int> CountFinishedAsync(Contract.BoundScope scope) => observer.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM "Instances"
            WHERE "FoundationTenant" = {scope.Tenant} AND "Status" = {(int)InstanceStatus.Finished}
            """).SingleAsync(token);
    }

    private static Contract.RefusalReason RequireRefusal(Contract.Admission admission) => admission switch
    {
        { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.RateLimited or
            Contract.RefusalReason.InboxFull or Contract.RefusalReason.NodeBusy } => admission.Reason.Value,
        _ => throw new InvalidOperationException($"Unexpected start admission: {admission}.")
    };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FirstTurnActor : Contract.IActorType
    {
        internal const string Quiet = "quiet";
        public string Type => "tenant-first-turn-probe";
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 24;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken token) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken token)
        {
            Require(turn.Inbox is [{ Kind: Contract.Kinds.Start }], "The measurement requires a first turn from a genuine root start.");
            turn.State = turn.Header.TypeData == Quiet ? Stopwatch.GetTimestamp().ToString(CultureInfo.InvariantCulture) : "0";
            turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
    }
}
