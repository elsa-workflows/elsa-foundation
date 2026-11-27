using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;
using static Kernel.Testing.NativeTerminalFixture;

namespace Kernel.Native.Run;

internal static class ShippedNativeTerminalEnvelopeChecks
{
    private const string Definition = "shipped-native-terminal-envelope";
    private const string TerminalOperation = "terminate";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    internal static async Task<int> RunAsync(bool postgres, bool inBatch)
    {
        using var timeout = new CancellationTokenSource(Lease);
        var token = timeout.Token;
        var scope = EmbeddedTenant.Bind(Definition);
        var directory = postgres ? null : Directory.CreateTempSubdirectory(Definition);
        try
        {
            var (open, ingress, reads) = postgres
                ? await OpenPostgresAsync(scope, token)
                : await OpenSqliteAsync(Path.Combine(directory!.FullName, "kernel.db"), scope, token);
            await using var database = open();
            var marker = await database.StoreVersions.SingleAsync(row => row.Id == StoreVersionEntity.MarkerId, token);
            Check(marker.Format == StoreFormat.RootShipped, "the installed store has the shipped root format");
            var cold = new EfStore(open, postgres);
            var runtime = new WorkflowRuntime(ingress, reads);
            var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, Wf.Parallel("root", null,
                Wf.Node<OpenInbox>("receive"), Wf.Node<Padding>("padding"))));
            var native = new NativeExecutionModel(registry, new() { SelfSends = false });
            // The earlier runner uses the canonical Native shape without features requiring v14.
            var v13 = new PostgresRootTerminateChecks.ObservedNative(native, 13);
            var v14 = new PostgresRootTerminateChecks.ObservedNative(native, native.StateVersion);
            var committed = new Dictionary<string, InstanceRow>(StringComparer.Ordinal);
            var exact = await StartAsync("exact");
            var overflow = await StartAsync("overflow");
            EfStore.RootImage saved;
            long previousEpoch;
            string exactInbox;
            int exactPadding;
            await using (var original = await OwnAsync("original", new PublicActor([v13])))
            {
                (saved, exactInbox, exactPadding) = await GrowAsync(original, exact, extraBytes: 0);
                await GrowAsync(original, overflow, extraBytes: 1);
                previousEpoch = original.Epoch;
            }

            var selected = await reads.GetAsync(scope, exact, token);
            Check(selected is { Selection: not null, Header.Status: Contract.ActorStatus.Running, Header.EndedAt: null } &&
                Version(saved) == 13, "the preceding owner leaves a selectable v13 running root");
            Check(await runtime.TerminateAsync(scope, TerminalOperation, selected!, cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } target } && target == exact,
                "the shipped ingress admits selected Terminate for the exact creation");

            await using var successor = await OwnAsync("successor", new PublicActor([v14]));
            var ended = await DriveAsync(successor, exact, saved.Revision + 1);
            var terminal = await reads.GetAsync(scope, exact, token);
            Check(successor.Epoch > previousEpoch && ended.Status == InstanceStatus.Cancelled &&
                ended.TerminalRevision == ended.Revision && ended.TerminalAt > 0 &&
                terminal is { Park: null, Selection: not null, Header.Status: Contract.ActorStatus.Cancelled } &&
                terminal.Header.EndedAt == ended.TerminalAt && terminal.Header.Ref == exact,
                "the replacement owner stamps the exact terminal revision and time and publishes the matching terminal selection");
            var terminalState = State(ended);
            var closedInbox = terminalState.Inboxes![exactInbox];
            Check(Version(ended) == native.StateVersion && v14.UpgradedOnTerminate && v14.OrdinaryTurns == 0 &&
                terminalState.Frames.Values.Single(frame => frame.Node == "padding").State!.GetValue<string>().Length == exactPadding &&
                terminalState.Inboxes.Count == 1 && closedInbox.Closed == "cancelled",
                "Terminate upgrades before any ordinary turn and closes the authored inbox with its padding intact");

            var content = await ReadPhysicalContentAsync(ended.Actor.Id);
            var journal = await cold.ReadJournalOfAsync(0, ended.Actor.Id, 0, token);
            Check(content <= EfStore.RootContentBytes && journal.Last() is { Entries: """["end"]""" } end &&
                end.Revision == ended.Revision && journal.Count(entry => entry.Revision == ended.Revision) == 1 &&
                await database.Messages.AsNoTracking().CountAsync(row => row.Target == ended.Actor.Id && row.Kind == Kinds.Terminate && row.Done, token) == 1 &&
                await runtime.TerminateAsync(scope, TerminalOperation, selected!, cancellationToken: token) is
                    { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } replayedActor } && replayedActor == exact,
                $"the physical terminal content fits ({content} bytes), with one end entry and the settled operator receipt");
            return 0;

            async Task<Contract.ActorRef> StartAsync(string id) =>
                (await runtime.StartAsync(scope, id, NativeExecutionModel.Start(Definition), cancellationToken: token)).Actor
                ?? throw new InvalidOperationException($"The {id} root start was refused.");

            async Task<(EfStore.RootImage Checkpoint, string Inbox, int Padding)> GrowAsync(Worker owner, Contract.ActorRef reference, int extraBytes)
            {
                var before = await DriveAsync(owner, reference, 1);
                var state = State(before);
                var receivingFrame = state.Frames.Values.Single(frame => frame.Node == "receive").Id;
                var row = committed[reference.Id];
                var growth = row.FoundationDispatchHeader!.TerminalGrowth;
                Check(growth == 7 && state.Inboxes is { Count: 1 } && state.Inboxes[receivingFrame].Closed is null,
                    "the committed Native state contains one open invocation inbox with its closure reserve");
                // Padding replaces the empty frame state with ASCII, adding exactly this many UTF-8 bytes.
                var padding = checked((int)(Envelope - RowBytes(row, PublicHeaderReserve) - growth + extraBytes));
                var paddingFrame = state.Frames.Values.Single(frame => frame.Node == "padding").Id;
                await SendAsync(reference, "grow", CompositeSignal.Kind, new CompositeSignal(paddingFrame, "grow", padding.ToString(CultureInfo.InvariantCulture)));
                var result = await DriveAsync(owner, reference, 2);
                if (extraBytes == 0)
                {
                    var grown = committed[reference.Id];
                    Check(result.Status == InstanceStatus.Running && result.State == grown.State &&
                        RowBytes(grown, PublicHeaderReserve) + grown.FoundationDispatchHeader!.TerminalGrowth == Envelope,
                        $"the exact projected Native envelope commits ({Envelope} bytes)");
                }
                else
                {
                    Check(result is { Status: InstanceStatus.Faulted, ParkAt: > 0, TerminalAt: null, TerminalRevision: null } &&
                        result.State == before.State && result.TypeData == before.TypeData && result.Fault?.Contains("terminal growth exceed", StringComparison.Ordinal) == true &&
                        (await reads.GetAsync(scope, reference, token)) is { Park: not null, Header.EndedAt: null },
                        "one extra byte opens Park and preserves the previous checkpoint without a terminal stamp");
                }
                return (result, receivingFrame, padding);
            }

            async Task SendAsync<T>(Contract.ActorRef reference, string id, string kind, T message) =>
                Check((await ingress.SendAsync(scope, new Contract.Target.Ref(reference), id, kind, Json.Write(message), cancellationToken: token)).Outcome ==
                    Contract.AdmissionOutcome.Accepted, $"the shipped ingress admits {id} to its exact root");

            async Task<Worker> OwnAsync(string name, PublicActor actor)
            {
                var store = new EfStore(open, postgres, inBatch) { CommitSql = CommitSql.Owned };
                await store.EnableRootJobsAsync(token);
                await store.EnableRootChildrenAsync(token);
                var roots = postgres ? store.BindWorkerNode(actor.Withholds) : store.BindSqliteWorkerNode(actor.Withholds);
                var adapter = new ActorAdapter(new EfStore.RootHeaders(roots), actor, rootJobHandle: store.RootJobHandle,
                    rootTimedJobClock: store.ReadRootTimedJobPublicationTimeAsync, rootHeldReplies: true);
                var observed = DispatchProxy.Create<IKernelStore, PostgresRootParkOpeningChecks.ObservedCommit>();
                var proxy = (PostgresRootParkOpeningChecks.ObservedCommit)observed;
                proxy.Store = store;
                proxy.Write = async (batch, commitToken) =>
                {
                    var result = await store.CommitAsync(batch, commitToken);
                    if (result.Landed)
                        foreach (var turn in batch.Turns)
                            if (turn.Row is { FoundationDispatchHeader: { } header } row) committed[header.Reference] = row;
                    return result;
                };
                var owner = new Worker(name, 0, observed, adapter, new WorkerOptions { Partitions = 1, Lease = Lease });
                var host = new DurableHost([owner]);
                await host.HeartbeatAsync(Lease, token);
                if (await host.ActivateAsync(0, token) == owner) return owner;
                await owner.DisposeAsync();
                throw new InvalidOperationException($"The {name} fixture owner did not take its partition.");
            }

            async Task<EfStore.RootImage> DriveAsync(Worker owner, Contract.ActorRef reference, long revision)
            {
                await owner.WokenAsync(null, token);
                while (owner.Owns)
                {
                    await owner.RunAsync(token);
                    if (await cold.ReadFirstRootAsync(scope.Tenant, reference.Id, token) is { } image && image.Revision >= revision) return image;
                    await Task.Delay(5, token);
                }
                throw new InvalidOperationException($"The fixture owner lost its partition before revision {revision} of {reference.Id}.");
            }

            Task<int> ReadPhysicalContentAsync(string actor) => postgres
                ? database.Database.SqlQuery<int>($"""
                    SELECT octet_length("State") + octet_length("FoundationHeader") + coalesce(octet_length("FoundationStateWire"), 0) AS "Value"
                    FROM "Instances" WHERE "Id" = {actor}
                    """).SingleAsync(token)
                : database.Database.SqlQuery<int>($"""
                    SELECT length(CAST("State" AS BLOB)) + length(CAST("FoundationHeader" AS BLOB)) + coalesce(length("FoundationStateWire"), 0) AS "Value"
                    FROM "Instances" WHERE "Id" = {actor}
                    """).SingleAsync(token);
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task<(Func<KernelDb>, Contract.IIngress, Contract.IActorReads)> OpenPostgresAsync(Contract.BoundScope scope, CancellationToken token)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => PostgresPreviewHosting.CreateShippedContext(options);
        await using var database = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(database, token), "the fixture uses an empty PostgreSQL database");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(1);
        await store.InstallPreviewRootSchemaAsync(token);
        var foundation = await PostgresPreviewHosting.OpenFoundationWithOperatorsAsync(Open, PublicActor.ActorType,
            (caller, _, kind) => ReferenceEquals(caller, scope) && kind == Contract.Kinds.Terminate, cancellationToken: token);
        return (Open, foundation, foundation);
    }

    private static async Task<(Func<KernelDb>, Contract.IIngress, Contract.IActorReads)> OpenSqliteAsync(string path, Contract.BoundScope scope, CancellationToken token)
    {
        await SqliteEmbeddedHosting.InstallShippedAsync(path, token);
        var connection = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ToString();
        var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connection).Options;
        var definitions = new SqliteAdmissionDefinitions();
        KernelDb Open() => KernelDb.ForSqliteRootRuntime(options, definitions);
        var ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(path, PublicActor.ActorType,
            (caller, _, kind) => ReferenceEquals(caller, scope) && kind == Contract.Kinds.Terminate, cancellationToken: token);
        return (Open, ingress, SqliteEmbeddedHosting.CreateFoundationReads(path));
    }

    private static int Version(EfStore.RootImage image) => Json.Read<JsonElement>(image.TypeData).GetProperty("Version").GetInt32();
    private static NativeState State(EfStore.RootImage image) => Json.Read<NativeState>(image.State!);

    private static void Check(bool pass, string description)
    {
        if (!pass) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
