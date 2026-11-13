using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeTestEntryAdmissionTests
{
    [Fact]
    public async Task Trusted_preparation_uses_the_actual_provider_tuple_and_creates_no_execution_or_artifact()
    {
        var owners = new Owners();
        await using var harness = Build(owners);
        await Open(harness, owners);
        var result = await Prepare(harness, owners);
        Assert.True(result.IsAvailable);
        Assert.Equal(owners.Entry.ContextId, result.Context!.ContextId);
        Assert.Equal(new[] { "selected" }, result.Context.OwnedNodeIds);
        Assert.Equal(2, owners.EntryReads);
        Assert.Equal(1, owners.SeedReads);
        Assert.Equal(2, owners.RoutingReads);
        var tuple = Assert.IsType<RuntimeTestPersistenceBoundaryContext>(owners.Tuple);
        Assert.Same(Physical<IWorkflowExecutionStateStore>(harness), tuple.Executions);
        Assert.Same(Physical<IWorkflowSchedulerWorkQueue>(harness), tuple.Scheduler);
        Assert.Same(Physical<IRuntimeCheckpointCommitStore>(harness), tuple.Checkpoints);
        Assert.Same(Physical<IRuntimePostCommitOutboxStore>(harness), tuple.Outbox);
        Assert.Same(harness.Services.GetRequiredService<IBackgroundActivityWorkStore>(), tuple.Background);
        Assert.Same(harness.Services.GetRequiredService<IDurableTimerStore>(), tuple.Timers);
        Assert.Same(harness.Services.GetRequiredService<IWorkflowExecutionActorProvider>(), owners.Actors);
        Assert.Null(await tuple.Executions.FindAsync("prepared"));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("prepared"));
        Assert.Empty(await tuple.Scheduler.ListAllAsync(new RuntimeSchedulerWorkQuery("prepared")));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(owners.Executable.Identity.ArtifactId));
    }

    [Theory]
    [InlineData("resolver-missing")]
    [InlineData("resolver-duplicate")]
    [InlineData("seed-missing")]
    [InlineData("seed-duplicate")]
    [InlineData("routing-missing")]
    [InlineData("routing-duplicate")]
    [InlineData("persistence-missing")]
    [InlineData("persistence-duplicate")]
    [InlineData("scope-missing")]
    [InlineData("scope-closed")]
    [InlineData("descriptor-absent")]
    [InlineData("seed-absent")]
    [InlineData("descriptor-changed")]
    [InlineData("seed-changed")]
    [InlineData("routing-refused")]
    [InlineData("persistence-refused")]
    [InlineData("resolver-revoked")]
    [InlineData("expires-during-seed")]
    [InlineData("closes-during-seed")]
    public async Task Missing_changed_unqualified_or_no_longer_open_context_never_becomes_available(string profile)
    {
        var owners = new Owners();
        await using var harness = Build(owners, services =>
        {
            switch (profile)
            {
                case "resolver-missing": services.RemoveAll<IWorkflowTestEntryContextResolver>(); break;
                case "resolver-duplicate": services.AddSingleton<IWorkflowTestEntryContextResolver>(owners); break;
                case "seed-missing": services.RemoveAll<IRuntimeTestContextSeedSource>(); break;
                case "seed-duplicate": services.AddSingleton<IRuntimeTestContextSeedSource>(owners); break;
                case "routing-missing": services.RemoveAll<IRuntimeTestExecutionRoutingBoundary>(); break;
                case "routing-duplicate": services.AddSingleton<IRuntimeTestExecutionRoutingBoundary>(owners); break;
                case "persistence-missing": services.RemoveAll<IRuntimeTestPersistenceBoundary>(); break;
                case "persistence-duplicate": services.AddSingleton<IRuntimeTestPersistenceBoundary>(owners); break;
            }
        });
        if (profile != "scope-missing") await Open(harness, owners);
        if (profile == "scope-closed") await Close(harness, owners);
        owners.DescriptorAbsent = profile == "descriptor-absent";
        owners.SeedAbsent = profile == "seed-absent";
        owners.DescriptorChanged = profile == "descriptor-changed";
        owners.SeedChanged = profile == "seed-changed";
        owners.CanRoute = profile != "routing-refused";
        owners.CanPersist = profile != "persistence-refused";
        owners.RevokeAfterFirstRead = profile == "resolver-revoked";
        if (profile == "expires-during-seed") owners.BeforeSeed = () =>
        { owners.Clock.Now = owners.Entry.Scope.ExpiresAt; return ValueTask.CompletedTask; };
        if (profile == "closes-during-seed") owners.BeforeSeed = () => Close(harness, owners);
        var result = await Prepare(harness, owners);
        Assert.False(result.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("prepared"));
    }

    [Theory]
    [InlineData("resolver")]
    [InlineData("seed")]
    [InlineData("routing")]
    [InlineData("persistence")]
    [InlineData("resolver-cancel")]
    [InlineData("seed-cancel")]
    [InlineData("routing-cancel")]
    [InlineData("persistence-cancel")]
    public async Task Provider_failures_remain_infrastructure_failures_without_exposing_exception_data(string owner)
    {
        var owners = new Owners { FailAt = owner };
        await using var harness = Build(owners);
        await Open(harness, owners);
        var result = await Prepare(harness, owners);
        Assert.False(result.IsAvailable);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(RuntimeTestContextFindingKind.InfrastructureFailure, finding.Kind);
        Assert.DoesNotContain("private-provider-data", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("run-kind")]
    [InlineData("scope")]
    [InlineData("tenant")]
    [InlineData("partition")]
    [InlineData("system-authority")]
    [InlineData("initiator")]
    [InlineData("authority-metadata")]
    [InlineData("hash")]
    [InlineData("source-version")]
    [InlineData("node")]
    [InlineData("parent")]
    [InlineData("trigger")]
    [InlineData("start-authority")]
    [InlineData("depth")]
    [InlineData("stimulus")]
    public async Task Foreign_or_non_root_start_context_is_rejected_before_trusted_lookup_or_provider_qualification(string profile)
    {
        var owners = new Owners();
        await using var harness = Build(owners);
        await Open(harness, owners);
        var authority = profile switch
        {
            "system-authority" => new("other", "initiator"),
            "initiator" => new("system", "other"),
            "authority-metadata" => new("system", "initiator", new Dictionary<string, string> { ["grant"] = "other" }),
            _ => owners.Entry.Authority
        };
        var entry = new WorkflowTestEntry(owners.Entry.ContextId, owners.Entry.ContextFingerprint,
            profile == "hash" ? owners.Executable.Identity with { ArtifactHash = "other" } :
            profile == "source-version" ? owners.Executable.Identity with { DefinitionVersionId = "other" } : owners.Executable.Identity,
            profile == "node" ? "unknown" : "selected", owners.Entry.Scope, owners.Entry.Authority);
        var error = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await harness.Services.GetRequiredService<WorkflowTestEntryAdmission>().ValidateAsync(entry, owners.Executable,
                profile == "run-kind" ? WorkflowRunKind.Unknown : WorkflowRunKind.TestRun,
                profile == "scope" ? null : owners.Entry.Scope, authority,
                profile == "tenant" ? "other" : owners.Entry.Scope.TenantId,
                profile == "partition" ? new("other") : owners.Entry.Scope.Partition,
                profile == "parent" ? "parent" : null, profile == "trigger" ? "trigger" : null,
                profile == "start-authority" ? WorkflowExecutableStartAuthority.FromRetainedDependency("parent", "hash", "dispatch") : null,
                profile == "depth" ? 1 : 0, profile == "stimulus", CancellationToken.None));
        Assert.Equal("test-entry-context-mismatch", error.ReasonCode);
        Assert.Equal(0, owners.EntryReads);
        Assert.Equal(0, owners.RoutingReads);
        Assert.Null(owners.Tuple);
    }

    [Fact]
    public async Task Default_runtime_composes_no_authority_seed_or_routing_owner_and_refuses_preparation()
    {
        var owners = new Owners();
        await using var harness = WorkflowExecutionHarness.Create().Build();
        Assert.Empty(harness.Services.GetServices<IWorkflowTestEntryContextResolver>());
        Assert.Empty(harness.Services.GetServices<IRuntimeTestContextSeedSource>());
        Assert.Empty(harness.Services.GetServices<IRuntimeTestExecutionRoutingBoundary>());
        Assert.False((await Prepare(harness, owners)).IsAvailable);
    }

    [Theory]
    [InlineData("resolver")]
    [InlineData("seed")]
    [InlineData("routing")]
    [InlineData("persistence")]
    [InlineData("preparer")]
    public async Task Duplicate_replacement_contracts_are_diagnosed_at_startup(string contract)
    {
        var owners = new Owners();
        await using var harness = Build(owners, services =>
        {
            switch (contract)
            {
                case "resolver": services.AddSingleton<IWorkflowTestEntryContextResolver>(owners); break;
                case "seed": services.AddSingleton<IRuntimeTestContextSeedSource>(owners); break;
                case "routing": services.AddSingleton<IRuntimeTestExecutionRoutingBoundary>(owners); break;
                case "persistence": services.AddSingleton<IRuntimeTestPersistenceBoundary>(owners); break;
                case "preparer": services.AddScoped<IRuntimeTestContextPreparer, RuntimeTestContextPreparer>(); break;
            }
        });
        var registrations = harness.Services.GetRequiredService<RuntimeTestContextInspectionRegistrations>();
        Assert.Single(registrations.Read(), x => x.ServiceType == typeof(IStartupTask) && x.ImplementationType == typeof(ValidateRuntimeTestEntryComposition));
        var startup = new ValidateRuntimeTestEntryComposition(registrations);
        await Assert.ThrowsAsync<InvalidOperationException>(() => startup.ExecuteAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Requested_cancellation_propagates_without_preparation()
    {
        var owners = new Owners();
        await using var harness = Build(owners);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Prepare(harness, owners, cancellation.Token));
        Assert.Equal(0, owners.EntryReads);
    }

    [Fact]
    public async Task Requested_cancellation_during_seed_lookup_propagates()
    {
        var owners = new Owners();
        await using var harness = Build(owners);
        await Open(harness, owners);
        using var cancellation = new CancellationTokenSource();
        owners.BeforeSeed = () => { cancellation.Cancel(); return ValueTask.FromCanceled(cancellation.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Prepare(harness, owners, cancellation.Token));
        Assert.Equal(1, owners.SeedReads);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("prepared"));
    }

    [Fact]
    public async Task Replacing_the_read_only_inspector_cannot_bypass_canonical_trusted_setup_validation()
    {
        var owners = new Owners { InvalidSetup = true };
        var inspector = new UnexpectedInspector();
        await using var harness = Build(owners, services => services.AddScoped<IRuntimeTestContextInspector>(_ => inspector));
        await Open(harness, owners);
        var result = await Prepare(harness, owners);
        Assert.False(result.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
        Assert.Equal(0, inspector.Reads);
    }

    private sealed class UnexpectedInspector : IRuntimeTestContextInspector
    {
        internal int Reads;
        public ValueTask<RuntimeTestContextPreparation> InspectAsync(RuntimeTestContextInspectionRequest request,
            CancellationToken cancellationToken = default)
        { Reads++; throw new InvalidOperationException("A replaceable inspector cannot authorize trusted setup."); }
    }

    [Theory]
    [InlineData("execution")]
    [InlineData("scheduler")]
    [InlineData("checkpoint")]
    [InlineData("outbox")]
    public async Task Actual_wrappers_cannot_borrow_qualification_from_an_unrelated_registered_inner(string store)
    {
        var owners = new Owners();
        await using var actual = Build(owners);
        await using var decoy = WorkflowExecutionHarness.Create().Build();
        var services = new ServiceCollection();
        services.AddSingleton(actual.Services.GetRequiredService<IWorkflowExecutionStateStore>());
        services.AddSingleton(actual.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>());
        services.AddSingleton(actual.Services.GetRequiredService<IRuntimeCheckpointCommitStore>());
        services.AddSingleton(actual.Services.GetRequiredService<IRuntimePostCommitOutboxStore>());
        services.AddSingleton(actual.Services.GetRequiredService<IBackgroundActivityWorkStore>());
        services.AddSingleton(actual.Services.GetRequiredService<IDurableTimerStore>());
        services.AddSingleton(actual.Services.GetRequiredService<IWorkflowExecutionActorProvider>());
        services.AddSingleton(new CoalescingInner<IWorkflowExecutionStateStore>(Physical<IWorkflowExecutionStateStore>(store == "execution" ? decoy : actual)));
        services.AddSingleton(new CoalescingInner<IWorkflowSchedulerWorkQueue>(Physical<IWorkflowSchedulerWorkQueue>(store == "scheduler" ? decoy : actual)));
        services.AddSingleton(new CoalescingInner<IRuntimeCheckpointCommitStore>(Physical<IRuntimeCheckpointCommitStore>(store == "checkpoint" ? decoy : actual)));
        services.AddSingleton(new CoalescingInner<IRuntimePostCommitOutboxStore>(Physical<IRuntimePostCommitOutboxStore>(store == "outbox" ? decoy : actual)));
        services.AddSingleton<IRuntimeTestPersistenceBoundary>(owners);
        services.AddSingleton<IRuntimeTestExecutionRoutingBoundary>(owners);
        owners.PersistencePredicate = tuple => store switch
        {
            "execution" => ReferenceEquals(tuple.Executions, Physical<IWorkflowExecutionStateStore>(decoy)),
            "scheduler" => ReferenceEquals(tuple.Scheduler, Physical<IWorkflowSchedulerWorkQueue>(decoy)),
            "checkpoint" => ReferenceEquals(tuple.Checkpoints, Physical<IRuntimeCheckpointCommitStore>(decoy)),
            _ => ReferenceEquals(tuple.Outbox, Physical<IRuntimePostCommitOutboxStore>(decoy))
        };
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () =>
            await new RuntimeTestExecutionBoundary(provider).RequireAsync(owners.Entry, CancellationToken.None));
        Assert.Equal("test-entry-provider-unavailable", error.ReasonCode);
        Assert.Same(Physical<IWorkflowExecutionStateStore>(actual), owners.Tuple!.Executions);
        Assert.Same(Physical<IWorkflowSchedulerWorkQueue>(actual), owners.Tuple.Scheduler);
        Assert.Same(Physical<IRuntimeCheckpointCommitStore>(actual), owners.Tuple.Checkpoints);
        Assert.Same(Physical<IRuntimePostCommitOutboxStore>(actual), owners.Tuple.Outbox);
    }

    [Theory]
    [InlineData("frames-null")]
    [InlineData("results-null")]
    [InlineData("frame-null")]
    [InlineData("fixture-null")]
    [InlineData("frame-values-null")]
    [InlineData("root-value-null")]
    public async Task Malformed_returned_seed_shape_is_unavailable_setup(string shape)
    {
        var owners = new Owners { SeedShape = shape };
        await using var harness = Build(owners);
        await Open(harness, owners);
        var result = await Prepare(harness, owners);
        Assert.False(result.IsAvailable);
        Assert.Equal(RuntimeTestContextFindingKind.Unavailable, Assert.Single(result.Findings).Kind);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("prepared"));
    }

    private static WorkflowExecutionHarness Build(Owners owners, Action<IServiceCollection>? extra = null) =>
        WorkflowExecutionHarness.Create().ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(owners.Clock);
            services.AddSingleton<IWorkflowTestEntryContextResolver>(owners);
            services.AddSingleton<IRuntimeTestContextSeedSource>(owners);
            services.AddSingleton<IRuntimeTestExecutionRoutingBoundary>(owners);
            services.AddSingleton<IRuntimeTestPersistenceBoundary>(owners);
            extra?.Invoke(services);
        }).Build();

    private static async ValueTask Open(WorkflowExecutionHarness harness, Owners owners) =>
        _ = await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(owners.Entry.Scope, owners.Clock.Now);

    private static async ValueTask Close(WorkflowExecutionHarness harness, Owners owners) =>
        _ = await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CloseAsync(new(owners.Entry.Scope.ScopeId,
            WorkflowTestScopeCloseReason.ExplicitTeardown, owners.Clock.Now));

    private static ValueTask<RuntimeTestContextPreparation> Prepare(WorkflowExecutionHarness harness, Owners owners,
        CancellationToken cancellationToken = default) => harness.Services.GetRequiredService<IRuntimeTestContextPreparer>().PrepareAsync(
        owners.Entry, owners.Executable, "prepared", new Dictionary<string, JsonElement>(), new Dictionary<string, JsonElement>(), cancellationToken);

    private static T Physical<T>(WorkflowExecutionHarness harness) where T : class =>
        harness.Services.GetService<CoalescingInner<T>>()?.Value ?? harness.Services.GetRequiredService<T>();

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Owners : IWorkflowTestEntryContextResolver, IRuntimeTestContextSeedSource,
        IRuntimeTestPersistenceBoundary, IRuntimeTestExecutionRoutingBoundary
    {
        internal Clock Clock { get; } = new();
        internal WorkflowExecutable Executable { get; } = WorkflowExecutionHarness.NewExecutable(new ExecutableNode("selected", "selected",
            "test.leaf", "1", "test.leaf", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()));
        internal WorkflowTestEntry Entry { get; }
        internal RuntimeTestPersistenceBoundaryContext? Tuple { get; private set; }
        internal IWorkflowExecutionActorProvider? Actors { get; private set; }
        internal int EntryReads, SeedReads, RoutingReads;
        internal bool DescriptorChanged, SeedChanged, RevokeAfterFirstRead, DescriptorAbsent, SeedAbsent, InvalidSetup;
        internal bool CanPersist = true, CanRoute = true;
        internal string? FailAt;
        internal string? SeedShape;
        internal Func<ValueTask>? BeforeSeed;
        internal Func<RuntimeTestPersistenceBoundaryContext, bool>? PersistencePredicate;

        internal Owners() => Entry = new("trusted-context", "fingerprint", Executable.Identity, "selected",
            new("scope", Clock.Now.AddHours(1), "tenant", new("partition")), new("system", "initiator"));

        public ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default)
        {
            EntryReads++; Fail("resolver");
            Assert.Equal(Entry.ContextId, contextId);
            return ValueTask.FromResult<WorkflowTestEntry?>(DescriptorAbsent ? null : DescriptorChanged ? new(Entry.ContextId, "changed", Entry.Executable,
                Entry.ExecutableNodeId, Entry.Scope, Entry.Authority) : RevokeAfterFirstRead && EntryReads > 1 ? null : Entry);
        }

        public async ValueTask<RuntimeTestContextSeed?> ResolveAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
            string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            SeedReads++; Fail("seed");
            Assert.Same(Entry, entry); Assert.Same(Executable, executable); Assert.Equal("prepared", workflowExecutionId);
            if (BeforeSeed is not null) await BeforeSeed();
            if (SeedAbsent) return null;
            var seed = new RuntimeTestContextSeed(Entry.ContextId, SeedChanged ? "changed" : Entry.ContextFingerprint, [],
                InvalidSetup ? [new("missing-producer", "scope", ValueEnvelope.Null(new("Int32"), ValueProtectionPolicy.InstanceInline))] : []);
            return SeedShape switch
            {
                "frames-null" => seed with { Frames = null! },
                "results-null" => seed with { Results = null! },
                "frame-null" => seed with { Frames = [null!] },
                "fixture-null" => seed with { Results = [null!] },
                "frame-values-null" => seed with { Frames = [new("scope", VariableFrameKind.Container, null!)] },
                "root-value-null" => seed with { RootVariables = new Dictionary<string, ValueEnvelope> { ["amount"] = null! } },
                _ => seed
            };
        }

        public bool CanPersistTestEntry(RuntimeTestPersistenceBoundaryContext context)
        { Fail("persistence"); Tuple = context; return PersistencePredicate?.Invoke(context) ?? CanPersist; }

        public ValueTask<bool> CanRouteTestEntryAsync(WorkflowTestEntry entry, IWorkflowExecutionActorProvider actors,
            CancellationToken cancellationToken = default)
        { RoutingReads++; Fail("routing"); Actors = actors; Assert.Same(Entry, entry); return ValueTask.FromResult(CanRoute); }

        private void Fail(string owner)
        {
            if (FailAt == owner) throw new InvalidOperationException("private-provider-data");
            if (FailAt == owner + "-cancel") throw new OperationCanceledException("private-provider-data");
        }
    }
}
