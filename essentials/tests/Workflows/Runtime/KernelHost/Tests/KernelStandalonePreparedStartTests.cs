using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed partial class KernelStandalonePreparedStartTests
{
    private const string FoundationDatabase = "KERNEL_STANDALONE_FOUNDATION_POSTGRES";
    private const string RuntimeDatabase = "KERNEL_STANDALONE_RUNTIME_POSTGRES";
    private const string Definition = "private-standalone";
    private const string FinishEvent = "private-standalone-complete-v1";
    private static readonly PersistenceScope Persistence = new(PersistenceScope.DefaultValue);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public Task Sqlite_reconciles_exact_standalone_birth_after_republication_and_restart() => RunAsync(false);

    [SkippableFact]
    public Task PostgreSql_reconciles_exact_standalone_birth_after_republication_and_restart()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(FoundationDatabase)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(RuntimeDatabase)),
            "Requires dedicated Foundation and Runtime PostgreSQL databases.");
        return RunAsync(true);
    }

    private static async Task RunAsync(bool postgres)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var directory = postgres ? null : Directory.CreateTempSubdirectory("kernel-standalone-start");
        try
        {
            var foundationPath = directory is null ? null : Path.Combine(directory.FullName, "foundation.db");
            var foundation = postgres ? Environment.GetEnvironmentVariable(FoundationDatabase)! : null;
            var runtime = postgres ? Environment.GetEnvironmentVariable(RuntimeDatabase)!
                : new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory!.FullName, "runtime.db"), Pooling = false }.ToString();
            if (postgres)
                Assert.NotEqual(new NpgsqlConnectionStringBuilder(foundation).Database, new NpgsqlConnectionStringBuilder(runtime).Database);
            await RunAsync(postgres ? "PostgreSql" : "Sqlite", foundationPath, foundation, runtime, deadline.Token);
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static async Task RunAsync(string provider, string? path, string? foundation, string runtime, CancellationToken token)
    {
        var ownerScope = EmbeddedTenant.Bind(Persistence.Value);
        var clock = new TestClock(DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        var historical = Material("v1", Sequence("original",
            Node("wait", Types.Event, [Literal("EventName", FinishEvent), Literal("CanStartWorkflow", false, "Boolean")]),
            SetOutput("finish-original", "result", Request("value", "value"))));
        var replacement = Material("v2", SetOutput("replacement", "result", Literal("value", "replacement")));
        RuntimeCommittedRecipientAuthority authority;
        Contract.ActorRef actor;

        await using (var host = OpenHost())
        {
            await using var runtimeScope = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
            var stores = runtimeScope.ServiceProvider;
            await stores.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync(token);
            var before = await PublishAsync(stores, historical, 0, clock.GetUtcNow(), token);
            var recipient = await AdmitAsync(stores, before, clock.GetUtcNow(), token);
            var shell = host.GetRequiredService<KernelShellRuntime>();
            await shell.StartAsync(token);
            try
            {
                var selected = host.GetRequiredService<KernelShellStorage>().BindPreparedStartQualification(ownerScope);
                var probe = new OfferProbe(selected) { RefusePrepared = true };
                var delivery = Delivery(host, probe);
                var nowPublished = await PublishAsync(stores, replacement, before.Revision, clock.GetUtcNow(), token);
                Assert.NotEqual(before.ActiveActivationId, nowPublished.ActiveActivationId);
                var references = stores.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
                Assert.NotNull(Assert.IsType<WorkflowExecutableSourceReference>(await references.FindAsync(
                    WorkflowActivationReferenceIdentity.Create(before.ActiveActivationId!), token)).DeletedAt);
                var first = await ClaimAsync(stores, "first", clock, token);
                var handoff = (IRuntimePreparedStartHandoff)stores.GetRequiredService<IRuntimeIngressDeliveryStore>();
                var committed = Assert.IsType<RuntimeCommittedRecipient>(await handoff.CommitPreparedStartAsync(first, recipient.RecipientId, token));
                authority = new(committed.Seal.SourceOutboxItemId, committed.Seal.SourceFingerprint,
                    committed.Seal.SelectionFingerprint, Persistence, recipient.RecipientId, committed.Seal.Fingerprint);

                var refused = await delivery.DeliverAsync(first, recipient.RecipientId, token);
                Assert.Null(Progress(refused).Receipt);
                Assert.Equal((1L, true), await handoff.ReadPreparedStartAttemptAsync(authority, token));
                var abandoned = Assert.IsType<PendingRootStart>(probe.LastOffer);
                Assert.Equal(PreparedRootStartDisposition.Disarmed, await selected.ObserveAsync(abandoned.Actor, abandoned.Issuance, token));

                clock.Advance(TimeSpan.FromMinutes(2));
                var second = await ClaimAsync(stores, "replacement-claim", clock, token);
                await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() =>
                    delivery.DeliverAsync(first, recipient.RecipientId, token).AsTask());
                Assert.Equal(1, probe.Starts);
                probe.RefusePrepared = false;
                probe.DropAcceptedReturn = true;
                await Assert.ThrowsAsync<IOException>(() => delivery.DeliverAsync(second, recipient.RecipientId, token).AsTask());
                actor = Assert.IsType<PendingRootStart>(probe.LastOffer).Actor;
                Assert.NotEqual(abandoned.Actor, actor);
                Assert.Equal(2, probe.Starts);
                Assert.Equal((2L, false), await handoff.ReadPreparedStartAttemptAsync(authority, token));
                Assert.Null(Progress((await stores.GetRequiredService<IRuntimeIngressDeliveryStore>()
                    .ReadCommittedRecipientAsync(authority, token)).Item).Receipt);
                Assert.Null(Progress(await handoff.ReconcilePreparedStartAsync(second, authority, 1, token)).Receipt);
                var reads = await shell.ReadAsync(token);
                await WaitForAsync(reads, ownerScope, actor, view => view.Header.Status == Contract.ActorStatus.Running &&
                    WorkflowActor.DescribeRun(view.Header)?.StartedAt is not null, token);
            }
            finally { await StopAsync(shell); }
        }

        await using (var host = OpenHost())
        {
            var shell = host.GetRequiredService<KernelShellRuntime>();
            await shell.StartAsync(token);
            try
            {
                var storage = host.GetRequiredService<KernelShellStorage>();
                var probe = new OfferProbe(storage.BindPreparedStartQualification(ownerScope));
                var delivery = Delivery(host, probe);
                await using var runtimeScope = await host.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(Persistence);
                var stores = runtimeScope.ServiceProvider;
                clock.Advance(TimeSpan.FromMinutes(2));
                var current = await ClaimAsync(stores, "cold-claim", clock, token);
                var settled = await delivery.DeliverAsync(current, authority.RecipientId, token);
                var receipt = Assert.IsType<RuntimeTargetAcceptanceReceipt>(Progress(settled).Receipt);
                Assert.Equal((RuntimeTargetAcceptanceKind.PreparedActorBirth, actor.Id, historical.Identity, authority.SealFingerprint),
                    (receipt.Kind, receipt.EvidenceId, receipt.Material, receipt.SealFingerprint));
                Assert.True(receipt.IsEquivalentTo(Progress(await delivery.DeliverAsync(current, authority.RecipientId, token)).Receipt));
                Assert.Equal(0, probe.Starts);

                var foundationStore = await storage.OpenAsync(token);
                var waiting = Assert.IsType<Contract.ActorView>(await foundationStore.Reads.GetAsync(ownerScope, actor, token));
                Assert.Equal((historical.Identity.ArtifactId, Definition), (WorkflowActor.DescribeRun(waiting.Header)?.Workflow,
                    WorkflowActor.DescribeRun(waiting.Header)?.Definition));
                var resumed = await foundationStore.Ingress.SendAsync(ownerScope, new Contract.Target.Ref(actor), "finish-old",
                    NativeKinds.Resume, JsonSerializer.Serialize(new ResumeStimulus(FinishEvent)), cancellationToken: token);
                Assert.Equal(Contract.AdmissionOutcome.Accepted, resumed.Outcome);
                var ended = await WaitForAsync(foundationStore.Reads, ownerScope, actor,
                    view => view.Header.Status == Contract.ActorStatus.Finished, token);
                var output = await Assert.IsAssignableFrom<Contract.IActorTerminalOutputReads>(foundationStore.Reads)
                    .ReadTerminalOutputAsync(ownerScope, actor, Assert.IsType<long>(ended.Header.EndedAt), includeValues: true, cancellationToken: token);
                using var values = JsonDocument.Parse(Assert.IsType<byte[]>(output?.Values));
                Assert.Equal("captured-on-v1", values.RootElement.GetProperty("result").GetString());
                Assert.Equal(actor, Assert.Single((await foundationStore.Reads.ListAsync(ownerScope, new(), cancellationToken: token)).Items).Header.Ref);
            }
            finally { await StopAsync(shell); }
        }

        ServiceProvider OpenHost()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddLogging(builder => builder.AddSimpleConsole().SetMinimumLevel(LogLevel.Warning));
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton<IWellKnownTypeRegistry, KernelPublishedExecuteTests.FixtureTypes>();
            new Elsa.Events.EventsFeature().ConfigureServices(services);
            KernelRuntimeFixture.Add(services, provider, runtime);
            new RuntimePreparedStandaloneEntityFrameworkCoreFeature().ConfigureServices(services);
            services.AddStandaloneTriggerIngress<FixtureEvent>(new("event", 1, EventFamily, EventSchema));
            services.AddScoped<IStandaloneTriggerSourceContext<FixtureEvent>, EventContext>();
            new WorkflowsRuntimeKernelHostingFeature
            {
                Provider = provider, SqlitePath = path, PostgresConnectionString = foundation, PreparedStandaloneStarts = true
            }
                .ConfigureServices(services);
            new WorkflowsRuntimeKernelFeature().ConfigureServices(services);
            new WorkflowsRuntimeKernelFeature().PostConfigureServices(services);
            services.AddScoped<IRuntimePreparedStartAttemptProof>(sp => new KernelStandalonePreparedStartProof(Persistence, ownerScope,
                () => sp.GetRequiredService<KernelShellStorage>().BindPreparedStartQualification(ownerScope)));
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        KernelStandalonePreparedStartHandoff Delivery(IServiceProvider host, IPreparedRootStartHandoff selected) =>
            new(host.GetRequiredService<IServiceScopeFactory>(), Persistence, ownerScope,
                host.GetRequiredService<KernelServing>().Capabilities, () => selected);
    }

    private static WorkflowExecutable Material(string version, ExecutableNode root) =>
        new(new($"private-standalone-{version}", Definition, version, version, $"sha256:private-standalone-{version}"), root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UnixEpoch, new Dictionary<string, string>(),
            new(WorkflowExecutableInputContract.CurrentVersion, [new("value", new TypeReference("String"), isRequired: true)]),
            dependencies: null, new IncidentStrategyReference("Fault", "1"));

    private static async Task<WorkflowActivationSlot> PublishAsync(IServiceProvider stores, WorkflowExecutable executable, long previous,
        DateTimeOffset now, CancellationToken token)
    {
        await stores.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable, token);
        var identity = executable.Identity;
        var source = new WorkflowExecutableSourceReference("candidate", identity.ArtifactId, "definition", Definition,
            identity.ArtifactVersion, Definition, identity.DefinitionVersionId, identity.ArtifactVersion, now, now,
            WorkflowExecutableReferenceScope.Published, TenantId: Persistence.Value);
        var published = await stores.GetRequiredService<IWorkflowActivationCoordinator>().ActivateAsync(new(executable, source,
            KernelServing.DefaultSlotName, "publication-" + identity.ArtifactVersion, WorkflowActivationSource.Publishing, previous), token);
        Assert.True(published.Succeeded, published.Diagnostic);
        return published.Slot;
    }

    private static async Task<RuntimeSubscriptionRecipient> AdmitAsync(IServiceProvider stores, WorkflowActivationSlot slot,
        DateTimeOffset now, CancellationToken token)
    {
        const string subscriptionId = "standalone-subscription";
        var management = new TriggerManagementOrigin(TriggerManagementKind.Studio);
        var trigger = new StandaloneTrigger(EventId, "Test standalone", "event", 1, JsonSerializer.SerializeToElement(new { eventName = EventId }),
            1, 1, 1, TriggerDesiredState.Enabled, management, now, now, "test-owner", tenantId: Persistence.Value);
        var subscription = new TriggerSubscription(subscriptionId, EventId, Definition, slot.SlotName, "native", 1, 1, true,
            [new("value", new TriggerSourceBinding(["payload", "value"]))], management,
            new("standalone-grant", EventId, subscriptionId, Definition, slot.SlotName, "native", "event", 1,
                TriggerGrantStatus.Granted, "test-owner", now));
        await stores.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(DeliveryScope,
            new("create-standalone", "create-standalone", "test-owner", null, trigger, [subscription]), token);

        var accepted = await stores.GetRequiredService<IStandaloneTriggerIngress<FixtureEvent>>().AdmitAsync(
            new(EventId, "test-source", EventSchema, JsonSerializer.SerializeToElement(new { value = "captured-on-v1" }),
                JsonSerializer.SerializeToElement(new { })), token);
        var item = await stores.GetRequiredService<IRuntimeIngressDeliveryStore>().FindDeliveryAsync(DeliveryScope, accepted.AdmissionId, token);
        return Assert.Single(Assert.IsType<RuntimePostCommitOutboxItem>(item).SubscriptionDelivery!.Recipients);
    }

    private static async Task<RuntimeDeliverySourceContext> ClaimAsync(IServiceProvider stores, string owner, TimeProvider clock, CancellationToken token)
    {
        var outbox = Assert.IsAssignableFrom<IRuntimePostCommitOutboxClaimStore>(stores.GetRequiredService<IRuntimePostCommitOutboxStore>());
        var claims = await outbox.ClaimAsync(new(owner, clock.GetUtcNow(), TimeSpan.FromMinutes(1), 1,
            intentKind: RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind), token);
        return new(Assert.Single(claims), Persistence);
    }

    private static RuntimeRecipientProgress Progress(RuntimePostCommitOutboxItem item) => Assert.Single(item.SubscriptionDelivery!.Progress);

    private static async Task<Contract.ActorView> WaitForAsync(Contract.IActorReads reads, Contract.BoundScope scope, Contract.ActorRef actor,
        Func<Contract.ActorView, bool> ready, CancellationToken token)
    {
        var until = DateTimeOffset.UtcNow.AddSeconds(10);
        Contract.ActorView? latest;
        do
        {
            latest = await reads.GetAsync(scope, actor, token);
            if (latest is not null && ready(latest)) return latest;
            await Task.Delay(TimeSpan.FromMilliseconds(20), token);
        } while (DateTimeOffset.UtcNow < until);
        throw new InvalidOperationException($"The actor did not reach the expected state. Status: {latest?.Header.Status}; journal revision: {latest?.AsOf}.");
    }

    private static async Task StopAsync(KernelShellRuntime shell)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await shell.StopAsync(stop.Token);
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private long ticks = now.UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
        public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
    }

    private sealed class OfferProbe(IPreparedRootStartHandoff foundation) : IPreparedRootStartHandoff
    {
        public PendingRootStart? LastOffer { get; private set; }
        public int Starts { get; private set; }
        public bool RefusePrepared { get; set; }
        public bool DropAcceptedReturn { get; set; }

        public async ValueTask<Contract.Admission> StartAsync(string operationId, Contract.StartSpec spec, ProtectRootStartAsync protect,
            CancellationToken cancellationToken = default)
        {
            Starts++;
            var admitted = await foundation.StartAsync(operationId, spec, async (offer, token) =>
            {
                LastOffer = offer;
                Assert.True(await protect(offer, token));
                return !RefusePrepared;
            }, cancellationToken);
            if (DropAcceptedReturn)
            {
                Assert.Equal(Contract.AdmissionOutcome.Accepted, admitted.Outcome);
                throw new IOException("Simulated loss of the Runtime observer after durable Foundation birth.");
            }
            return admitted;
        }

        public ValueTask<PreparedRootStartDisposition> ObserveAsync(Contract.ActorRef actor, string issuance, CancellationToken cancellationToken = default) =>
            foundation.ObserveAsync(actor, issuance, cancellationToken);
    }
}
