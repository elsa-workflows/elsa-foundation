using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CShells;
using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;
using Elsa.Persistence.EntityFramework;
using Elsa.Locking.FileSystem;
using Elsa.Tasks;
using Elsa.Tasks.Core;
using Elsa.Etl.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Worker_acknowledges_terminal_delivery_only_after_adapter_returns_proof()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var delivery = new TestResultDelivery(EtlDeliveryDisposition.Consumed);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, resultDelivery: delivery);
        var invocation = files.Invocation(EtlTestPipelines.Create());
        await files.AdmitAsync(host, invocation);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var delivered = await delivery.WaitForDeliveryAsync(timeout.Token);
        Assert.Equal(invocation.RunId, delivered.RunId);
        Assert.Equal(invocation.ResultAddress, delivered.Address);
        var outbox = await files.WaitForOutboxAsync(host.Services, invocation.RunId,
            row => row.Disposition == (int)EtlDeliveryDisposition.Consumed, timeout.Token);
        Assert.Equal(delivered.DeliveryId, outbox.DeliveryId);
        Assert.True(outbox.Attempts >= 1);
        Assert.NotNull(outbox.AcknowledgedAtUtcTicks);
    }

    [Fact]
    public async Task Worker_leaves_delivery_owed_until_adapter_returns_a_disposition()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var delivery = new TestResultDelivery(null);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, resultDelivery: delivery, deliveryLeaseSeconds: 5);
        var invocation = files.Invocation(EtlTestPipelines.Create());
        await files.AdmitAsync(host, invocation);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var first = await delivery.WaitForDeliveryAsync(timeout.Token);
        var retry = await delivery.WaitForDeliveryAsync(timeout.Token);
        Assert.Equal(invocation.RunId, retry.RunId);
        Assert.True(retry.Fence > first.Fence);

        var outbox = await files.ReadOutboxAsync(host.Services, invocation.RunId);
        Assert.NotNull(outbox);
        Assert.True(outbox.Attempts >= 2);
        Assert.Null(outbox.Disposition);
        Assert.Null(outbox.AcknowledgedAtUtcTicks);
    }

    [Fact]
    public async Task Worker_does_not_claim_delivery_when_no_adapter_is_configured()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(EtlTestPipelines.Create());
        await files.AdmitAsync(host, invocation);
        await files.WaitForTerminalAsync(host, invocation.RunId);
        await Task.Delay(500);

        var outbox = await files.ReadOutboxAsync(host.Services, invocation.RunId);
        Assert.NotNull(outbox);
        Assert.Equal(0, outbox.Attempts);
        Assert.Null(outbox.Disposition);
        await using var operation = host.Services.CreateAsyncScope();
        var owed = await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>()
            .ClaimDeliveryAsync(files.Scope, "proof-check");
        Assert.NotNull(owed);
        Assert.Null((await files.ReadOutboxAsync(host.Services, invocation.RunId))!.Disposition);
    }

    [Fact]
    public async Task Admission_service_resolves_trusted_bindings_and_reuses_the_first_pinned_revisions()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var pipeline = EtlTestPipelines.Create();
        var invocation = files.Invocation(pipeline);
        var firstHost = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, bindingRevision: "revision-one");

        EtlRunBindings firstBindings;
        await using (var operation = firstHost.Services.CreateAsyncScope())
        {
            var service = operation.ServiceProvider.GetRequiredService<IEtlRunAdmissionService>();
            Assert.Same(operation.ServiceProvider.GetRequiredService<EtlExecution>(), service);
            var result = await service.AdmitAsync(invocation);
            Assert.Equal(EtlStoreStatus.Applied, result.Status);
            firstBindings = Assert.IsType<EtlRunBindings>(Assert.IsType<EtlRunView>(result.Run).Bindings);
        }

        Assert.Equal("source", firstBindings.Source.Alias);
        Assert.Equal("revision-one", firstBindings.Source.Revision);
        Assert.False(string.IsNullOrWhiteSpace(firstBindings.Source.Handle));
        Assert.False(string.IsNullOrWhiteSpace(firstBindings.Source.ExpectedSha256));
        await files.WaitForTerminalAsync(firstHost, invocation.RunId);

        var secondHost = await files.StartHostAsync(EfMigratePolicy.Validate, bindingRevision: "revision-two");
        await using var secondOperation = secondHost.Services.CreateAsyncScope();
        var currentSource = secondOperation.ServiceProvider.GetRequiredService<HostFileBindings>()
            .Resolve(files.Scope, pipeline.Bindings.Source, EtlFileRole.Source);
        Assert.Equal("revision-two", currentSource.Revision);
        Assert.NotEqual(firstBindings.Source.Handle, currentSource.Handle);

        var replay = await secondOperation.ServiceProvider.GetRequiredService<IEtlRunAdmissionService>().AdmitAsync(invocation);
        Assert.Equal(EtlStoreStatus.Replay, replay.Status);
        var replayBindings = Assert.IsType<EtlRunBindings>(Assert.IsType<EtlRunView>(replay.Run).Bindings);
        Assert.Equal(firstBindings, replayBindings);
        Assert.Equal("revision-one", replayBindings.Source.Revision);
    }

    [Fact]
    public async Task Real_shell_starts_the_worker_and_awaits_its_unwind_when_disposed()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, useShell: true);
        using var activation = new CancellationTokenSource();
        var shell = await host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync("etl", activation.Token);
        await activation.CancelAsync();
        var worker = Assert.Single(shell.ServiceProvider.GetServices<IBackgroundTask>().OfType<EtlRuntimeWorker>());
        var execution = Assert.IsAssignableFrom<Task>(worker.ExecuteTask);
        await worker.StartAsync(CancellationToken.None);
        Assert.Same(execution, worker.ExecuteTask);
        var invocation = files.Invocation(EtlTestPipelines.Create());
        await files.AdmitAsync(shell.ServiceProvider, invocation);
        var run = await files.WaitForTerminalAsync(shell.ServiceProvider, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, run.Status);
        Assert.Equal(1, run.AcceptedRecords);
        await files.StopHostAsync(host);
        Assert.True(execution.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Two_hosts_execute_a_published_artifact_and_reopen_accepted_output_without_design_services()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n2,skip\n99,unknown\n3,Bram\n");
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = new EtlLimits { MaxBatchRecords = 1 },
            Filters = [new("name", EtlComparison.NotEqual, JsonSerializer.SerializeToElement("skip"))]
        };
        var invocation = files.Invocation(pipeline);
        var first = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var second = await files.StartHostAsync(EfMigratePolicy.Validate);
        await using (var operation = first.Services.CreateAsyncScope())
        {
            Assert.Null(operation.ServiceProvider.GetService<IEtlArtifactCompiler>());
            Assert.Null(operation.ServiceProvider.GetService<IEtlDefinitionStore>());
            Assert.Null(operation.ServiceProvider.GetService<EtlDesignDbContext>());
            Assert.Equal(EtlStoreStatus.Applied,
                (await operation.ServiceProvider.GetRequiredService<EtlExecution>().AdmitAsync(invocation)).Status);
        }
        var run = await files.WaitForTerminalAsync(second, invocation.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.Equal(2, run.AcceptedRecords);
        Assert.Equal(1, run.RejectedRecords);
        Assert.Equal(1, run.FilteredRecords);
        Assert.Equal(0, run.OutstandingRecords);
        Assert.Equal(0, run.OutstandingBytes);
        await using (var operation = second.Services.CreateAsyncScope())
        {
            var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            var partitions = await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 10);
            Assert.Equal(2, partitions.Count);
            Assert.All(partitions, partition => Assert.Equal(partition.EndOrdinalExclusive, partition.NextOrdinal));
            var manifest = await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10);
            Assert.Equal(3, manifest.Count);
            Assert.Equal("{\"name\":\"Ada\"}\n{\"name\":\"Bram\"}\n", await files.ReadRouteAsync(manifest, EtlOutputRoute.Main));
            Assert.Equal("{\"recordOrdinal\":3,\"code\":\"etl.lookup-key-not-found\",\"field\":\"id\"}\n",
                await files.ReadRouteAsync(manifest, EtlOutputRoute.Error));
        }
        await files.StopHostAsync(first);
        await files.StopHostAsync(second);
        var reopened = await files.StartHostAsync(EfMigratePolicy.Validate, includeBindings: false);
        await using (var operation = reopened.Services.CreateAsyncScope())
        {
            var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
            var retained = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId));
            Assert.Equal(run.TerminalResult, retained.TerminalResult);
            var delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(files.Scope, "native-consumer"));
            Assert.Equal(invocation.ResultAddress, delivery.Address);
            Assert.Equal(EtlTerminalOutcome.BusinessError, delivery.Result.Outcome);
            // No native parent exists in this test. Delivery remains owed rather than pretending a queued resume was consumed.
            var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
            await db.Database.OpenConnectionAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('elsa_etl_definition_drafts','elsa_etl_published_artifacts')";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Empty_input_settles_and_has_no_partition_or_output_work()
    {
        await using var files = new RuntimeFiles("id,name\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(EtlTestPipelines.Create());
        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, run.Status);
        Assert.Equal(0, run.AcceptedRecords);
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        Assert.Empty(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 10));
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10));
        Assert.NotNull(await store.ClaimDeliveryAsync(files.Scope, "native-consumer"));
    }

    [Fact]
    public async Task Preparation_errors_with_long_authored_field_names_still_settle_a_bounded_result()
    {
        await using var files = new RuntimeFiles("id\n1\n");
        var pipeline = EtlTestPipelines.Create() with
        {
            SourceSchema = [new("id", EtlColumnType.Int64, true), new(new string('x', 600), EtlColumnType.String, false)],
            Mapping = [new("name", EtlFieldOrigin.Source, "id")]
        };
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(pipeline);
        await files.AdmitAsync(host, invocation);
        var run = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, run.Status);
        Assert.NotNull(run.TerminalResult!.Diagnostic);
        Assert.Null(run.TerminalResult.Diagnostic.Field);
        Assert.False(Directory.Exists(files.OutputRoot));
    }

    [Fact]
    public async Task Missing_host_binding_is_a_durable_refusal_and_cancel_before_admission_stays_cancelled()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false);
        var refused = files.Invocation(EtlTestPipelines.Create(), "missing-binding");
        await files.AdmitAsync(host, refused, EtlStoreStatus.Replay);
        var failed = await files.WaitForTerminalAsync(host, refused.RunId);
        Assert.Equal(EtlRunStatus.BusinessError, failed.Status);
        Assert.Equal("etl.binding-unavailable", failed.TerminalResult!.Diagnostic!.Code);
        var cancelled = files.Invocation(EtlTestPipelines.Create(), "cancel-first");
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied, (await store.CancelAsync(cancelled)).Status);
        Assert.Equal(EtlRunStatus.Cancelled,
            (await operation.ServiceProvider.GetRequiredService<EtlExecution>().AdmitAsync(cancelled)).Run!.Status);
        Assert.Empty(await store.ListPartitionsAsync(files.Scope, cancelled.RunId, -1, 10));
        Assert.False(Directory.Exists(files.SnapshotRoot));
    }

    private sealed class RuntimeFiles : IAsyncDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.RuntimeJourney", Guid.NewGuid().ToString("N"));
        private readonly List<IHost> _hosts = [];
        private readonly string _source;
        private readonly string _lookup;
        public EtlScope Scope { get; } = new(null, "runtime-journey");
        public string SnapshotRoot => Path.Combine(_directory, "snapshots");
        public string OutputRoot => Path.Combine(_directory, "output");
        public string ErrorRoot => Path.Combine(_directory, "errors");

        public RuntimeFiles(string source)
        {
            Directory.CreateDirectory(_directory);
            _source = Path.Combine(_directory, "source.csv");
            _lookup = Path.Combine(_directory, "lookup.csv");
            File.WriteAllText(_source, source, new UTF8Encoding(false));
            File.WriteAllText(_lookup, "id,category\n1,one\n2,two\n3,three\n", new UTF8Encoding(false));
        }

        public EtlInvocation Invocation(EtlPipeline pipeline, string runId = "run")
        {
            var artifact = Assert.IsType<EtlArtifact>(new EtlArtifactCompiler().Compile("orders", 1, pipeline).Artifact);
            return EtlRuntimeFixtures.Admission(runId, Scope).Invocation with { Artifact = artifact };
        }

        public async Task<IHost> StartHostAsync(EfMigratePolicy policy, bool includeBindings = true, bool useShell = false,
            string bindingRevision = "v1", IEtlResultDelivery? resultDelivery = null, int deliveryLeaseSeconds = 30, int maximumPageSize = 100,
            Action<IServiceCollection>? configureServices = null)
        {
            var builder = Host.CreateApplicationBuilder();
            var runtime = new EtlRuntimeFeature
            {
                SnapshotRoot = SnapshotRoot,
                FileBindings = includeBindings ? [Binding("source", "Source", _source, bindingRevision), Binding("lookup", "Lookup", _lookup, bindingRevision),
                    Binding("output", "MainOutput", OutputRoot, bindingRevision), Binding("errors", "ErrorOutput", ErrorRoot, bindingRevision)] : []
            };
            var persistence = new EtlRuntimeEntityFrameworkCoreFeature
            {
                ConnectionString = $"Data Source={Path.Combine(_directory, "runtime.db")};Pooling=False",
                DeliveryLeaseSeconds = deliveryLeaseSeconds,
                MaximumPageSize = maximumPageSize
            };
            if (useShell)
                builder.Services.AddCShells(shells => shells
                    .WithAssemblies(typeof(EtlRuntimeFeature).Assembly, typeof(EtlRuntimeEntityFrameworkCoreFeature).Assembly,
                        typeof(TasksFeature).Assembly, typeof(FileSystemLockingFeature).Assembly)
                    .AddShell("etl", shell => shell
                        .WithFeature<EtlRuntimeFeature>(feature =>
                        {
                            feature.SnapshotRoot = runtime.SnapshotRoot;
                            feature.FileBindings = runtime.FileBindings;
                        })
                        .WithFeature<EtlRuntimeEntityFrameworkCoreFeature>(feature => feature.ConnectionString = persistence.ConnectionString)
                        .WithFeature<FileSystemLockingFeature>(feature => feature.LocksFolderPath = Path.Combine(_directory, "locks"))));
            else
            {
                runtime.ConfigureServices(builder.Services);
                persistence.ConfigureServices(builder.Services);
            }
            if (resultDelivery is not null)
                builder.Services.AddSingleton<IEtlResultDelivery>(resultDelivery);
            configureServices?.Invoke(builder.Services);
            builder.Services.Configure<EfMigrateOptions>(options => options.Policy = policy);
            var host = builder.Build();
            _hosts.Add(host);
            await host.StartAsync();
            return host;
        }

        public Task AdmitAsync(IHost host, EtlInvocation invocation, EtlStoreStatus expected = EtlStoreStatus.Applied) =>
            AdmitAsync(host.Services, invocation, expected);

        public async Task AdmitAsync(IServiceProvider services, EtlInvocation invocation, EtlStoreStatus expected = EtlStoreStatus.Applied)
        {
            await using var operation = services.CreateAsyncScope();
            Assert.Equal(expected, (await operation.ServiceProvider.GetRequiredService<EtlExecution>().AdmitAsync(invocation)).Status);
        }

        public Task<EtlRunView> WaitForTerminalAsync(IHost host, string runId) => WaitForTerminalAsync(host.Services, runId);

        public async Task<EtlRunView> WaitForTerminalAsync(IServiceProvider services, string runId)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                await using var operation = services.CreateAsyncScope();
                var run = await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().FindRunAsync(Scope, runId, deadline.Token);
                if (run?.TerminalResult is not null)
                    return run;
                await Task.Delay(25, deadline.Token);
            }
        }

        public async Task<EtlRuntimeOutboxRecord?> ReadOutboxAsync(IServiceProvider services, string runId)
        {
            await using var operation = services.CreateAsyncScope();
            var scopeKey = EtlRuntimeIdentity.ScopeKey(Scope);
            var runKey = EtlRuntimeRecordCodec.EncodeIdentity(runId, "run id");
            return await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().Outbox.AsNoTracking()
                .SingleOrDefaultAsync(row => row.ScopeKey == scopeKey && row.RunKey == runKey);
        }

        public async Task<EtlRuntimeOutboxRecord> WaitForOutboxAsync(IServiceProvider services, string runId,
            Func<EtlRuntimeOutboxRecord, bool> condition, CancellationToken cancellationToken)
        {
            while (true)
            {
                var row = await ReadOutboxAsync(services, runId);
                if (row is not null && condition(row))
                    return row;
                await Task.Delay(25, cancellationToken);
            }
        }

        public async Task<string> ReadRouteAsync(IReadOnlyList<EtlManifestEntry> manifest, EtlOutputRoute route)
        {
            var result = new StringBuilder();
            foreach (var entry in manifest.Where(entry => entry.Effect.Route == route))
            {
                var path = Path.Combine(route == EtlOutputRoute.Main ? OutputRoot : ErrorRoot, entry.Effect.Chunk.Key);
                var bytes = await File.ReadAllBytesAsync(path);
                Assert.Equal(entry.Effect.Chunk.ByteLength, bytes.LongLength);
                Assert.Equal(entry.Effect.Chunk.ContentHash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
                result.Append(Encoding.UTF8.GetString(bytes));
            }
            return result.ToString();
        }

        public async Task StopHostAsync(IHost host)
        {
            await host.StopAsync();
            host.Dispose();
            _hosts.Remove(host);
        }

        private EtlHostFileBindingOptions Binding(string alias, string role, string path, string revision) => new()
        {
            TenantId = Scope.TenantId, PersistencePartition = Scope.PersistencePartition, Alias = alias,
            Role = role, Revision = revision, Path = path, IsCurrent = true,
            ExpectedSha256 = role is "Source" or "Lookup" ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() : null,
            ImmutableInput = role is "Source" or "Lookup", AtomicPublication = role is "MainOutput" or "ErrorOutput"
        };

        public async ValueTask DisposeAsync()
        {
            foreach (var host in _hosts.ToArray())
                await StopHostAsync(host);
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class TestResultDelivery(EtlDeliveryDisposition? disposition) : IEtlResultDelivery
    {
        private readonly Channel<EtlDeliveryLease> _deliveries = Channel.CreateUnbounded<EtlDeliveryLease>();

        public ValueTask<EtlDeliveryDisposition?> DeliverAsync(EtlDeliveryLease delivery, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _deliveries.Writer.TryWrite(delivery);
            return ValueTask.FromResult(disposition);
        }

        public Task<EtlDeliveryLease> WaitForDeliveryAsync(CancellationToken cancellationToken = default) =>
            _deliveries.Reader.ReadAsync(cancellationToken).AsTask();
    }
}
