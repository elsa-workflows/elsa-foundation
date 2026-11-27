using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Elsa.Events;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Scheduling;
using Kernel.Foundation.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using Contract = Kernel.Foundation.Contracts;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed partial class KernelStandalonePreparedStartTests
{
    private const string WaitSourceFamily = EventFamily + ".UnqualifiedWait";

    [Fact]
    public Task Sqlite_neutral_runtime_admits_and_drains_only_prepared_standalone_sources() => RunNeutralAsync(false);

    [SkippableFact]
    public Task PostgreSql_neutral_runtime_admits_and_drains_only_prepared_standalone_sources()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)),
            "Requires dedicated Foundation and Runtime PostgreSQL databases.");
        return RunNeutralAsync(true);
    }

    private static async Task RunNeutralAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-standalone-neutral");
        try
        {
            var provider = postgres ? "PostgreSql" : "Sqlite";
            var path = directory is null ? null : Path.Combine(directory.FullName, "foundation.db");
            var foundation = postgres ? Environment.GetEnvironmentVariable(FoundationDatabase)! : null;
            var runtime = postgres ? Environment.GetEnvironmentVariable(RuntimeDatabase)!
                : new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false }.ToString();
            if (postgres)
                Assert.NotEqual(new NpgsqlConnectionStringBuilder(foundation).Database, new NpgsqlConnectionStringBuilder(runtime).Database);
            var clock = new TestClock(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            var model = ExecutableOf("standalone-neutral-native", Definition, SetOutput("set-result", "result", Literal("value", "runner-free")));
            var material = new WorkflowExecutable(model.Identity, model.RootActivity, model.ResumeTargets, model.CreatedAt,
                model.CompatibilityMetadata, new(WorkflowExecutableInputContract.CurrentVersion, []), model.Dependencies, model.IncidentStrategy);
            var nextModel = ExecutableOf("standalone-neutral-replacement", Definition,
                SetOutput("set-result", "result", Literal("value", "replacement")));
            var current = new WorkflowExecutable(nextModel.Identity with { ArtifactVersion = "2", DefinitionVersionId = "version-2" },
                nextModel.RootActivity, nextModel.ResumeTargets, nextModel.CreatedAt, nextModel.CompatibilityMetadata,
                material.InputContract!, nextModel.Dependencies, nextModel.IncidentStrategy);
            var signal = Signal("neutral-prepared-event");
            var legacySignal = Signal("old-pending-event");
            var waitsSignal = Signal("unqualified-waits");
            string sourceId;
            string legacyId;
            string legacySourceFingerprint;

            await using (var host = OpenHost(neutral: true))
            {
                await using var operation = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var services = operation.ServiceProvider;
                Assert.Null(services.GetService<IWorkflowExecutionStateStore>());
                Assert.Null(services.GetService<IWorkflowSchedulerWorkQueue>());
                Assert.Null(services.GetService<IRuntimeCheckpointCommitStore>());
                Assert.Null(services.GetService<IWorkflowStartDispatcher>());
                Assert.Null(services.GetService<IRuntimeIngressWaitTarget>());
                await services.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
                var activation = await PublishAsync(services, material, 0, clock.GetUtcNow(), token);
                var ingress = services.GetRequiredService<IRuntimeIngressDeliveryStore>();
                Assert.True(ingress.SupportsAdmission);
                var resource = services.GetRequiredService<ITriggerSourceResourceCommitStore>();
                Assert.True(resource.SupportsActivatedType("event"));
                await WriteEventAsync(resource, activation);
                var accepted = await Source(services).AdmitAsync(signal, token);
                sourceId = accepted.AdmissionId;
                Assert.False(accepted.Replayed);
                var item = await ReadAsync(services, sourceId, token);
                Assert.Equal(material.Identity, Assert.Single(item.SubscriptionDelivery!.Recipients).Material);
                Assert.Equal(RuntimeRecipientPhase.Pending, Progress(item).Phase);
                Assert.True((await Source(services).AdmitAsync(signal, token)).Replayed);

                await Assert.ThrowsAsync<NotSupportedException>(() => services.GetRequiredService<IStandaloneTriggerIngress<UnqualifiedWaitEvent>>()
                    .AdmitAsync(waitsSignal, token).AsTask());
                Assert.Null(await ingress.FindDeliveryAsync(DeliveryScope, SourceId(waitsSignal), token));
                var processor = services.GetRequiredService<IRuntimePostCommitOutboxProcessor>();
                Assert.False(processor.SupportsSourceKind(RuntimeDeliverySourceKind.WorkflowCheckpoint));
                await Assert.ThrowsAsync<NotSupportedException>(() => processor.ProcessAsync(new(1, intentKind: "other"), token).AsTask());
                var replacement = await PublishAsync(services, current, activation.Revision, clock.GetUtcNow(), token);
                Assert.NotEqual(activation.ActiveActivationId, replacement.ActiveActivationId);
            }

            await using (var host = OpenHost(neutral: true))
            {
                var shell = host.GetRequiredService<KernelShellRuntime>();
                await shell.StartAsync(token);
                try
                {
                    await using var operation = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                    var services = operation.ServiceProvider;
                    var replay = await Source(services).AdmitAsync(signal, token);
                    Assert.True(replay.Replayed);
                    Assert.Equal(sourceId, replay.AdmissionId);
                    var pump = Assert.Single(host.GetServices<IRecurringTask>());
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump.ExecuteAsync(new(canceled: true)));
                    Assert.Equal(RuntimeRecipientPhase.Pending, Progress(await ReadAsync(services, sourceId, token)).Phase);
                    await RunScheduledSweepAsync(pump, token);
                    var accepted = Assert.IsType<RuntimePostCommitOutboxItem>(await services.GetRequiredService<IRuntimeIngressDeliveryStore>()
                        .FindDeliveryAsync(DeliveryScope, sourceId, token));
                    var receipt = Assert.IsType<RuntimeTargetAcceptanceReceipt>(Progress(accepted).Receipt);
                    Assert.Equal(RuntimeTargetAcceptanceKind.PreparedActorBirth, receipt.Kind);
                    Assert.Equal(material.Identity, receipt.Material);
                    var reads = await shell.ReadAsync(token);
                    var owner = EmbeddedTenant.Bind(Persistence.Value);
                    var finished = await WaitForAsync(reads, owner, new(receipt.EvidenceId),
                        actor => actor.Header.Status == Contract.ActorStatus.Finished, token);
                    var result = await Assert.IsAssignableFrom<Contract.IActorTerminalOutputReads>(reads)
                        .ReadTerminalOutputAsync(owner, finished.Header.Ref, Assert.IsType<long>(finished.Header.EndedAt), includeValues: true, cancellationToken: token);
                    using var output = JsonDocument.Parse(Assert.IsType<byte[]>(result?.Values));
                    Assert.Equal("runner-free", output.RootElement.GetProperty("result").GetString());
                    Assert.Equal(0, (await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(1), token)).AttemptedCount);
                    Assert.Equal(finished.Header.Ref, Assert.Single((await reads.ListAsync(owner, new(), cancellationToken: token)).Items).Header.Ref);
                }
                finally { await StopAsync(shell); }
            }

            // A previous owner can still accept using its legacy pipeline. The neutral owner must leave that row for it.
            await using (var host = OpenHost(neutral: false))
            {
                await using var operation = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var old = await Source(operation.ServiceProvider).AdmitAsync(legacySignal, token);
                Assert.False(old.Replayed);
                legacyId = old.AdmissionId;
                var item = await ReadAsync(operation.ServiceProvider, legacyId, token);
                Assert.Equal(RuntimeRecipientPhase.Pending, Progress(item).Phase);
                legacySourceFingerprint = item.SourceFingerprint;
            }
            await using (var host = OpenHost(neutral: true))
            {
                await using var operation = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var services = operation.ServiceProvider;
                await Assert.ThrowsAsync<NotSupportedException>(() => Source(services).AdmitAsync(legacySignal, token).AsTask());
                await Assert.ThrowsAsync<NotSupportedException>(() => services.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                    .ProcessAsync(new(10), token).AsTask());
            }
            await using (var host = OpenHost(neutral: false))
            {
                await using var operation = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var prior = await Source(operation.ServiceProvider).AdmitAsync(legacySignal, token);
                Assert.True(prior.Replayed);
                var item = await ReadAsync(operation.ServiceProvider, prior.AdmissionId, token);
                Assert.Equal((legacyId, legacySourceFingerprint, RuntimeRecipientPhase.Pending),
                    (item.OutboxItemId, item.SourceFingerprint, Progress(item).Phase));
                Assert.Equal((0, 0L), (item.DeliveryAttemptCount, item.DeliveryFencingToken));
                Assert.Null(item.DeliveringOwnerId);
            }

            ServiceProvider OpenHost(bool neutral)
            {
                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IWellKnownTypeRegistry, KernelPublishedExecuteTests.FixtureTypes>();
                new EventsFeature().ConfigureServices(services);
                if (neutral)
                {
                    services.AddWorkflowActivationDefaults();
                    new RuntimeArtifactsEntityFrameworkCoreFeature
                    {
                        Provider = provider, ConnectionString = runtime, RecoveryContinuationSigningKey = new string('r', 32)
                    }.ConfigureServices(services);
                    new RuntimePublicationEntityFrameworkCoreFeature().ConfigureServices(services);
                    new RuntimePreparedStandaloneEntityFrameworkCoreFeature().ConfigureServices(services);
                    new WorkflowsRuntimePreparedStandaloneSourcePumpFeature().ConfigureServices(services);
                }
                else
                {
                    services.AddWorkflowRuntime();
                    services.AddRuntimeEntityFrameworkCore(new()
                    {
                        Provider = provider, ConnectionString = runtime,
                        RecoveryContinuationSigningKey = new string('r', 32), HierarchyCursorSigningKey = new string('h', 32)
                    });
                }
                services.AddStandaloneTriggerIngress<FixtureEvent>(new("event", 1, EventFamily, EventSchema));
                services.AddScoped<IStandaloneTriggerSourceContext<FixtureEvent>, EventContext>();
                services.AddStandaloneTriggerIngress<UnqualifiedWaitEvent>(new("event", 1, WaitSourceFamily, EventSchema));
                services.AddScoped<IStandaloneTriggerSourceContext<UnqualifiedWaitEvent>, UnqualifiedWaitEventContext>();
                new WorkflowsRuntimeKernelHostingFeature
                {
                    Provider = provider, SqlitePath = path, PostgresConnectionString = foundation, PreparedStandaloneStarts = neutral
                }.ConfigureServices(services);
                var kernel = new WorkflowsRuntimeKernelFeature();
                kernel.ConfigureServices(services);
                kernel.PostConfigureServices(services);
                return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            }

            async Task WriteEventAsync(ITriggerSourceResourceCommitStore store, WorkflowActivationSlot activation)
            {
                var now = clock.GetUtcNow();
                var origin = new TriggerManagementOrigin(TriggerManagementKind.Studio);
                var trigger = new StandaloneTrigger(EventId, EventId, "event", 1, JsonSerializer.SerializeToElement(new { name = EventId }),
                    1, 1, 1, TriggerDesiredState.Enabled, origin, now, now, "qualification", tenantId: Persistence.Value);
                var subscription = new TriggerSubscription(EventId + "-subscription", EventId, Definition, KernelServing.DefaultSlotName, "native",
                    1, 1, true, [], origin, new(EventId + "-grant", EventId, EventId + "-subscription", Definition, KernelServing.DefaultSlotName,
                        "native", "event", 1, TriggerGrantStatus.Granted, "qualification", now));
                var observedTarget = new TriggerTargetObservation(Definition, activation.SlotName, activation.Revision,
                    activation.ActiveActivationId, RuntimeIngressAdmission.ComputeInputContractFingerprint(material.InputContract!),
                    "native", TriggerTargetState.Available);
                var receipt = await store.CommitAsync(DeliveryScope,
                    new("neutral-create", "neutral-create", "qualification", null, trigger, [subscription])
                    {
                        ObservedTargets = [observedTarget]
                    }, [], token);
                Assert.Equal((EventId, 1L), (receipt.TriggerId, receipt.Revision));
            }
        }
        finally { directory?.Delete(recursive: true); }

        static async Task RunScheduledSweepAsync(IRecurringTask pump, CancellationToken token)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var scheduled = pump.GetSchedule().ScheduleExecution(async () =>
            {
                try
                {
                    await pump.ExecuteAsync(token);
                    completed.TrySetResult();
                }
                catch (Exception exception)
                {
                    completed.TrySetException(exception);
                }
            });
            await completed.Task.WaitAsync(token);
        }

        static IStandaloneTriggerIngress<FixtureEvent> Source(IServiceProvider provider) =>
            provider.GetRequiredService<IStandaloneTriggerIngress<FixtureEvent>>();

        static async ValueTask<RuntimePostCommitOutboxItem> ReadAsync(IServiceProvider services, string sourceId, CancellationToken token) =>
            Assert.IsType<RuntimePostCommitOutboxItem>(await services.GetRequiredService<IRuntimeIngressDeliveryStore>()
                .FindDeliveryAsync(DeliveryScope, sourceId, token));

        static StandaloneTriggerSignal Signal(string key) =>
            new(EventId, key, EventSchema, JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }));

        static string SourceId(StandaloneTriggerSignal signal) =>
            RuntimeStandaloneIngressSource.BuildOutboxItemId(Persistence, Persistence.Value, EventId, WaitSourceFamily, signal.SourceKey);
    }

    private sealed class UnqualifiedWaitEvent;

    private sealed class UnqualifiedWaitEventContext : IStandaloneTriggerSourceContext<UnqualifiedWaitEvent>
    {
        private readonly EventContext source = new();

        public bool SupportsResourceAuthorization => true;

        public async ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default) =>
            (await source.GetAsync(cancellationToken)) with
            {
                WaitCandidate = new("event", "unqualified-waits", null, new Dictionary<string, string>())
            };

        public ValueTask<bool> AuthorizeAsync(StandaloneTrigger trigger, StandaloneTriggerSignal signal, CancellationToken cancellationToken = default) =>
            source.AuthorizeAsync(trigger, signal, cancellationToken);
    }
}
