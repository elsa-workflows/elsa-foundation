using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Elsa.Activities.Primitives;
using Elsa.Activities.Scheduling.StandaloneTriggers;
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
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Workflows.Runtime.Scheduling.Options;
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
                var directTests = Assert.IsType<KernelDirectTestRunDispatcher>(services.GetRequiredService<IWorkflowDirectTestRunDispatcher>());
                Assert.Same(directTests, services.GetRequiredService<IWorkflowStartDispatcher>());
                Assert.Null(services.GetService<IRuntimeIngressWaitTarget>());
                Assert.Null(services.GetService<IStimulusRouter>());
                await services.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
                var activation = await PublishAsync(services, material, 0, clock.GetUtcNow(), token);
                var ingress = services.GetRequiredService<IRuntimeIngressDeliveryStore>();
                Assert.True(ingress.SupportsAdmission);
                var resource = services.GetRequiredService<ITriggerSourceResourceCommitStore>();
                Assert.True(resource.SupportsActivatedType("event"));
                await WriteTriggerAsync(resource, activation, EventId, "event", new { eventName = EventId });
                var accepted = await Source(services).AdmitAsync(signal, token);
                sourceId = accepted.AdmissionId;
                Assert.False(accepted.Replayed);
                var item = await ReadAsync(services, sourceId, token);
                Assert.Equal(material.Identity, Assert.Single(item.SubscriptionDelivery!.Recipients).Material);
                Assert.Equal(RuntimeRecipientPhase.Pending, Progress(item).Phase);
                Assert.Contains(material.Identity.ArtifactId, await services.GetRequiredService<EfRuntimePostCommitOutboxStore>()
                    .ListPinnedExecutableArtifactIdsAsync(token));
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
                    var tasks = host.GetServices<IRecurringTask>().ToArray();
                    var pump = Assert.Single(tasks, task => task is not RecurringTriggerPumpTask);
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump.ExecuteAsync(new(canceled: true)));
                    Assert.Equal(RuntimeRecipientPhase.Pending, Progress(await ReadAsync(services, sourceId, token)).Phase);
                    await RunScheduledSweepAsync(pump, token);
                    var accepted = Assert.IsType<RuntimePostCommitOutboxItem>(await services.GetRequiredService<IRuntimeIngressDeliveryStore>()
                        .FindDeliveryAsync(DeliveryScope, sourceId, token));
                    var receipt = Assert.IsType<RuntimeTargetAcceptanceReceipt>(Progress(accepted).Receipt);
                    Assert.Equal(RuntimeTargetAcceptanceKind.PreparedActorBirth, receipt.Kind);
                    Assert.Equal(material.Identity, receipt.Material);
                    Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, accepted.Status);
                    Assert.DoesNotContain(material.Identity.ArtifactId, await services.GetRequiredService<EfRuntimePostCommitOutboxStore>()
                        .ListPinnedExecutableArtifactIdsAsync(token));
                    Assert.Contains(material.Identity.ArtifactId, await services.GetRequiredService<IRuntimeKernelExecutablePinStore>()
                        .ListPinnedExecutableArtifactIdsAsync(token));
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
                    var recurrence = Assert.Single(tasks.OfType<RecurringTriggerPumpTask>());
                    await AssertResourceRecurrenceAsync(services, reads, owner, recurrence, pump);
                    await AssertAuthoredEventCohortAsync(host, services, pump);
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

            ServiceProvider OpenHost(bool neutral, bool authoredOnly = false, PersistenceScope? relayScope = null)
            {
                var services = new ServiceCollection();
                services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IWellKnownTypeRegistry, KernelPublishedExecuteTests.FixtureTypes>();
                new EventsFeature().ConfigureServices(services);
                if (neutral)
                {
                    new ActivitiesPrimitivesFeature().ConfigureServices(services);
                    services.AddWorkflowActivationDefaults();
                    new RuntimeArtifactsEntityFrameworkCoreFeature
                    {
                        Provider = provider, ConnectionString = runtime, RecoveryContinuationSigningKey = new string('r', 32)
                    }.ConfigureServices(services);
                    new RuntimePublicationEntityFrameworkCoreFeature().ConfigureServices(services);
                    new RuntimePreparedStandaloneEntityFrameworkCoreFeature().ConfigureServices(services);
                    new WorkflowsRuntimePreparedStandaloneSourcePumpFeature().ConfigureServices(services);
                    new WorkflowsRuntimePreparedStandaloneRecurringTriggersFeature().ConfigureServices(services);
                    new PreparedStandaloneScheduleTriggersFeature().ConfigureServices(services);
                    services.Configure<RecurringTriggerPumpOptions>(options => options.MaxSchedulesPerTick = 1);
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
                if (!authoredOnly)
                {
                    services.AddStandaloneTriggerIngress<FixtureEvent>(new("event", 1, EventFamily, EventSchema));
                    services.AddScoped<IStandaloneTriggerSourceContext<FixtureEvent>, EventContext>();
                    services.AddStandaloneTriggerIngress<UnqualifiedWaitEvent>(new("event", 1, WaitSourceFamily, EventSchema));
                    services.AddScoped<IStandaloneTriggerSourceContext<UnqualifiedWaitEvent>, UnqualifiedWaitEventContext>();
                }
                new WorkflowsRuntimeKernelHostingFeature
                {
                    Provider = provider, SqlitePath = path, PostgresConnectionString = foundation, PreparedStandaloneStarts = neutral
                }.ConfigureServices(services);
                if (relayScope is not null)
                    services.AddSingleton<IRuntimeAuthoredStandaloneEventAvailability>(new PublishedEventAvailability(relayScope));
                var kernel = new WorkflowsRuntimeKernelFeature();
                kernel.ConfigureServices(services);
                kernel.PostConfigureServices(services);
                return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            }

            async Task AssertAuthoredEventCohortAsync(ServiceProvider host, IServiceProvider services, IRecurringTask pump)
            {
                const string eventName = "actor-origin";
                const string family = "Elsa.Event.Broadcast";
                const string first = "authored-event-a";
                const string second = "authored-event-b";
                const string later = "authored-event-c";
                var firstEvent = new RuntimeAuthoredStandaloneEvent(DeliveryScope, new(Persistence.Value), new string('A', 64),
                    new string('B', 64), eventName, JsonSerializer.SerializeToElement(new { order = "A-42" }), clock.GetUtcNow(),
                    new("workflow:publisher", "subject:owner", new Dictionary<string, string>()));
                var empty = firstEvent with { Occurrence = new string('C', 64) };
                var port = services.GetRequiredService<IRuntimeAuthoredStandaloneEventIngress>();
                var sourceStore = services.GetRequiredService<IRuntimeIngressDeliveryStore>();
                var context = services.GetRequiredService<RuntimeDbContext>();
                var slot = Assert.IsType<WorkflowActivationSlot>(await services.GetRequiredService<IWorkflowActivationAuthority>()
                    .FindAsync(Definition, KernelServing.DefaultSlotName, token));
                await port.AdmitAsync(empty, token);
                var firstWrite = TriggerWrite(slot, first, "event", new { eventName }) with { ObservedTargets = null };
                foreach (var wrongScope in new PersistenceScope?[] { null, new("another-tenant") })
                {
                    await using var inactive = OpenHost(neutral: true, authoredOnly: true, relayScope: wrongScope);
                    await using var operation = await inactive.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                    var committer = operation.ServiceProvider.GetRequiredService<ITriggerManagementCommitter>();
                    Assert.False(committer.CanActivateType("event", 1));
                    var unavailable = await Assert.ThrowsAsync<TriggerOperationException>(() =>
                        committer.CommitAsync(DeliveryScope, firstWrite, token).AsTask());
                    Assert.Equal("unavailable", unavailable.Code);
                }

                await using var active = OpenHost(neutral: true, authoredOnly: true, relayScope: Persistence);
                await using var management = await active.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var authoring = management.ServiceProvider.GetRequiredService<ITriggerManagementCommitter>();
                Assert.True(authoring.CanActivateType("event", 1));
                Assert.False(authoring.CanActivateType("event", 2));
                await CommitAuthoredTriggerAsync(firstWrite);
                await CommitAuthoredTriggerAsync(TriggerWrite(slot, second, "event", new { eventName }) with { ObservedTargets = null });
                await port.AdmitAsync(empty, token);
                Assert.Null(await FindAsync(sourceStore, empty, first));

                if (!postgres)
                {
                    await context.Database.OpenConnectionAsync(token);
                    // A second physical outbox insert in this cohort fails, regardless of which resource EF stages first.
                    await context.Database.ExecuteSqlRawAsync("""
                        CREATE TEMP TRIGGER fail_second_authored_event BEFORE INSERT ON elsa_runtime_post_commit_outbox
                        WHEN EXISTS (
                            SELECT 1 FROM elsa_runtime_post_commit_outbox WHERE "SourceKeyHash" = NEW."SourceKeyHash")
                        BEGIN SELECT RAISE(ABORT, 'fail second authored event resource'); END;
                        """, token);
                    try { await Assert.ThrowsAsync<TriggerOperationException>(() => port.AdmitAsync(firstEvent, token).AsTask()); }
                    finally { await context.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_second_authored_event", token); }
                    Assert.Null(await FindAsync(sourceStore, firstEvent, first));
                    Assert.Null(await FindAsync(sourceStore, firstEvent, second));
                    await context.Database.CloseConnectionAsync();
                }

                await port.AdmitAsync(firstEvent, token);
                await AssertPinnedAsync(sourceStore, first);
                await AssertPinnedAsync(sourceStore, second);

                var thirdModel = ExecutableOf("standalone-neutral-third", Definition,
                    SetOutput("set-result", "result", Literal("value", "third")));
                var third = new WorkflowExecutable(thirdModel.Identity with { ArtifactVersion = "3", DefinitionVersionId = "version-3" },
                    thirdModel.RootActivity, thirdModel.ResumeTargets, thirdModel.CreatedAt, thirdModel.CompatibilityMetadata,
                    current.InputContract!, thirdModel.Dependencies, thirdModel.IncidentStrategy);
                var republished = await PublishAsync(services, third, slot.Revision, clock.GetUtcNow(), token);
                await CommitAuthoredTriggerAsync(TriggerWrite(republished, later, "event", new { eventName }) with { ObservedTargets = null });
                await using (var fresh = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence))
                {
                    var reopened = fresh.ServiceProvider.GetRequiredService<IRuntimeAuthoredStandaloneEventIngress>();
                    var reopenedStore = fresh.ServiceProvider.GetRequiredService<IRuntimeIngressDeliveryStore>();
                    await reopened.AdmitAsync(firstEvent, token);
                    await AssertPinnedAsync(reopenedStore, first);
                    await AssertPinnedAsync(reopenedStore, second);
                    Assert.Null(await FindAsync(reopenedStore, firstEvent, later));
                    await Assert.ThrowsAsync<TriggerOperationException>(() => reopened.AdmitAsync(firstEvent with { EventName = "other" }, token).AsTask());
                }

                await RunScheduledSweepAsync(pump, token);
                foreach (var trigger in new[] { first, second })
                {
                    var delivered = Assert.IsType<RuntimePostCommitOutboxItem>(await FindAsync(sourceStore, firstEvent, trigger));
                    Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
                    Assert.Equal(current.Identity, Progress(delivered).Receipt?.Material);
                }

                async Task CommitAuthoredTriggerAsync(TriggerStoreWrite write)
                {
                    var receipt = await authoring.CommitAsync(DeliveryScope, write, token);
                    Assert.Equal((write.Trigger.Id, 1L), (receipt.TriggerId, receipt.Revision));
                    var stored = await management.ServiceProvider.GetRequiredService<IStandaloneTriggerStore>().FindAsync(DeliveryScope,
                        write.Trigger.Id, token);
                    Assert.True(stored?.Trigger.HasActivated);
                }

                async Task AssertPinnedAsync(IRuntimeIngressDeliveryStore store, string trigger)
                {
                    var item = Assert.IsType<RuntimePostCommitOutboxItem>(await FindAsync(store, firstEvent, trigger));
                    Assert.Equal(current.Identity, Assert.Single(item.SubscriptionDelivery!.Recipients).Material);
                    Assert.Equal(firstEvent.Authority.RootInitiator, item.StandaloneIngress?.SourceActorAttribution.RootInitiator);
                }

                ValueTask<RuntimePostCommitOutboxItem?> FindAsync(IRuntimeIngressDeliveryStore store, RuntimeAuthoredStandaloneEvent authored, string trigger) =>
                    store.FindDeliveryAsync(DeliveryScope,
                        RuntimeStandaloneIngressSource.BuildOutboxItemId(Persistence, Persistence.Value, trigger, family, authored.Occurrence), token);
            }

            async Task AssertResourceRecurrenceAsync(IServiceProvider services, Contract.IActorReads reads, Contract.BoundScope owner,
                IRecurringTask recurrence, IRecurringTask sourcePump)
            {
                const string cronId = "prepared-standalone-cron";
                var schedules = services.GetRequiredService<IRecurringTriggerScheduleStore>();
                var slot = await services.GetRequiredService<IWorkflowActivationAuthority>().FindAsync(Definition, KernelServing.DefaultSlotName, token);
                var resource = services.GetRequiredService<ITriggerSourceResourceCommitStore>();
                var now = clock.GetUtcNow();
                var authored = new RecurringTriggerSchedule(RecurringTriggerSchedule.BuildId(material.Identity.ArtifactId, "legacy-timer"),
                    material.Identity.ArtifactId, "legacy-timer", "Timer", "legacy-timer-hash", RecurringScheduleKind.Interval, "PT1M", now, now);
                await schedules.SaveAsync(authored, token);
                await WriteTriggerAsync(resource, Assert.IsType<WorkflowActivationSlot>(slot), TimerId, "timer", new { interval = "PT1M" });
                await WriteTriggerAsync(resource, slot!, cronId, "cron", new { expression = "* * * * *", timezone = "UTC" });

                var timer = await ReadResourceScheduleAsync(TimerId);
                var cron = await ReadResourceScheduleAsync(cronId);
                var sourceIds = new[] { timer, cron }.Select(schedule => RuntimeStandaloneIngressSource.BuildOutboxItemId(
                    Persistence, Persistence.Value, schedule.Resource!.TriggerId, schedule.Resource.SourceFamily, schedule.BuildOccurrenceKey())).ToArray();
                clock.Advance(TimeSpan.FromMinutes(2));
                var ingress = services.GetRequiredService<IRuntimeIngressDeliveryStore>();
                await recurrence.ExecuteAsync(token);
                Assert.Single(await ReadAcceptedAsync());
                await recurrence.ExecuteAsync(token);
                Assert.Equal(2, (await ReadAcceptedAsync()).Length);

                var untouched = Assert.IsType<RecurringTriggerScheduleObservation>(await schedules.ObserveAsync(authored.ScheduleId, token));
                Assert.Equal(authored.NextOccurrence, untouched.Schedule.NextOccurrence);
                Assert.Equal(0, untouched.FailureCount);
                Assert.Null(untouched.RetryAt);
                Assert.False(untouched.OccurrenceInFlight);
                Assert.True((await ReadResourceScheduleAsync(TimerId)).NextOccurrence > clock.GetUtcNow());
                Assert.True((await ReadResourceScheduleAsync(cronId)).NextOccurrence > clock.GetUtcNow());
                await sourcePump.ExecuteAsync(token);
                foreach (var id in sourceIds)
                {
                    var delivered = await ReadAsync(services, id, token);
                    var receipt = Assert.IsType<RuntimeTargetAcceptanceReceipt>(Progress(delivered).Receipt);
                    Assert.Equal(RuntimeTargetAcceptanceKind.PreparedActorBirth, receipt.Kind);
                    Assert.Equal(current.Identity, receipt.Material);
                    await WaitForAsync(reads, owner, new(receipt.EvidenceId),
                        actor => actor.Header.Status == Contract.ActorStatus.Finished, token);
                }
                Assert.Equal(3, (await reads.ListAsync(owner, new(), cancellationToken: token)).Items.Count);

                async ValueTask<RecurringTriggerSchedule> ReadResourceScheduleAsync(string id) =>
                    Assert.IsType<RecurringTriggerSchedule>(await schedules.FindAsync(
                        RecurringTriggerSchedule.BuildResourceId(Persistence, id, Persistence.Value), token));

                async Task<RuntimePostCommitOutboxItem[]> ReadAcceptedAsync()
                {
                    var accepted = new List<RuntimePostCommitOutboxItem>();
                    foreach (var id in sourceIds)
                        if (await ingress.FindDeliveryAsync(DeliveryScope, id, token) is { } item)
                            accepted.Add(item);
                    return accepted.ToArray();
                }
            }

            async Task WriteTriggerAsync<TConfiguration>(ITriggerSourceResourceCommitStore store, WorkflowActivationSlot activation,
                string id, string type, TConfiguration configuration)
            {
                var receipt = await store.CommitAsync(DeliveryScope, TriggerWrite(activation, id, type, configuration), [], token);
                Assert.Equal((id, 1L), (receipt.TriggerId, receipt.Revision));
            }

            TriggerStoreWrite TriggerWrite<TConfiguration>(WorkflowActivationSlot activation, string id, string type, TConfiguration configuration)
            {
                var now = clock.GetUtcNow();
                var origin = new TriggerManagementOrigin(TriggerManagementKind.Studio);
                var trigger = new StandaloneTrigger(id, id, type, 1, JsonSerializer.SerializeToElement(configuration),
                    1, 1, 1, TriggerDesiredState.Enabled, origin, now, now, "qualification", tenantId: Persistence.Value);
                var subscription = new TriggerSubscription(id + "-subscription", id, Definition, KernelServing.DefaultSlotName, "native",
                    1, 1, true, [], origin, new(id + "-grant", id, id + "-subscription", Definition, KernelServing.DefaultSlotName,
                        "native", type, 1, TriggerGrantStatus.Granted, "qualification", now));
                var observedTarget = new TriggerTargetObservation(Definition, activation.SlotName, activation.Revision,
                    activation.ActiveActivationId, RuntimeIngressAdmission.ComputeInputContractFingerprint(material.InputContract!),
                    "native", TriggerTargetState.Available);
                return new(id + "-neutral-create", id + "-neutral-create", "qualification", null, trigger, [subscription])
                {
                    ObservedTargets = [observedTarget]
                };
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

    private sealed class PublishedEventAvailability(PersistenceScope owner) : IRuntimeAuthoredStandaloneEventAvailability
    {
        public bool Supports(PersistenceScope scope) => scope == owner;
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
