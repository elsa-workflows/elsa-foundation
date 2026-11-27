using System.Diagnostics;
using System.Reflection;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using SQLitePCL;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

// Exercise the shipped one-way edge and owned Worker, with passes called explicitly to exclude idle polling and heartbeats.
internal static class ShippedWriteBudget
{
    private const int Lines = 10;
    private const int Samples = 3;
    private const string Node = "shipped-write-budget";

    internal static async Task<int> RunAsync(bool postgres, bool inBatch, bool requireBudget)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory(Node);
        var path = directory is null ? null : Path.Combine(directory.FullName, "kernel.db");
        var counter = new Counting();
        using var sqlite = path is null ? null : new SqliteMeter(path);
        var options = new DbContextOptionsBuilder<KernelDb>();
        var observerOptions = new DbContextOptionsBuilder<KernelDb>();
        Wire? wire = null;
        if (postgres)
        {
            var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
                throw new InvalidOperationException("Set KERNEL_PREVIEW_POSTGRES to a fresh disposable PostgreSQL database.");
            var settings = new NpgsqlConnectionStringBuilder(connection);
            if (settings.Host != "127.0.0.1" || settings.SslMode != SslMode.Disable ||
                settings.GssEncryptionMode == GssEncryptionMode.Require)
                throw new ArgumentException("The wire meter requires a disposable plaintext loopback PostgreSQL connection.");
            observerOptions.UseNpgsql(connection);
            wire = Wire.Start("postgres", $"Host=127.0.0.1;Port={settings.Port}");
            settings.Port = wire.LocalPort;
            settings.GssEncryptionMode = GssEncryptionMode.Disable;
            settings.Pooling = true;
            options.UseNpgsql(settings.ConnectionString).AddInterceptors(counter, counter.Transactions);
        }
        else
        {
            var connection = new SqliteConnectionStringBuilder
            {
                DataSource = path!, ForeignKeys = true, Mode = SqliteOpenMode.ReadWrite, DefaultTimeout = 2
            }.ConnectionString;
            options.UseSqlite(connection);
            observerOptions.UseSqlite(connection);
        }

        var definitions = new SqliteAdmissionDefinitions();
        IDisposable? sqliteFile = null;
        DbContextOptions<KernelDb>? hostedOptions = null;
        KernelDb At(DbContextOptions<KernelDb> settings) => postgres ? PostgresPreviewHosting.CreateShippedContext(settings)
            : KernelDb.ForSqliteRootRuntime(settings, definitions);
        KernelDb Open() => At(hostedOptions ?? options.Options);
        KernelDb Observe() => At(observerOptions.Options);
        var observer = new EfStore(Observe, postgres);
        var previousOutput = WriteLine.Out;
        try
        {
            Contract.IIngress ingress;
            Contract.IActorReads reads;
            IReplyTransport transport;
            RootAdmissionPressure? pressure;
            if (postgres)
            {
                await using var empty = Observe();
                Check(await PostgresFixtureDatabase.IsEmptyAsync(empty, token), "The PostgreSQL database must be empty.");
                await observer.CreateAsync(1);
                await observer.InstallPreviewRootSchemaAsync(token);
                var requests = PostgresPreviewHosting.CreateRequests(Open, Node, PublicActor.ActorType);
                var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, PublicActor.ActorType, requests, cancellationToken: token);
                (ingress, reads, transport, pressure) = (foundation, foundation, requests, requests.Pressure);
            }
            else
            {
                await SqliteEmbeddedHosting.InstallShippedAsync(path!, token);
                (sqliteFile, hostedOptions, definitions) = HoldSqliteFile(path!);
                await using (var hosted = Open()) sqlite!.Path = hosted.Database.GetDbConnection().DataSource;
                var requests = SqliteEmbeddedHosting.CreateRequests(path!, Node, PublicActor.ActorType);
                ingress = await SqliteEmbeddedHosting.OpenFoundationWithRequestsAsync(path!, PublicActor.ActorType, requests, token);
                (reads, transport, pressure) = (SqliteEmbeddedHosting.CreateFoundationReads(path!), requests, requests.Pressure);
            }
            await using (var db = Observe())
            {
                var markers = await db.StoreVersions.AsNoTracking().OrderBy(row => row.Id).ToListAsync(token);
                Check(markers is [var family, _] && family.Format == StoreFormat.RootShipped && db.UsesRootDueBacklog,
                    "The fixture must run the shipped profile with its pressure capability.");
                Console.WriteLine($"{(postgres ? "PostgreSQL" : "SQLite")} shipped {markers[0].Format}, definitions " +
                    $"{markers[1].StoreSchema}/{markers[1].MessageProtocol}/{markers[1].JournalCodec}, owned SQL, " +
                    $"{(inBatch ? "in-batch" : "checked")} fence, one warm partition.");
            }

            var store = new EfStore(Open, postgres, inBatch) { CommitSql = CommitSql.Owned, AdmissionPressure = pressure };
            Check(store.OwnedCommit && store.FenceInBatch == inBatch && pressure is not null, "The measurement must use the requested owned writer.");
            await store.EnableRootSelfSendsAsync(token);
            await store.EnableRootJobsAsync(token);
            var definition = Reference.WriteLines(Lines);
            var model = new NativeExecutionModel(new WorkflowRegistry().Add(definition), new() { SelfSends = true });
            var actor = new PublicActor([model]);
            var roots = postgres ? store.BindWorkerNode(actor.Withholds) : store.BindSqliteWorkerNode(actor.Withholds);
            var adapter = new ActorAdapter(new EfStore.RootHeaders(roots), actor, store.RootSelfIssuer, rootJobHandle: store.RootJobHandle,
                rootTimedJobClock: store.ReadRootTimedJobPublicationTimeAsync, rootHeldReplies: true);
            var lease = TimeSpan.FromMinutes(2);
            // The cadence clock stays fixed across the three warm samples. Database time, ownership and all SQL remain real.
            var sampledAt = Environment.TickCount64;
            await using var worker = new Worker(Node, 0, store, adapter, new WorkerOptions
            {
                Partitions = 1, Lease = lease, Backlog = pressure!.Backlog, Transport = transport, Monotonic = () => sampledAt
            });
            var host = new DurableHost([worker]);
            await host.HeartbeatAsync(lease, token);
            Check(await host.ActivateAsync(0, token) == worker, "The benchmark owner could not take the partition.");
            await store.PrimeAdmissionPressureAsync(token);

            var scope = EmbeddedTenant.Bind(Node);
            var runtime = new WorkflowRuntime(ingress, reads);
            var spec = NativeExecutionModel.Start(definition.Id, Modes.Balanced);
            Console.WriteLine("Window starts before public admission and ends after the worker pass that lands revision 1. " +
                "Installation, take, pressure priming, warm-up and verification reads are outside the window.");
            var measured = new List<long>(Samples);
            for (var sample = 0; sample <= Samples; sample++)
            {
                using var stdout = new StringWriter();
                WriteLine.Out = stdout;
                if (sample > 0)
                {
                    counter.Trace = [];
                    sqlite?.Begin();
                }
                var baseline = Read();
                var accepted = await runtime.StartAsync(scope, $"budget-{sample}", spec, cancellationToken: token);
                Check(accepted is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "The public start was not admitted.");
                var reference = accepted.Actor!.Value;
                var admitted = Read();
                await worker.WokenAsync(null, token);
                var written = admitted;
                List<Measurement> passCosts = [];
                EfStore.RootImage? landed = null;
                var passes = 0;
                while (landed is null && passes < 8)
                {
                    if (sample > 0) sqlite?.Resume();
                    await worker.RunAsync(token);
                    passes++;
                    var afterPass = Read();
                    passCosts.Add(afterPass - written);
                    written = afterPass;
                    sqlite?.End();
                    landed = await observer.ReadFirstRootAsync(scope.Tenant, reference.Id, token);
                }

                Check(landed is { Revision: 1, TerminalRevision: 1, TerminalAt: > 0, Status: InstanceStatus.Finished },
                    "The measured passes must land the terminal checkpoint at revision 1.");
                Check(stdout.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                    .SequenceEqual(Enumerable.Range(1, Lines).Select(line => $"line {line}")),
                    "The ten Native WriteLines must execute once in authored order.");
                var journal = await reads.TailAsync(scope, reference, cancellationToken: token);
                var completions = journal.Items.Select(PostgresPublicPreview.Decode)
                    .Where(entry => entry.StartsWith("c:", StringComparison.Ordinal) && entry.Contains(":write-", StringComparison.Ordinal))
                    .Select(entry => entry[(entry.LastIndexOf(':') + 1)..]);
                Check(journal.Items.All(entry => entry.Revision == 1) &&
                    completions.SequenceEqual(Enumerable.Range(1, Lines).Select(line => $"write-{line}")) &&
                    journal.Items.Select(PostgresPublicPreview.Decode).Count(entry => entry == "end") == 1,
                    "The public journal must expose ten ordered completions and one end in revision 1.");
                if (sample == 0) continue;

                var total = written - baseline;
                var admission = admitted - baseline;
                var owner = written - admitted;
                if (postgres) Check(total.Connections == 0 && total.Groups > 0, "The sample must use warmed PostgreSQL connections.");
                else Check(total.Begun > 0 && total.Ended == total.Begun && total.Statements >= total.ProviderCalls,
                    $"The SQLite trace must cover both raw and EF transactions and the measured provider calls. {total}");
                Check(total.ProviderCalls > 0 && total.BudgetUnits(postgres) > 0, "The sample must contain measured database work.");
                measured.Add(total.BudgetUnits(postgres));
                Report($"sample {sample} admission", admission, postgres);
                Report($"sample {sample} worker poll and fenced commit ({passes} passes)", owner, postgres);
                Report($"sample {sample} total", total, postgres);
                if (sample == 1)
                {
                    for (var pass = 0; pass < passCosts.Count; pass++)
                        Report($"  worker pass {pass + 1}", passCosts[pass], postgres);
                    var sql = postgres ? counter.Trace! : sqlite!.Trace;
                    for (var i = 0; i < total.Commands; i++)
                        Console.WriteLine($"  {(i < admission.Commands ? "edge" : "owner")} {sql[i]}");
                }
            }
            var count = measured.Max();
            var budget = postgres ? 16 : 32;
            Console.WriteLine($"{Samples} warm samples: maximum {count} {(postgres ? "wire round trips" : "SQLite provider calls")}, " +
                $"budget {budget} ({(count <= budget ? "met" : "missed")}).");
            return requireBudget && count > budget ? 1 : 0;
        }
        finally
        {
            WriteLine.Out = previousOutput;
            sqliteFile?.Dispose();
            if (directory is not null)
            {
                SqliteConnection.ClearAllPools();
                directory.Delete(recursive: true);
            }
        }

        Measurement Read() => wire is null ? sqlite!.Read() : new(wire.Trips, wire.ProtocolRequests, wire.Connections,
            counter.Commands, counter.Transactions.Begun, counter.Transactions.Ended, 0, 0);
    }

    // The stepped owner shares the shipped host's file lease, options and verified definitions with its public edge.
    private static (IDisposable Lease, DbContextOptions<KernelDb> Options, SqliteAdmissionDefinitions Definitions) HoldSqliteFile(string path)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var lease = (IDisposable)typeof(SqliteEmbeddedHosting).GetMethod("AcquireFile", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [path, false, null])!;
        var type = lease.GetType();
        try
        {
            return (lease, (DbContextOptions<KernelDb>)type.GetProperty("Options", instance)!.GetValue(lease)!,
                (SqliteAdmissionDefinitions)type.GetProperty("AdmissionDefinitions", instance)!.GetValue(lease)!);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static void Check(bool satisfied, string message)
    {
        if (!satisfied) throw new InvalidOperationException(message);
    }

    private static void Report(string phase, Measurement m, bool postgres) => Console.WriteLine(postgres
        ? $"{phase}: {m.Trips} wire round trips, {m.Groups} Sync/Query groups, {m.Connections} connections, {m.Commands} EF commands, {m.Ended} transaction completions."
        : $"{phase}: {m.ProviderCalls} provider calls ({m.Commands} commands, {m.Begun} begins, {m.Ended} ends), " +
          $"{m.Statements} top-level SQLite statements, {m.Internal} internal trace records.");

    private readonly record struct Measurement(long Trips, long Groups, long Connections, int Commands, int Begun, int Ended,
        int Statements, int Internal)
    {
        internal int ProviderCalls => Commands + Begun + Ended;
        internal long BudgetUnits(bool postgres) => postgres ? Trips : ProviderCalls;

        public static Measurement operator -(Measurement after, Measurement before) => new(after.Trips - before.Trips,
            after.Groups - before.Groups, after.Connections - before.Connections, after.Commands - before.Commands,
            after.Begun - before.Begun, after.Ended - before.Ended, after.Statements - before.Statements, after.Internal - before.Internal);
    }

    private sealed class SqliteMeter : IDisposable
    {
        internal string Path { private get; set; }
        private readonly IDisposable _all;
        private IDisposable? _events;
        private bool _enabled;
        private int _begun, _ended, _statements, _internal;
        internal List<string> Trace { get; } = [];

        internal SqliteMeter(string path)
        {
            Path = path;
            _all = DiagnosticListener.AllListeners.Subscribe(new Observer<DiagnosticListener>(listener =>
            {
                if (listener.Name == "Microsoft.EntityFrameworkCore")
                    _events = listener.Subscribe(new Observer<KeyValuePair<string, object?>>(Observe));
            }));
        }

        internal void Begin()
        {
            (_begun, _ended, _statements, _internal) = (0, 0, 0, 0);
            Trace.Clear();
            _enabled = true;
        }

        internal void End() => _enabled = false;
        internal void Resume() => _enabled = true;

        internal Measurement Read() => new(0, 0, 0, Trace.Count, _begun, _ended, _statements, _internal);

        private void Observe(KeyValuePair<string, object?> entry)
        {
            if (entry.Key == RelationalEventId.ConnectionClosing.Name && entry.Value is ConnectionEventData closing &&
                closing.Connection is SqliteConnection releasing && releasing.DataSource == Path && releasing.Handle is { } handle)
                raw.sqlite3_trace(handle, (strdelegate_trace)null!, this);
            if (!_enabled) return;
            if (entry.Key == RelationalEventId.ConnectionOpened.Name && entry.Value is ConnectionEndEventData opened &&
                opened.Connection is SqliteConnection connection && connection.DataSource == Path)
                raw.sqlite3_trace(connection.Handle!, (strdelegate_trace)Statement, this);
            if (entry.Key == RelationalEventId.CommandExecuting.Name && entry.Value is CommandEventData command &&
                command.Command.Connection is SqliteConnection sql && sql.DataSource == Path)
            {
                var text = command.Command.CommandText;
                Trace.Add(text[..Math.Min(text.Length, 160)].ReplaceLineEndings(" "));
            }
        }

        // The public SQLite start begins and commits directly on SqliteConnection, so EF transaction events alone miss it.
        // Count top-level executed statements separately. Trigger and virtual table callbacks are reported as internal records.
        private void Statement(object _, string sql)
        {
            if (!_enabled) return;
            var text = sql.AsSpan().TrimStart();
            if (text.StartsWith("--", StringComparison.Ordinal))
            {
                _internal++;
                return;
            }
            _statements++;
            if (text.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase)) _begun++;
            if (text.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("ROLLBACK", StringComparison.OrdinalIgnoreCase)) _ended++;
        }

        public void Dispose()
        {
            _all.Dispose();
            _events?.Dispose();
        }
    }

    private sealed class Observer<T>(Action<T> receive) : IObserver<T>
    {
        public void OnNext(T value) => receive(value);
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
