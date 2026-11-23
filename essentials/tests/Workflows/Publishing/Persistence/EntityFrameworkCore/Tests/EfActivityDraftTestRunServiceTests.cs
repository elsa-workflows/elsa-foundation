using System.Data.Common;
using System.Text.Json;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Primitives.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

/// <summary>Actual service/Design/Runtime/receipt stores across fresh SQLite contexts; dispatch does not execute a workflow.</summary>
public sealed class EfActivityDraftTestRunServiceTests : IAsyncLifetime
{
    private SqliteTestDatabase publishing = null!, design = null!, runtime = null!;
    private readonly TestClock clock = new();
    private readonly StartActivityDraftTestRun request = new("draft-1", 4, "source-handoff");
    private static readonly ExecutableActivityTemplate Template = ActivityPublicationTestMaterial.Commit().ExecutableTemplate;

    public async Task InitializeAsync()
    {
        publishing = await SqliteTestDatabase.CreateAsync<PublishingSnapshotReviewSqliteDbContext>(options => new(options));
        design = await SqliteTestDatabase.CreateAsync<ActivitiesDesignSqliteDbContext>(options => new(options));
        runtime = await SqliteTestDatabase.CreateAsync<RuntimeSqliteDbContext>(options => new(options));
        await using var context = design.Open<ActivitiesDesignSqliteDbContext>(options => new(options));
        await ActivityPublicationTestMaterial.SeedDraftAsync(context, "definition-1", "draft-1");
    }

    public async Task DisposeAsync() { await publishing.DisposeAsync(); await design.DisposeAsync(); await runtime.DisposeAsync(); }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_EF_service_recovers_a_source_write_cut_through_fresh_contexts_and_keeps_the_winning_payload(bool committed)
    {
        var cut = new SourceWriteCut(committed);
        string runId; string? firstBytes = null, firstIncarnation = null;
        await using (var owner = Open(cut))
        {
            var failure = await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => owner.Service.StartAsync(request));
            runId = failure.TestRunId; Assert.True(cut.Fired); Assert.Contains(cut.Cause, ExceptionChain(failure));
            Assert.Equal(0, owner.Dispatcher.Calls); Assert.Empty(owner.Runtime.ChangeTracker.Entries());
        }
        await using (var verify = Open())
        {
            var receipt = (await verify.Receipts.FindAsync(runId))!; Assert.Equal(ActivityDraftTestRunReceiptStatus.Preparing, receipt.Status);
            Assert.Single(await verify.Runtime.WorkflowExecutables.AsNoTracking().ToArrayAsync());
            var row = (await verify.Runtime.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync()).SingleOrDefault(IsWrapperSource);
            Assert.Equal(committed, row is not null); firstBytes = row?.ContentJson; firstIncarnation = row?.IncarnationId;
        }
        await using (var collection = Open())
        {
            var collector = new WorkflowExecutableReferenceGarbageCollector(collection.Artifacts, collection.Sources,
                collection.Templates, new InMemoryWorkflowExecutionStateStore(), null,
                Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
                clock, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);
            Assert.False(collector.SupportsStimulusDeliveryRetention); // Ordinary Test Run roots; no default cohort activation is claimed.
            await collector.SweepAsync();
            Assert.Equal(committed ? 1 : 0, await collection.Runtime.WorkflowExecutables.CountAsync());
            Assert.NotNull(await collection.Templates.FindAsync(Template.TemplateId));
        }
        clock.Now += TimeSpan.FromSeconds(10);
        await using (var retry = Open())
        {
            var result = await retry.Service.StartAsync(request); Assert.Equal(runId, result.TestRunId); Assert.Equal("DispatchAmbiguous", result.Status);
            Assert.Equal(1, retry.Dispatcher.Calls);
        }
        await using var fresh = Open();
        var final = (await fresh.Receipts.FindAsync(runId))!; Assert.Equal(ActivityDraftTestRunReceiptStatus.DispatchAmbiguous, final.Status);
        var winner = Assert.Single((await fresh.Runtime.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync()).Where(IsWrapperSource));
        if (committed) { Assert.Equal(firstBytes, winner.ContentJson); Assert.Equal(firstIncarnation, winner.IncarnationId); }
        var source = (await fresh.Sources.FindAsync(final.SourceReferenceId!))!;
        Assert.Equal(final.SourceReferenceExpiresAt, source.ExpiresAt); Assert.Equal(final.ArtifactId, source.ArtifactId);
        var actual = (await fresh.Artifacts.FindAsync(final.ArtifactId!))!;
        var templateSource = (await fresh.Sources.FindAsync(actual.RootActivity.Metadata["activity.sourceReferenceId"]))!;
        Assert.Equal(Template.TemplateId, templateSource.ArtifactId); Assert.True(templateSource.IsLive(clock.Now));
        Assert.Single(await fresh.Runtime.WorkflowExecutables.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Actual_EF_service_new_run_after_older_expiry_keeps_independent_template_and_wrapper_sources()
    {
        ActivityDraftTestRunView first;
        await using (var owner = Open()) first = await owner.Service.StartAsync(request);
        clock.Now += TimeSpan.FromMinutes(31);
        await using (var second = Open())
        {
            var next = await second.Service.StartAsync(request with { IdempotencyKey = "another-run" });
            Assert.Equal("DispatchAmbiguous", next.Status); Assert.NotEqual(first.ArtifactId, next.ArtifactId);
            var wrapper = (await second.Artifacts.FindAsync(next.ArtifactId!))!;
            var currentTemplateSource = (await second.Sources.FindAsync(wrapper.RootActivity.Metadata["activity.sourceReferenceId"]))!;
            Assert.True(currentTemplateSource.IsLive(clock.Now));
            Assert.Equal(currentTemplateSource.SourceReferenceId, wrapper.RootActivity.Descriptor.Payload.GetProperty("sourceReferenceId").GetString());
            Assert.False((await second.Sources.FindAsync(first.SourceReferenceId!))!.IsLive(clock.Now));
        }
    }

    [Fact]
    public async Task Actual_EF_service_retry_does_not_restore_a_source_retired_after_committed_acknowledgement_loss()
    {
        var cut = new SourceWriteCut(true); string runId;
        await using (var owner = Open(cut)) runId = (await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => owner.Service.StartAsync(request))).TestRunId;
        WorkflowExecutableSourceReference retired;
        await using (var peer = Open())
        {
            var source = Assert.Single((await peer.Sources.ListAllAsync()).Where(reference => reference.SourceKind == "ActivityDraftTestRun"));
            Assert.True(await peer.Sources.RetireAsync(source.SourceReferenceId, clock.Now, "retired by peer"));
            retired = (await peer.Sources.FindAsync(source.SourceReferenceId))!;
        }
        await using (var retry = Open())
        {
            Assert.Equal(runId, (await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => retry.Service.StartAsync(request))).TestRunId);
            Assert.Equal(0, retry.Dispatcher.Calls);
        }
        await using var fresh = Open(); Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(retired, (await fresh.Sources.FindAsync(retired.SourceReferenceId))!));
        Assert.Equal(ActivityDraftTestRunReceiptStatus.Preparing, (await fresh.Receipts.FindAsync(runId))!.Status);
    }

    [Fact]
    public async Task Actual_EF_service_source_expiry_after_SQL_rolls_back_and_same_receipt_retry_cannot_renew_expiry()
    {
        var expiry = new SourceExpiry(clock); string runId; DateTimeOffset deadline;
        await using (var owner = Open(expiry))
        {
            var failure = await Assert.ThrowsAsync<ActivityDraftTestRunStorageException>(() => owner.Service.StartAsync(request));
            runId = failure.TestRunId; Assert.True(expiry.Fired); Assert.Equal(0, owner.Dispatcher.Calls);
        }
        await using (var fresh = Open())
        {
            var receipt = (await fresh.Receipts.FindAsync(runId))!; deadline = receipt.SourceReferenceExpiresAt;
            Assert.True(deadline <= clock.Now); Assert.Equal(ActivityDraftTestRunReceiptStatus.Preparing, receipt.Status);
            Assert.DoesNotContain(await fresh.Runtime.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync(), IsWrapperSource);
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => fresh.Service.StartAsync(request));
            Assert.Equal(0, fresh.Dispatcher.Calls);
        }
        await using var verify = Open();
        Assert.Equal(deadline, (await verify.Receipts.FindAsync(runId))!.SourceReferenceExpiresAt);
        Assert.DoesNotContain(await verify.Runtime.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync(), IsWrapperSource);
    }

    private Attempt Open(params IInterceptor[] interceptors) => new(
        publishing.Open<PublishingSnapshotReviewSqliteDbContext>(options => new(options)),
        design.Open<ActivitiesDesignSqliteDbContext>(options => new(options)),
        runtime.Open<RuntimeSqliteDbContext>(options => new(options), interceptors), clock);

    private static bool IsWrapperSource(WorkflowExecutableSourceReferenceEntity row)
    {
        using var document = JsonDocument.Parse(row.ContentJson);
        return EfRelationalIdentity.Decode(document.RootElement.GetProperty("reference").GetProperty("sourceKind").GetString()!) == "ActivityDraftTestRun";
    }
    private static IEnumerable<Exception> ExceptionChain(Exception failure) { for (Exception? current = failure; current is not null; current = current.InnerException) yield return current; }
    private sealed class TestClock : TimeProvider { public DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class Attempt : IAsyncDisposable
    {
        private readonly PublishingSnapshotReviewDbContext publishing;
        private readonly ActivitiesDesignDbContext design;
        public Attempt(PublishingSnapshotReviewDbContext publishing, ActivitiesDesignDbContext design, RuntimeDbContext runtime, TimeProvider clock)
        {
            this.publishing = publishing; this.design = design; Runtime = runtime;
            var access = TestAccess.Scoped("default"); var codec = ActivityPublicationScope.RecoveryCodec;
            var definitions = new EfActivityDesignStores(design, access, null, new EfActivityManagementProjectionWriter(design, access));
            Artifacts = new(runtime, access); Sources = new(runtime, access, codec, clock);
            Templates = new(runtime, access, codec, clock);
            Receipts = new(publishing, access); Dispatcher = new();
            Service = new((IActivityDefinitionStore)definitions, (IActivityDefinitionDraftStore)definitions, (IActivityDefinitionLayoutStore)definitions,
                new Compiler(), new Authorization(), Templates, Sources,
                new ActivityTemplatePlacer((IActivityDefinitionVersionPublicationStore)definitions, Templates, Sources, new Sha256ActivityPlacementHasher(),
                    new DefaultActivityStructureService([]), new RuntimeInputBindingCompiler(SerializationServices.TypeRegistry()),
                    new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]))),
                new Elsa.Workflows.Publishing.Services.WorkflowExecutableHasher(), Artifacts, Dispatcher,
                new InMemoryWorkflowTestScopeStore(), new Partition(), Receipts, new InMemoryWorkflowExecutionStateStore(),
                new InMemoryActivityExecutionStateStore(), new DefaultActivityDraftTestRunCancellationPolicy(), null!, null!, clock,
                new WorkflowExecutableRootWriteLeaseManager(Artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock));
        }
        public RuntimeDbContext Runtime { get; }
        public EfWorkflowExecutableStore Artifacts { get; }
        public EfWorkflowExecutableSourceReferenceStore Sources { get; }
        public EfExecutableActivityTemplateStore Templates { get; }
        public EfActivityDraftTestRunStore Receipts { get; }
        public RecordingDispatcher Dispatcher { get; }
        public ActivityDraftTestRunService Service { get; }
        public async ValueTask DisposeAsync() { await publishing.DisposeAsync(); await design.DisposeAsync(); await Runtime.DisposeAsync(); }
    }

    private sealed class Compiler : IActivityTemplateCompiler
    {
        public ValueTask<ActivityTemplateCompilerResult> CompileAsync(ActivityTemplateCompilerRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityTemplateCompilerResult(Template, new(1, 1, 0, 1, 1, request.LayoutBytes, 1), [], []));
    }
    private sealed class Authorization : IActivityPublishingAuthorizationContext { public string? TenantId => null; public bool CanAccessTenant(string? tenantId) => tenantId is null; }
    private sealed class Partition : IWorkflowExecutionPartitionAccessor { public WorkflowExecutionPartition Current => new(WorkflowExecutionPartition.DefaultValue); }
    private sealed class RecordingDispatcher : IWorkflowStartDispatcher
    {
        public int Calls { get; private set; }
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request, WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        { Calls++; throw new InvalidOperationException("Recording dispatcher acknowledgement unavailable; no workflow executes."); }
    }

    private sealed class SourceWriteCut(bool afterCommit) : DbTransactionInterceptor, ISaveChangesInterceptor
    {
        public bool Fired { get; private set; }
        public IOException Cause { get; } = new("Actual wrapper source transaction acknowledgement unavailable.");
        private static bool Targets(DbContext? context) => context?.ChangeTracker.Entries<WorkflowExecutableSourceReferenceEntity>().Any(entry => IsWrapperSource(entry.Entity)) == true;
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        { if (afterCommit && !Fired && Targets(eventData.Context)) { Fired = true; throw Cause; } return Task.CompletedTask; }
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (!afterCommit && !Fired && Targets(eventData.Context)) { Fired = true; throw Cause; } return ValueTask.FromResult(result); }
    }

    private sealed class SourceExpiry(TestClock clock) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!Fired && eventData.Context?.ChangeTracker.Entries<WorkflowExecutableSourceReferenceEntity>().Any(entry => IsWrapperSource(entry.Entity)) == true)
            { Fired = true; clock.Now += TimeSpan.FromMinutes(31); }
            return ValueTask.FromResult(result);
        }
    }
}
