using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Resumption;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>Service-recreation coverage for successful waited DispatchWorkflow completion.</summary>
public sealed class DispatchWorkflowWaitCrashTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 15, 0, 0, TimeSpan.Zero);
    private const string ParentWorkflowExecutionId = "parent-wait";
    private const string ParentActivityExecutionId = "activity-wait";
    private const string SensitiveValue = "TOP-SECRET-NEVER-LEAK";

    public enum WaitCrashBoundary
    {
        BeforeWaitCommit,
        AfterWaitCommit,
        DuringChildStartClaim,
        AfterChildStartMaterialization,
        BeforeChildCompletionAndOutputCapture,
        AfterChildCompletionAndResumeIntentCommit,
        DuringParentResumeClaim,
        BeforeBookmarkConsumptionAndParentPropagation,
        AfterBookmarkConsumptionAndParentPropagationBeforeAcknowledgement,
        AfterAcknowledgement
    }

    [Fact]
    public async Task ChildCompleted_commit_and_replay_preserve_one_exact_safe_resume_intent()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var commit = NewChildCompletedCommit();
        string committedPayload;

        await using (var completing = BuildProvider(store, new CaptureDisclosedOutputPolicy()))
        {
            await SeedStartedChildAsync(completing);

            var result = await completing.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);

            Assert.True(result.Succeeded);
            var identity = Identity();
            var item = Assert.IsType<RuntimePostCommitOutboxItem>(
                await completing.GetRequiredService<IPostCommitOutboxLookupStore>()
                    .FindAsync(identity.ParentResumeOutboxItemId(commit.CommitId)));
            Assert.Equal(RuntimePostCommitOutboxStatus.Pending, item.Status);
            Assert.True(item.RetryPolicy.RetryUntilAcknowledged);
            committedPayload = item.Intent.Payload!.Value.GetRawText();
            AssertSafeResult(item.Intent, expectedDisclosedValue: "visible-value");
            Assert.DoesNotContain(SensitiveValue, committedPayload, StringComparison.Ordinal);
            Assert.Equal(
                WorkflowExecutionStatus.Completed,
                (await completing.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(identity.ChildWorkflowExecutionId))!.Status);
            Assert.Equal(
                WorkflowDispatchStatus.Completed,
                (await completing.GetRequiredService<IWorkflowDispatchStore>().FindAsync(identity.DispatchId))!.Status);
            Assert.Equal(
                2,
                (await completing.GetRequiredService<IDurableValueStateStore>()
                    .ListAsync(identity.ChildWorkflowExecutionId)).Count);
            Assert.Equal(
                item.OutboxItemId,
                Assert.Single(await completing.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(
                    new RuntimePostCommitOutboxQuery(
                        Now,
                        10,
                        ParentWorkflowExecutionId,
                        intentKind: DispatchWorkflowConstants.ResumeParentIntentKind))).OutboxItemId);
        }

        var throwingOutputSource = new ThrowingOutputSource();
        await using (var replaying = BuildProvider(store, new CaptureDisclosedOutputPolicy(), throwingOutputSource))
        {
            var replay = await replaying.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);

            Assert.True(replay.Succeeded);
            Assert.Equal(0, throwingOutputSource.ReadCount);
            var item = Assert.IsType<RuntimePostCommitOutboxItem>(
                await replaying.GetRequiredService<IPostCommitOutboxLookupStore>()
                    .FindAsync(Identity().ParentResumeOutboxItemId(commit.CommitId)));
            Assert.Equal(committedPayload, item.Intent.Payload!.Value.GetRawText());
            AssertSafeResult(item.Intent, expectedDisclosedValue: "visible-value");
            Assert.DoesNotContain(SensitiveValue, item.Intent.Payload.Value.GetRawText(), StringComparison.Ordinal);
            Assert.Single(await replaying.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(
                new RuntimePostCommitOutboxQuery(
                    Now,
                    10,
                    ParentWorkflowExecutionId,
                    intentKind: DispatchWorkflowConstants.ResumeParentIntentKind)));
        }
    }

    [Fact]
    public async Task Provider_backed_wait_cycle_converges_across_restart_expired_claim_and_uncertain_ack()
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var timeProvider = new MutableTimeProvider(Now);
        var bookmarkObserver = new RecordingBookmarkObserver();
        var lostAcknowledgement = new LostAcknowledgementProbe();
        WorkflowDispatchIdentity cycleIdentity;
        RuntimePostCommitIntent intent;
        string outboxItemId;
        RuntimePostCommitOutboxClaim expiredClaim;

        // Generation 1 executes the actual parent graph. Its one checkpoint durably suspends the
        // DispatchWorkflow activity, creates the deterministic bookmark and start responsibility,
        // and leaves the child invisible until global outbox delivery.
        await using (var suspending = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
        {
            await SeedExecutableGraphAsync(suspending);
            await StartParentAsync(suspending);

            cycleIdentity = await FindIdentityAsync(suspending);
            Assert.Equal(ActivityExecutionStatus.Suspended, (await FindParentActivityAsync(suspending)).Status);
            Assert.NotNull(await suspending.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(ParentWorkflowExecutionId, cycleIdentity.WaitBookmarkId));
            Assert.Null(await suspending.GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(cycleIdentity.ChildWorkflowExecutionId));
            Assert.Equal(
                WorkflowDispatchStatus.Pending,
                (await suspending.GetRequiredService<IWorkflowDispatchStore>().FindAsync(cycleIdentity.DispatchId))!.Status);
            var start = Assert.Single(await suspending.GetRequiredService<IRuntimePostCommitOutboxStore>()
                .GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                    timeProvider.GetUtcNow(),
                    10,
                    ParentWorkflowExecutionId,
                    intentKind: DispatchWorkflowConstants.StartChildIntentKind)));
            Assert.Equal(cycleIdentity.StartIntentId, start.Intent.IntentId);
        }

        // Generation 2 delivers the real start intent and executes the actual child graph. Child
        // completion, safe output projection, dispatch completion and the resume responsibility
        // land durably. Claim the resume item and then lose the process before delivery.
        await using (var completingChild = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
        {
            var sweep = await SweepAsync(completingChild);
            Assert.Equal(1, sweep.OutboxDeliveredCount);

            var identity = await FindIdentityAsync(completingChild);
            Assert.Equal(cycleIdentity.DispatchId, identity.DispatchId);
            Assert.Equal(cycleIdentity.ChildWorkflowExecutionId, identity.ChildWorkflowExecutionId);
            Assert.Equal(cycleIdentity.WaitBookmarkId, identity.WaitBookmarkId);
            Assert.Equal(
                WorkflowExecutionStatus.Completed,
                (await completingChild.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(identity.ChildWorkflowExecutionId))!.Status);
            Assert.Equal(
                WorkflowDispatchStatus.Completed,
                (await completingChild.GetRequiredService<IWorkflowDispatchStore>().FindAsync(identity.DispatchId))!.Status);
            var childOutputs = await completingChild.GetRequiredService<IDurableValueStateStore>()
                .ListAsync(identity.ChildWorkflowExecutionId);
            Assert.Equal(2, childOutputs.Count);
            Assert.Contains(childOutputs, output => StringComparer.Ordinal.Equals(
                output.ValueId,
                RuntimeWorkflowStateSeed.OutputValueIdPrefix + "disclosed"));
            Assert.Contains(childOutputs, output => StringComparer.Ordinal.Equals(
                output.ValueId,
                RuntimeWorkflowStateSeed.OutputValueIdPrefix + "secret"));
            Assert.Equal(ActivityExecutionStatus.Suspended, (await FindParentActivityAsync(completingChild)).Status);
            Assert.NotNull(await completingChild.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(ParentWorkflowExecutionId, identity.WaitBookmarkId));

            var resume = Assert.Single(await completingChild.GetRequiredService<IRuntimePostCommitOutboxStore>()
                .GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                    timeProvider.GetUtcNow(),
                    10,
                    ParentWorkflowExecutionId,
                    intentKind: DispatchWorkflowConstants.ResumeParentIntentKind)));
            intent = resume.Intent;
            outboxItemId = resume.OutboxItemId;
            Assert.True(resume.RetryPolicy.RetryUntilAcknowledged);
            AssertSafeResult(
                intent,
                expectedDisclosedValue: "visible-value",
                expectedDisclosedName: nameof(ChildOutputActivity.Disclosed),
                expectedSecretName: nameof(ChildOutputActivity.Secret));
            Assert.DoesNotContain(SensitiveValue, intent.Payload!.Value.GetRawText(), StringComparison.Ordinal);

            expiredClaim = Assert.Single(await completingChild.GetRequiredService<IRuntimePostCommitOutboxClaimStore>()
                .ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                    "worker-before-crash",
                    timeProvider.GetUtcNow(),
                    TimeSpan.FromMinutes(1),
                    1,
                    ParentWorkflowExecutionId,
                    DispatchWorkflowConstants.ResumeParentIntentKind)));
        }

        timeProvider.Advance(TimeSpan.FromMinutes(2));

        // Generation 3 reclaims the expired fence, delivers through the real bookmark dispatcher,
        // invokes DispatchWorkflow's resume target and commits ordinary parent graph completion.
        // The provider commits Delivered and then simulates process loss before the caller observes it.
        await using (var resumingParent = BuildExecutableProvider(
                         store,
                         timeProvider,
                         bookmarkObserver,
                         lostAcknowledgement))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                SweepAsync(resumingParent).AsTask());

            Assert.Equal(1, lostAcknowledgement.LostCount);
            Assert.True(lostAcknowledgement.ReclaimedFencingToken > expiredClaim.FencingToken);
            Assert.Equal(cycleIdentity.ParentResumeIdempotencyKey, lostAcknowledgement.IdempotencyKey);
            Assert.Single(bookmarkObserver.Consumed, bookmark =>
                bookmark.WorkflowExecutionId == ParentWorkflowExecutionId &&
                bookmark.BookmarkId == cycleIdentity.WaitBookmarkId);
            Assert.Null(await resumingParent.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(ParentWorkflowExecutionId, cycleIdentity.WaitBookmarkId));
            Assert.Equal(ActivityExecutionStatus.Completed, (await FindParentActivityAsync(resumingParent)).Status);
            Assert.Equal(
                WorkflowExecutionStatus.Completed,
                (await resumingParent.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(ParentWorkflowExecutionId))!.Status);
        }

        // Generation 4 observes the durable acknowledgement and converged completed graph. No
        // duplicate resume is deliverable, and the parent result contains the disclosed output but
        // never the sensitive value.
        await using (var recovered = BuildProvider(store))
        {
            var persisted = Assert.IsType<RuntimePostCommitOutboxItem>(
                await recovered.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(outboxItemId));
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, persisted.Status);
            Assert.Equal(intent.Payload!.Value.GetRawText(), persisted.Intent.Payload!.Value.GetRawText());
            AssertSafeResult(
                persisted.Intent,
                expectedDisclosedValue: "visible-value",
                expectedDisclosedName: nameof(ChildOutputActivity.Disclosed),
                expectedSecretName: nameof(ChildOutputActivity.Secret));
            Assert.DoesNotContain(SensitiveValue, persisted.Intent.Payload.Value.GetRawText(), StringComparison.Ordinal);
            Assert.Empty(await recovered.GetRequiredService<IRuntimePostCommitOutboxClaimStore>().ClaimAsync(
                new RuntimePostCommitOutboxClaimRequest("completion-audit", Now.AddYears(1), TimeSpan.FromMinutes(1), 10)));
            Assert.Null(await recovered.GetRequiredService<IBookmarkStateStore>().FindAsync(
                ParentWorkflowExecutionId,
                cycleIdentity.WaitBookmarkId));
            Assert.Equal(ActivityExecutionStatus.Completed, (await FindParentActivityAsync(recovered)).Status);
            Assert.Equal(
                WorkflowExecutionStatus.Completed,
                (await recovered.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(ParentWorkflowExecutionId))!.Status);
            AssertSafeParentResult(await recovered.GetRequiredService<IDurableValueStateStore>()
                .ListAsync(ParentWorkflowExecutionId));
        }
    }

    [Theory]
    [InlineData(WaitCrashBoundary.BeforeWaitCommit)]
    [InlineData(WaitCrashBoundary.AfterWaitCommit)]
    [InlineData(WaitCrashBoundary.DuringChildStartClaim)]
    [InlineData(WaitCrashBoundary.AfterChildStartMaterialization)]
    [InlineData(WaitCrashBoundary.BeforeChildCompletionAndOutputCapture)]
    [InlineData(WaitCrashBoundary.AfterChildCompletionAndResumeIntentCommit)]
    [InlineData(WaitCrashBoundary.DuringParentResumeClaim)]
    [InlineData(WaitCrashBoundary.BeforeBookmarkConsumptionAndParentPropagation)]
    [InlineData(WaitCrashBoundary.AfterBookmarkConsumptionAndParentPropagationBeforeAcknowledgement)]
    [InlineData(WaitCrashBoundary.AfterAcknowledgement)]
    public async Task Provider_backed_wait_cycle_converges_after_each_named_crash_boundary(
        WaitCrashBoundary boundary)
    {
        var store = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var timeProvider = new MutableTimeProvider(Now);
        var bookmarkObserver = new RecordingBookmarkObserver();
        WorkflowDispatchIdentity? identity = null;
        string? resumeOutboxItemId = null;

        await using (var seeding = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
            await SeedExecutableGraphAsync(seeding);

        if (boundary != WaitCrashBoundary.BeforeWaitCommit)
        {
            await using var suspending = BuildExecutableProvider(store, timeProvider, bookmarkObserver);
            await StartParentAsync(suspending);
            identity = await AssertWaitingParentAsync(suspending);
        }
        else
        {
            await using var beforeWait = BuildExecutableProvider(store, timeProvider, bookmarkObserver);
            Assert.Null(await beforeWait.GetRequiredService<IWorkflowExecutionStateStore>()
                .FindAsync(ParentWorkflowExecutionId));
            Assert.Empty(await beforeWait.GetRequiredService<IRuntimePostCommitOutboxStore>()
                .GetDeliverableAsync(new RuntimePostCommitOutboxQuery(timeProvider.GetUtcNow(), 10)));
        }

        switch (boundary)
        {
            case WaitCrashBoundary.BeforeWaitCommit:
            case WaitCrashBoundary.AfterWaitCommit:
            case WaitCrashBoundary.BeforeChildCompletionAndOutputCapture:
                break;

            case WaitCrashBoundary.DuringChildStartClaim:
                await using (var claimingStart = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
                {
                    var claim = Assert.Single(await claimingStart
                        .GetRequiredService<IRuntimePostCommitOutboxClaimStore>()
                        .ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                            "start-worker-before-crash",
                            timeProvider.GetUtcNow(),
                            TimeSpan.FromMinutes(1),
                            1,
                            ParentWorkflowExecutionId,
                            DispatchWorkflowConstants.StartChildIntentKind)));
                    Assert.Equal(identity!.StartIntentId, claim.Item.Intent.IntentId);
                    Assert.Null(await claimingStart.GetRequiredService<IWorkflowExecutionStateStore>()
                        .FindAsync(identity.ChildWorkflowExecutionId));
                }
                timeProvider.Advance(TimeSpan.FromMinutes(2));
                break;

            case WaitCrashBoundary.AfterChildStartMaterialization:
                await using (var materializingChild = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
                {
                    var start = Assert.Single(await materializingChild
                        .GetRequiredService<IRuntimePostCommitOutboxStore>()
                        .GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                            timeProvider.GetUtcNow(),
                            1,
                            ParentWorkflowExecutionId,
                            intentKind: DispatchWorkflowConstants.StartChildIntentKind)));
                    await materializingChild.GetRequiredService<ChildStartExecutor>()
                        .HandleAsync(start.Intent);
                    Assert.Equal(
                        WorkflowExecutionStatus.Completed,
                        (await materializingChild.GetRequiredService<IWorkflowExecutionStateStore>()
                            .FindAsync(identity!.ChildWorkflowExecutionId))!.Status);
                    resumeOutboxItemId = await AssertSafeResumeIntentAsync(materializingChild);
                }
                break;

            case WaitCrashBoundary.AfterChildCompletionAndResumeIntentCommit:
            case WaitCrashBoundary.BeforeBookmarkConsumptionAndParentPropagation:
                await using (var completingChild = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
                {
                    Assert.Equal(1, (await SweepAsync(completingChild)).OutboxDeliveredCount);
                    resumeOutboxItemId = await AssertSafeResumeIntentAsync(completingChild);
                }
                break;

            case WaitCrashBoundary.DuringParentResumeClaim:
                await using (var completingChild = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
                {
                    Assert.Equal(1, (await SweepAsync(completingChild)).OutboxDeliveredCount);
                    resumeOutboxItemId = await AssertSafeResumeIntentAsync(completingChild);
                    var claim = Assert.Single(await completingChild
                        .GetRequiredService<IRuntimePostCommitOutboxClaimStore>()
                        .ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                            "resume-worker-before-crash",
                            timeProvider.GetUtcNow(),
                            TimeSpan.FromMinutes(1),
                            1,
                            ParentWorkflowExecutionId,
                            DispatchWorkflowConstants.ResumeParentIntentKind)));
                    Assert.Equal(identity!.ParentResumeIntentId, claim.Item.Intent.IntentId);
                }
                timeProvider.Advance(TimeSpan.FromMinutes(2));
                break;

            case WaitCrashBoundary.AfterBookmarkConsumptionAndParentPropagationBeforeAcknowledgement:
                await using (var completingChild = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
                {
                    Assert.Equal(1, (await SweepAsync(completingChild)).OutboxDeliveredCount);
                    resumeOutboxItemId = await AssertSafeResumeIntentAsync(completingChild);
                }
                await using (var consumingBookmark = BuildExecutableProvider(
                                 store,
                                 timeProvider,
                                 bookmarkObserver,
                                 loseBeforeAcknowledgement: true))
                {
                    await Assert.ThrowsAsync<OperationCanceledException>(() =>
                        SweepAsync(consumingBookmark).AsTask());
                    Assert.Null(await consumingBookmark.GetRequiredService<IBookmarkStateStore>()
                        .FindAsync(ParentWorkflowExecutionId, identity!.WaitBookmarkId));
                    Assert.Equal(ActivityExecutionStatus.Completed, (await FindParentActivityAsync(consumingBookmark)).Status);
                    Assert.Equal(
                        WorkflowExecutionStatus.Completed,
                        (await consumingBookmark.GetRequiredService<IWorkflowExecutionStateStore>()
                            .FindAsync(ParentWorkflowExecutionId))!.Status);
                }
                timeProvider.Advance(TimeSpan.FromMinutes(2));
                break;

            case WaitCrashBoundary.AfterAcknowledgement:
                await using (var completingChild = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
                {
                    Assert.Equal(1, (await SweepAsync(completingChild)).OutboxDeliveredCount);
                    resumeOutboxItemId = await AssertSafeResumeIntentAsync(completingChild);
                }
                await using (var resumingParent = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
                    Assert.Equal(1, (await SweepAsync(resumingParent)).OutboxDeliveredCount);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(boundary), boundary, null);
        }

        if (identity is null)
        {
            await using var suspending = BuildExecutableProvider(store, timeProvider, bookmarkObserver);
            await StartParentAsync(suspending);
            identity = await AssertWaitingParentAsync(suspending);
        }

        for (var pass = 0; pass < 3; pass++)
        {
            await using var recovering = BuildExecutableProvider(store, timeProvider, bookmarkObserver);
            await SweepAsync(recovering);
            resumeOutboxItemId ??= await TryAssertSafeResumeIntentAsync(recovering);
            timeProvider.Advance(TimeSpan.FromMinutes(2));
        }

        await using (var auditing = BuildExecutableProvider(store, timeProvider, bookmarkObserver))
        {
            var child = Assert.IsType<WorkflowExecutionState>(
                await auditing.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(identity.ChildWorkflowExecutionId));
            Assert.Equal(WorkflowExecutionStatus.Completed, child.Status);
            Assert.Equal(
                WorkflowDispatchStatus.Completed,
                (await auditing.GetRequiredService<IWorkflowDispatchStore>().FindAsync(identity.DispatchId))!.Status);
            Assert.Null(await auditing.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(ParentWorkflowExecutionId, identity.WaitBookmarkId));
            Assert.Equal(ActivityExecutionStatus.Completed, (await FindParentActivityAsync(auditing)).Status);
            Assert.Equal(
                WorkflowExecutionStatus.Completed,
                (await auditing.GetRequiredService<IWorkflowExecutionStateStore>()
                    .FindAsync(ParentWorkflowExecutionId))!.Status);
            Assert.Single(bookmarkObserver.Consumed, bookmark =>
                bookmark.WorkflowExecutionId == ParentWorkflowExecutionId &&
                bookmark.BookmarkId == identity.WaitBookmarkId);
            AssertSafeParentResult(await auditing.GetRequiredService<IDurableValueStateStore>()
                .ListAsync(ParentWorkflowExecutionId));

            Assert.NotNull(resumeOutboxItemId);
            var resume = Assert.IsType<RuntimePostCommitOutboxItem>(
                await auditing.GetRequiredService<IPostCommitOutboxLookupStore>()
                    .FindAsync(resumeOutboxItemId));
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, resume.Status);
            AssertSafeResult(
                resume.Intent,
                "visible-value",
                nameof(ChildOutputActivity.Disclosed),
                nameof(ChildOutputActivity.Secret));
            Assert.DoesNotContain(SensitiveValue, resume.Intent.Payload!.Value.GetRawText(), StringComparison.Ordinal);
            Assert.Empty(await auditing.GetRequiredService<IRuntimePostCommitOutboxClaimStore>().ClaimAsync(
                new RuntimePostCommitOutboxClaimRequest(
                    "matrix-completion-audit",
                    timeProvider.GetUtcNow().AddYears(1),
                    TimeSpan.FromMinutes(1),
                    10)));
        }
    }

    private static ServiceProvider BuildExecutableProvider(
        IDocumentStore store,
        MutableTimeProvider timeProvider,
        RecordingBookmarkObserver bookmarkObserver,
        LostAcknowledgementProbe? lostAcknowledgement = null,
        bool loseBeforeAcknowledgement = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<IRuntimePayloadCapturePolicy>(new CaptureDisclosedOutputPolicy());
        services.AddSingleton<IActivityConstructor, DispatchActivityConstructor>();
        services.AddSingleton<IActivityConstructor, ChildOutputActivityConstructor>();
        services.AddSingleton<IBookmarkLifecycleObserver>(bookmarkObserver);
        var typeRegistry = new WellKnownTypeRegistry();
        typeRegistry.RegisterType(typeof(string), "String");
        services.AddSingleton<IWellKnownTypeRegistry>(typeRegistry);

        new SerializationFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        new DispatchWorkflowRuntimeFeature().ConfigureServices(services);
        services.Replace(ServiceDescriptor.Singleton<IWellKnownTypeRegistry>(typeRegistry));

        services.AddSingleton(store);
        services.AddGroundworkRuntimeStores();
        services.RemoveAll<IPersistenceAccessContextAccessor>();
        services.RemoveAll<IPersistenceAccessContextBinder>();
        services.AddScoped<ProviderCyclePersistenceAccessContext>();
        services.AddScoped<IPersistenceAccessContextAccessor>(serviceProvider =>
            serviceProvider.GetRequiredService<ProviderCyclePersistenceAccessContext>());
        services.AddScoped<IPersistenceAccessContextBinder>(serviceProvider =>
            serviceProvider.GetRequiredService<ProviderCyclePersistenceAccessContext>());
        services.RemoveAll<IWorkflowExecutableRootWriteLeaseManager>();
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughRootWriteLeaseManager.Instance);

        if (lostAcknowledgement is not null)
        {
            services.RemoveAll<IRuntimePostCommitOutboxStore>();
            services.AddScoped<IRuntimePostCommitOutboxStore>(serviceProvider =>
                new LoseResumeAcknowledgementOutboxStore(
                    serviceProvider.GetRequiredService<GroundworkRuntimePostCommitOutboxStore>(),
                    lostAcknowledgement));
        }
        else if (loseBeforeAcknowledgement)
        {
            services.RemoveAll<IRuntimePostCommitOutboxStore>();
            services.AddScoped<IRuntimePostCommitOutboxStore>(serviceProvider =>
                new ResumeAckFailingOutboxStore(
                    serviceProvider.GetRequiredService<GroundworkRuntimePostCommitOutboxStore>()));
        }

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IActivityConstructorRegistry>()
            .AddAll(provider.GetServices<IActivityConstructor>());
        return provider;
    }

    private static async Task SeedExecutableGraphAsync(ServiceProvider provider)
    {
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewChildExecutable());
        await provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(ChildSourceReference());
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewParentExecutable());
        await provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(ParentSourceReference());
    }

    private static async Task StartParentAsync(ServiceProvider provider)
    {
        var authority = new WorkflowExecutionAuthoritySnapshot("parent-caller", "root-initiator");
        await using var scope = provider.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: ParentExecutableIdentity.ArtifactId,
                requestedBy: authority.SystemIdentity,
                workflowExecutionId: ParentWorkflowExecutionId,
                idempotencyKey: $"start:{ParentWorkflowExecutionId}",
                metadata: null,
                variables: null,
                inputs: null,
                stimulusInput: null,
                triggerNodeId: null,
                runKind: WorkflowRunKind.PublishedRun,
                sourceSelection: new WorkflowExecutableSourceSelection(ParentSourceReference().SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null,
                correlationId: "correlation-parent",
                tenantId: "partition-eu",
                partition: new WorkflowExecutionPartition("partition-eu"),
                authority: authority,
                startAuthority: null,
                dispatchNestingDepth: 0));
        Assert.True(
            result.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted,
            $"Parent start faulted: {result.CommandDispatch.Reason}");
    }

    private static async ValueTask<RuntimeResumptionSweepResult> SweepAsync(ServiceProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>()
            .SweepAsync(new RuntimeResumptionSweepRequest(), timeout.Token);
    }

    private static async Task<WorkflowDispatchIdentity> AssertWaitingParentAsync(ServiceProvider provider)
    {
        var identity = await FindIdentityAsync(provider);
        Assert.Equal(ActivityExecutionStatus.Suspended, (await FindParentActivityAsync(provider)).Status);
        Assert.NotNull(await provider.GetRequiredService<IBookmarkStateStore>()
            .FindAsync(ParentWorkflowExecutionId, identity.WaitBookmarkId));
        Assert.Null(await provider.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(identity.ChildWorkflowExecutionId));
        Assert.Equal(
            WorkflowDispatchStatus.Pending,
            (await provider.GetRequiredService<IWorkflowDispatchStore>().FindAsync(identity.DispatchId))!.Status);
        return identity;
    }

    private static async Task<string> AssertSafeResumeIntentAsync(ServiceProvider provider)
    {
        var item = Assert.Single(await provider.GetRequiredService<IRuntimePostCommitOutboxStore>()
            .GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                Now.AddYears(1),
                10,
                ParentWorkflowExecutionId,
                intentKind: DispatchWorkflowConstants.ResumeParentIntentKind)));
        AssertSafeResult(
            item.Intent,
            "visible-value",
            nameof(ChildOutputActivity.Disclosed),
            nameof(ChildOutputActivity.Secret));
        Assert.DoesNotContain(SensitiveValue, item.Intent.Payload!.Value.GetRawText(), StringComparison.Ordinal);
        return item.OutboxItemId;
    }

    private static async Task<string?> TryAssertSafeResumeIntentAsync(ServiceProvider provider)
    {
        var items = await provider.GetRequiredService<IRuntimePostCommitOutboxStore>()
            .GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
                Now.AddYears(1),
                10,
                ParentWorkflowExecutionId,
                intentKind: DispatchWorkflowConstants.ResumeParentIntentKind));
        if (items.Count == 0)
            return null;
        var item = Assert.Single(items);
        AssertSafeResult(
            item.Intent,
            "visible-value",
            nameof(ChildOutputActivity.Disclosed),
            nameof(ChildOutputActivity.Secret));
        Assert.DoesNotContain(SensitiveValue, item.Intent.Payload!.Value.GetRawText(), StringComparison.Ordinal);
        return item.OutboxItemId;
    }

    private static async Task<ActivityExecutionState> FindParentActivityAsync(ServiceProvider provider) =>
        Assert.Single(await provider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAsync(ParentWorkflowExecutionId));

    private static async Task<WorkflowDispatchIdentity> FindIdentityAsync(ServiceProvider provider)
    {
        var activity = await FindParentActivityAsync(provider);
        return new WorkflowDispatchIdentity(ParentWorkflowExecutionId, activity.Execution.ActivityExecutionId);
    }

    private static void AssertSafeParentResult(IReadOnlyCollection<DurableValueState> values)
    {
        var resultState = Assert.Single(values, value =>
            StringComparer.Ordinal.Equals(value.ValueId, "dispatch-result-provider-cycle"));
        var result = resultState.InlineValue!.Value.Deserialize<DispatchWorkflowResult>(SerializerOptions);
        Assert.NotNull(result);
        var outputs = result.Outputs.OrderBy(output => output.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, outputs.Length);
        Assert.Equal(nameof(ChildOutputActivity.Disclosed), outputs[0].Name);
        Assert.Equal("visible-value", outputs[0].Value!.Value.GetString());
        Assert.False(outputs[0].IsRedacted);
        Assert.Equal(nameof(ChildOutputActivity.Secret), outputs[1].Name);
        Assert.True(outputs[1].IsRedacted);
        Assert.Null(outputs[1].Value);
        Assert.DoesNotContain(SensitiveValue, resultState.InlineValue.Value.GetRawText(), StringComparison.Ordinal);
    }

    private static readonly WorkflowExecutableIdentity ChildExecutableIdentity =
        new("artifact-child-provider", "definition-child-provider", "version-child-provider", "1.0.0", "sha256:child-provider");
    private static readonly WorkflowExecutableIdentity ParentExecutableIdentity =
        new("artifact-parent-provider", "definition-parent-provider", "version-parent-provider", "1.0.0", "sha256:parent-provider");

    private static WorkflowExecutable NewChildExecutable()
    {
        var node = NewNode(
            "node-child-output",
            "test/dispatch-child-output",
            ChildOutputActivityConstructor.ConsumerKeyValue,
            JsonSerializer.SerializeToElement(new ChildOutputDescriptor()),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                [nameof(ChildOutputActivity.Disclosed)] = new(
                    nameof(ChildOutputActivity.Disclosed),
                    RuntimeWorkflowStateSeed.OutputValueIdPrefix + "disclosed",
                    new RuntimeValueTypeDescriptor("clr", typeof(string).FullName, null),
                    DurableValueLifecycle.Result,
                    DurableValueStorage.Inline,
                    true),
                [nameof(ChildOutputActivity.Secret)] = new(
                    nameof(ChildOutputActivity.Secret),
                    RuntimeWorkflowStateSeed.OutputValueIdPrefix + "secret",
                    new RuntimeValueTypeDescriptor("clr", typeof(string).FullName, null),
                    DurableValueLifecycle.Result,
                    DurableValueStorage.Inline,
                    true,
                    new Dictionary<string, string> { [RuntimeMetadataKeys.IsSensitive] = "true" })
            });
        return new WorkflowExecutable(
            ChildExecutableIdentity,
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now,
            new Dictionary<string, string>(),
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []),
            []);
    }

    private static WorkflowExecutable NewParentExecutable()
    {
        var inputs = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(DispatchWorkflowActivity.WorkflowDefinitionId)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.WorkflowDefinitionId),
                ChildExecutableIdentity.DefinitionId,
                typeof(string)),
            [nameof(DispatchWorkflowActivity.WaitForCompletion)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.WaitForCompletion),
                true,
                typeof(bool))
        };
        var node = NewNode(
            "node-dispatch",
            DispatchWorkflowConstants.ActivityType,
            DispatchActivityConstructor.ConsumerKeyValue,
            JsonSerializer.SerializeToElement(new DispatchActivityDescriptor()),
            inputs,
            new Dictionary<string, RuntimeOutputCapture>
            {
                [nameof(DispatchWorkflowActivity.ChildWorkflowExecutionId)] = new(
                    nameof(DispatchWorkflowActivity.ChildWorkflowExecutionId),
                    "dispatch-child-id-provider-cycle",
                    new RuntimeValueTypeDescriptor("clr", typeof(string).FullName, null),
                    DurableValueLifecycle.Instance,
                    DurableValueStorage.Inline,
                    true),
                [nameof(DispatchWorkflowActivity.Result)] = new(
                    nameof(DispatchWorkflowActivity.Result),
                    "dispatch-result-provider-cycle",
                    new RuntimeValueTypeDescriptor("clr", typeof(DispatchWorkflowResult).FullName, null),
                    DurableValueLifecycle.Result,
                    DurableValueStorage.Inline,
                    true)
            },
            new Dictionary<string, string>
            {
                [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(
                    new DispatchWorkflowPin(
                        ChildExecutableIdentity,
                        WorkflowExecutableSourceProvenance.From(ChildSourceReference())),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))
            });
        return new WorkflowExecutable(
            ParentExecutableIdentity,
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [DispatchWorkflowConstants.CompletionResumeTargetId] = new(
                    DispatchWorkflowConstants.CompletionResumeTargetId,
                    node.ExecutableNodeId,
                    "OnChildCompletedAsync",
                    new Dictionary<string, string>())
            },
            Now,
            new Dictionary<string, string>(),
            inputContract: null,
            dependencies:
            [
                new WorkflowExecutableDependency(
                    ChildExecutableIdentity.ArtifactId,
                    ChildExecutableIdentity.ArtifactHash,
                    [node.ExecutableNodeId])
            ]);
    }

    private static RuntimeInputBinding LiteralBinding(string name, object value, Type type) =>
        new(
            inputName: name,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value, value.GetType()),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = type.AssemblyQualifiedName!
            });

    private static ExecutableNode NewNode(
        string executableNodeId,
        string activityType,
        string consumerKey,
        JsonElement descriptorPayload,
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings = null,
        IReadOnlyDictionary<string, RuntimeOutputCapture>? outputCaptures = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            executableNodeId,
            $"authored-{executableNodeId}",
            activityType,
            "1.0.0",
            new RuntimeActivityDescriptor(
                consumerKey,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                descriptorPayload),
            inputBindings ?? new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures ?? new Dictionary<string, RuntimeOutputCapture>(),
            metadata ?? new Dictionary<string, string>());

    private static WorkflowExecutableSourceReference ChildSourceReference() =>
        NewSourceReference("source-child-provider", ChildExecutableIdentity, "publication-child-provider", "slot-child-provider");

    private static WorkflowExecutableSourceReference ParentSourceReference() =>
        NewSourceReference("source-parent-provider", ParentExecutableIdentity, "publication-parent-provider", "slot-parent-provider");

    private static WorkflowExecutableSourceReference NewSourceReference(
        string sourceReferenceId,
        WorkflowExecutableIdentity identity,
        string publicationId,
        string slotId) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: identity.ArtifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: identity.DefinitionVersionId,
            SourceVersion: identity.ArtifactVersion,
            DefinitionId: identity.DefinitionId,
            DefinitionVersionId: identity.DefinitionVersionId,
            ArtifactVersion: identity.ArtifactVersion,
            CreatedAt: Now,
            PublishedAt: Now,
            Scope: WorkflowExecutableReferenceScope.Published,
            ExpiresAt: null,
            PublicationId: publicationId,
            SlotId: slotId);

    private static ServiceProvider BuildProvider(
        IDocumentStore store,
        IRuntimePayloadCapturePolicy? capturePolicy = null,
        IWorkflowOutputSource? outputSource = null)
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new DispatchWorkflowRuntimeFeature().ConfigureServices(services);
        services.AddSingleton(store);
        services.AddGroundworkRuntimeStores();
        services.RemoveAll<IWorkflowExecutableRootWriteLeaseManager>();
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughRootWriteLeaseManager.Instance);
        if (capturePolicy is not null)
        {
            services.RemoveAll<IRuntimePayloadCapturePolicy>();
            services.AddSingleton(capturePolicy);
        }
        if (outputSource is not null)
        {
            services.RemoveAll<IWorkflowOutputSource>();
            services.AddSingleton(outputSource);
        }
        return services.BuildServiceProvider();
    }

    private static async Task SeedStartedChildAsync(ServiceProvider provider)
    {
        var dispatch = NewDispatch();
        var dispatchStore = provider.GetRequiredService<IWorkflowDispatchStore>();
        await dispatchStore.SaveAsync(dispatch);
        await dispatchStore.SaveAsync(dispatch.TransitionTo(WorkflowDispatchStatus.Started, Now.AddMinutes(-1)));
        await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(
            NewWorkflowState(Identity().ChildWorkflowExecutionId, WorkflowExecutionStatus.Running));
    }

    private static RuntimeCheckpointCommit NewChildCompletedCommit()
    {
        var identity = Identity();
        var execution = NewWorkflowState(identity.ChildWorkflowExecutionId, WorkflowExecutionStatus.Completed);
        var dispatch = NewDispatch()
            .TransitionTo(WorkflowDispatchStatus.Started, Now.AddMinutes(-1))
            .TransitionTo(WorkflowDispatchStatus.Completed, Now);
        var disclosed = NewOutput("disclosed", "visible-value", isSensitive: false);
        var redacted = NewOutput("secret", SensitiveValue, isSensitive: true);
        return new RuntimeCheckpointCommit(
            "commit-child-completed",
            new RuntimeCheckpoint("checkpoint-child-completed", "Completed", execution.WorkflowExecutionId, Now, [], new Dictionary<string, string>()),
            new RuntimeCheckpointStateChangeSet(
                Change(execution.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, execution),
                null,
                [],
                [],
                [Change(disclosed.DurableValueId, RuntimeStateChangeOperation.Upsert, disclosed), Change(redacted.DurableValueId, RuntimeStateChangeOperation.Upsert, redacted)],
                [],
                [],
                [Change(dispatch.DispatchId, RuntimeStateChangeOperation.Upsert, dispatch)]),
            [],
            new Dictionary<string, string>());
    }

    private static void AssertSafeResult(
        RuntimePostCommitIntent intent,
        string expectedDisclosedValue,
        string expectedDisclosedName = "disclosed",
        string expectedSecretName = "secret")
    {
        var payload = intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var outputs = payload.Result.Outputs.OrderBy(output => output.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, outputs.Length);
        Assert.Equal(expectedDisclosedName, outputs[0].Name);
        Assert.Equal(expectedDisclosedValue, outputs[0].Value!.Value.GetString());
        Assert.Equal(expectedSecretName, outputs[1].Name);
        Assert.True(outputs[1].IsRedacted);
        Assert.Null(outputs[1].Value);
    }

    private static WorkflowDispatchRecord NewDispatch()
    {
        var identity = Identity();
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            ParentWorkflowExecutionId,
            ParentActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity("artifact-child", "definition-child", "version-child", "1.0.0", "sha256:child"),
            new WorkflowExecutableSourceProvenance("source-child", "WorkflowDefinitionVersion", "version-child", "1.0.0", "definition-child", "version-child", "1.0.0", "publication-child", "slot-child"),
            WorkflowDispatchMode.WaitForCompletion,
            WorkflowDispatchStatus.Pending,
            null,
            null,
            new WorkflowExecutionPartition("partition-1"),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(ParentWorkflowExecutionId, "initiator"),
            [],
            Now.AddMinutes(-2),
            Now.AddMinutes(-2));
    }

    private static WorkflowExecutionState NewWorkflowState(string workflowExecutionId, WorkflowExecutionStatus status) =>
        new(
            workflowExecutionId,
            new WorkflowExecutableIdentity("artifact-" + workflowExecutionId, "definition-" + workflowExecutionId, "version-" + workflowExecutionId, "1.0.0", "sha256:" + workflowExecutionId),
            status,
            null,
            Now.AddMinutes(-3),
            Now.AddMinutes(-3),
            status == WorkflowExecutionStatus.Completed ? Now : null,
            Now,
            null,
            null,
            null,
            new Dictionary<string, string>());

    private static DurableValueState NewOutput(string name, string value, bool isSensitive)
    {
        var metadata = new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.OutputName] = name
        };
        if (isSensitive)
            metadata[RuntimeMetadataKeys.IsSensitive] = "true";
        return new DurableValueState(
            "durable-output:" + name,
            Identity().ChildWorkflowExecutionId,
            RuntimeWorkflowStateSeed.OutputValueIdPrefix + name,
            new RuntimeValueTypeDescriptor("clr", "System.String", null),
            DurableValueLifecycle.Result,
            DurableValueStorage.Inline,
            JsonSerializer.SerializeToElement(value),
            null,
            null,
            Now,
            metadata);
    }

    private static RuntimeStateChange<T> Change<T>(string id, RuntimeStateChangeOperation operation, T state) =>
        new(id, operation, state, new Dictionary<string, string>());

    private static WorkflowDispatchIdentity Identity() =>
        new(ParentWorkflowExecutionId, ParentActivityExecutionId);

    private sealed class DispatchActivityConstructor : IActivityConstructor<DispatchActivityDescriptor>
    {
        internal const string ConsumerKeyValue = "test/dispatch-workflow";
        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            DispatchActivityDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var activity = new DispatchWorkflowActivity();
            if (inputs is not null)
            {
                if (inputs.TryGetValue(nameof(activity.WorkflowDefinitionId), out var definitionId))
                    activity.WorkflowDefinitionId = (InputArgument<string>)definitionId;
                if (inputs.TryGetValue(nameof(activity.WaitForCompletion), out var waitForCompletion))
                    activity.WaitForCompletion = (InputArgument<bool>)waitForCompletion;
            }

            if (outputs is not null && outputs.TryGetValue(nameof(activity.ChildWorkflowExecutionId), out var childId))
                activity.ChildWorkflowExecutionId = new OutputArgument<string>(childId.MemoryBlockReference());
            if (outputs is not null && outputs.TryGetValue(nameof(activity.Result), out var result))
                activity.Result = new OutputArgument<DispatchWorkflowResult>(result.MemoryBlockReference());
            return new(activity);
        }
    }

    private sealed record DispatchActivityDescriptor;

    private sealed class ChildOutputActivityConstructor : IActivityConstructor<ChildOutputDescriptor>
    {
        internal const string ConsumerKeyValue = "test/dispatch-child-output";
        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            ChildOutputDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var activity = new ChildOutputActivity();
            if (outputs is not null && outputs.TryGetValue(nameof(activity.Disclosed), out var disclosed))
                activity.Disclosed = new OutputArgument<string>(disclosed.MemoryBlockReference());
            if (outputs is not null && outputs.TryGetValue(nameof(activity.Secret), out var secret))
                activity.Secret = new OutputArgument<string>(secret.MemoryBlockReference());
            return new(activity);
        }
    }

    private sealed record ChildOutputDescriptor;

    private sealed class ChildOutputActivity : CodeActivity
    {
        public ChildOutputActivity() : base("test/dispatch-child-output")
        {
        }

        public OutputArgument<string>? Disclosed { get; set; }
        public OutputArgument<string>? Secret { get; set; }

        protected override void Execute(IActivityExecutionContext context)
        {
            context.Set(Disclosed, "visible-value", nameof(Disclosed));
            context.Set(Secret, SensitiveValue, nameof(Secret));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now = _now.Add(amount);
    }

    private sealed class ProviderCyclePersistenceAccessContext :
        IPersistenceAccessContextAccessor,
        IPersistenceAccessContextBinder
    {
        private static readonly PersistenceAccessContext Expected =
            PersistenceAccessContext.Scoped(new PersistenceScope("partition-eu"));

        public PersistenceAccessContext Current { get; private set; } = Expected;

        public void Bind(PersistenceAccessContext context)
        {
            Assert.Equal(Expected, context);
            Current = context;
        }
    }

    private sealed class RecordingBookmarkObserver : IBookmarkLifecycleObserver
    {
        private readonly List<BookmarkState> _consumed = [];
        public IReadOnlyCollection<BookmarkState> Consumed
        {
            get
            {
                lock (_consumed)
                    return _consumed.ToArray();
            }
        }

        public ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default)
        {
            lock (_consumed)
                _consumed.Add(bookmark);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LostAcknowledgementProbe
    {
        public int LostCount { get; private set; }
        public long ReclaimedFencingToken { get; private set; }
        public string? IdempotencyKey { get; private set; }

        public void Record(RuntimePostCommitOutboxClaim claim)
        {
            LostCount++;
            ReclaimedFencingToken = claim.FencingToken;
            IdempotencyKey = claim.Item.Intent.IdempotencyKey;
        }
    }

    private sealed class LoseResumeAcknowledgementOutboxStore(
        GroundworkRuntimePostCommitOutboxStore inner,
        LostAcknowledgementProbe probe) :
        IRuntimePostCommitOutboxStore,
        IRuntimePostCommitOutboxClaimStore,
        IRuntimePostCommitOutboxClaimCompletionStore
    {
        private int _loseNextResumeAcknowledgement = 1;

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default) =>
            inner.GetDeliverableAsync(query, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(result, cancellationToken);

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
            RuntimePostCommitOutboxClaimRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(request, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxClaim claim,
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(claim, result, cancellationToken);

        public async ValueTask CompleteClaimAsync(
            RuntimePostCommitOutboxClaimCompletion completion,
            CancellationToken cancellationToken = default)
        {
            await inner.CompleteClaimAsync(completion, cancellationToken);
            if (completion.DeliveryResult.Status == RuntimePostCommitOutboxStatus.Delivered &&
                StringComparer.Ordinal.Equals(
                    completion.Claim.Item.Intent.Kind,
                    DispatchWorkflowConstants.ResumeParentIntentKind) &&
                Interlocked.Exchange(ref _loseNextResumeAcknowledgement, 0) == 1)
            {
                probe.Record(completion.Claim);
                throw new OperationCanceledException(
                    "Simulated process loss after durable resume acknowledgement but before caller acknowledgement.");
            }
        }
    }

    private sealed class ResumeAckFailingOutboxStore(
        GroundworkRuntimePostCommitOutboxStore inner) :
        IRuntimePostCommitOutboxStore,
        IRuntimePostCommitOutboxClaimStore,
        IRuntimePostCommitOutboxClaimCompletionStore
    {
        private int _loseNextResumeAcknowledgement = 1;

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default) =>
            inner.GetDeliverableAsync(query, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(result, cancellationToken);

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
            RuntimePostCommitOutboxClaimRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ClaimAsync(request, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxClaim claim,
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(claim, result, cancellationToken);

        public ValueTask CompleteClaimAsync(
            RuntimePostCommitOutboxClaimCompletion completion,
            CancellationToken cancellationToken = default)
        {
            if (completion.DeliveryResult.Status == RuntimePostCommitOutboxStatus.Delivered &&
                StringComparer.Ordinal.Equals(
                    completion.Claim.Item.Intent.Kind,
                    DispatchWorkflowConstants.ResumeParentIntentKind) &&
                Interlocked.Exchange(ref _loseNextResumeAcknowledgement, 0) == 1)
            {
                throw new OperationCanceledException(
                    "Simulated process loss after bookmark consumption but before durable resume acknowledgement.");
            }

            return inner.CompleteClaimAsync(completion, cancellationToken);
        }
    }

    private sealed class CaptureDisclosedOutputPolicy : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            request.IsSensitive
                ? new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive output redacted")
                : new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Disclosed output captured");
    }

    private sealed class ThrowingOutputSource : IWorkflowOutputSource
    {
        public int ReadCount { get; private set; }

        public ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(
            string workflowExecutionId,
            IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            throw new InvalidOperationException("Committed resume replay must not recapture outputs.");
        }
    }

}
