using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Services;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Tests.Publication;
using Elsa.Agent.Specialists.Tests.Recovery;
using Elsa.Primitives.Models;
using Elsa.Secrets.Features;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed partial class SpecialistNativeTests
{
    public static IEnumerable<object[]> InvalidApplicationSources() =>
        from durable in new[] { false, true }
        from defect in new[] { "scope", "privileged", "missing", "payload", "owner", "fence", "expired", "overlay", "parent", "contract", "child", "expired-after-read", "unreadable-receipt" }
        select new object[] { durable, defect };

    [Theory]
    [MemberData(nameof(InvalidApplicationSources))]
    public async Task Settlement_requires_current_physical_source_native_proof_and_authenticated_receipt(bool durable, string defect)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var trace = new LeaseTrace { HoldApplication = true };
        var factory = new FixtureFactory("{\"Article\":\"accepted\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock, trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var work = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), work);
        await DrainAsync(actor);
        clock.Advance(TimeSpan.FromSeconds(1));
        _ = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.AppliedKind));
        Assert.Equal(1, trace.ApplicationAttempts);
        trace.HoldApplication = false;
        clock.Advance(TimeSpan.FromSeconds(6));
        var claims = provider.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var claim = Assert.Single(await claims.ClaimAsync(new("settlement-test", clock.GetUtcNow(), TimeSpan.FromMinutes(1), 10, "run", SpecialistSettlementBoundary.AppliedKind)));
        var access = provider.GetRequiredService<IPersistenceAccessContextAccessor>();
        var scope = access.Current.RequireScope();
        var original = new PostCommitDeliveryContext(claim, scope);
        var (retained, retainedRequest) = await NativeTurnAsync(provider, executable);
        var item = claim.Item;
        var stateStore = provider.GetRequiredService<IActivityExecutionStateStore>();
        var parent = (await stateStore.FindAsync("run", item.Intent.ActivityExecutionId!))!;
        var child = Assert.Single((await stateStore.ListAllAsync("run")).Where(state => state.ParentActivityExecutionId == parent.InvocationId));
        if (defect is "parent" or "contract")
            await stateStore.SaveAsync(defect == "parent" ? parent with { Status = ActivityExecutionStatus.Cancelled } : parent with { ContractIdentity = new(parent.ContractIdentity!.ActivityTypeKey, parent.ContractIdentity.ContractVersion, "changed-contract") });
        if (defect == "child") await stateStore.SaveAsync(child with { Provenance = child.Provenance with { Metadata = new Dictionary<string, string>() } });
        if (defect == "expired") clock.Advance(TimeSpan.FromSeconds(61));
        if (defect == "expired-after-read") trace.AfterAcceptedRead = () => { clock.Advance(TimeSpan.FromSeconds(61)); return ValueTask.CompletedTask; };
        if (defect == "unreadable-receipt") trace.AlwaysLoseAcceptedReceiptRead = true;
        var presented = original;
        if (defect == "scope") presented = new(claim, new("other"));
        if (defect is "missing" or "payload" or "owner" or "fence")
        {
            var intent = item.Intent;
            if (defect == "payload") intent = new(intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt, intent.ActivityExecutionId, intent.IdempotencyKey, Json("{\"version\":999}"));
            var owner = defect == "owner" ? "forged-owner" : claim.OwnerId;
            var fence = defect == "fence" ? claim.FencingToken + 1 : claim.FencingToken;
            var changed = new RuntimePostCommitOutboxItem(defect == "missing" ? "missing:" + intent.IntentId : item.OutboxItemId,
                intent, RuntimePostCommitOutboxStatus.Delivering, item.RecordedAt, item.AvailableAt, item.RetryPolicy, item.DeliveryAttemptCount,
                owner, claim.ClaimedAt, deliveryFencingToken: fence, deliveryVisibleAfter: claim.VisibleAfter);
            presented = new(new(changed, owner, fence, claim.ClaimedAt, claim.VisibleAfter), scope);
        }
        var selectedAccess = defect == "privileged" ? new SettlementAccess(PersistenceAccessContext.PrivilegedScoped(scope, new("test"))) : access;
        var overlay = new OverlayOnlySource(item);
        var handler = defect is "overlay" or "privileged" ? new SpecialistSettlementHandler(provider.GetRequiredService<SpecialistSettlementBoundary>(), stateStore, defect == "overlay" ? overlay : provider.GetRequiredService<IPostCommitOutboxLookupStore>(),
            selectedAccess, clock, provider.GetRequiredService<SpecialistTurnJournal>(), provider.GetRequiredService<ISpecialistTurnStore>()) : provider.GetRequiredService<SpecialistSettlementHandler>();
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(presented).AsTask());
        if (defect is "parent" or "contract" or "child")
        {
            var domain = Assert.IsType<SpecialistTurnException>(failure);
            Assert.Equal("specialist.settlement.failed", domain.Code);
            Assert.Equal(original.SourceOutboxItemId, domain.Data["contextId"]);
            Assert.Equal("run", domain.Data["workflowExecutionId"]);
            Assert.NotNull(domain.InnerException);
        }
        trace.AfterAcceptedRead = null;
        trace.AlwaysLoseAcceptedReceiptRead = false;
        Assert.Equal(1, trace.ApplicationAttempts); // No journal mutation on any source/receipt refusal.
        Assert.Null((await provider.GetRequiredService<SpecialistTurnJournal>().ReadAsync(retained.Handle, retainedRequest.Identity, default)).Document.Applications);
        Assert.Equal(0, overlay.OverlayReads);
        await stateStore.SaveAsync(parent);
        await stateStore.SaveAsync(child);
        if (clock.GetUtcNow() >= claim.VisibleAfter)
            original = new(Assert.Single(await claims.ClaimAsync(new("recovered", clock.GetUtcNow(), TimeSpan.FromMinutes(1), 10, "run", SpecialistSettlementBoundary.AppliedKind))), scope);
        await provider.GetRequiredService<SpecialistSettlementHandler>().HandleAsync(original);
        Assert.Single((await NativeTurnAsync(provider, executable)).Document.Applications!);
        Assert.Equal(2, trace.ApplicationAttempts);
        Assert.Equal(1, factory.Calls);
        await Assert.ThrowsAsync<Elsa.Workflows.Runtime.Core.Exceptions.RuntimePostCommitSourceBoundaryException>(() =>
            provider.GetRequiredService<IRuntimePostCommitIntentDispatcher>().DispatchAsync(item.Intent).AsTask());
        Assert.Equal(2, trace.ApplicationAttempts);
    }

    private sealed class SettlementAccess(PersistenceAccessContext context) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => context;
    }

    private sealed class OverlayOnlySource(RuntimePostCommitOutboxItem item) : IPostCommitOutboxLookupStore
    {
        internal int OverlayReads;
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string id, CancellationToken cancellationToken = default)
        { Interlocked.Increment(ref OverlayReads); return ValueTask.FromResult<RuntimePostCommitOutboxItem?>(item); }
    }

    public static IEnumerable<object[]> ApplicationJourneys() =>
        from durable in new[] { false, true }
        from mode in new[] { "normal", "lost-response", "before-write" }
        from persistence in new[] { PersistenceMode.Balanced, PersistenceMode.Durable }
        select new object[] { durable, mode, persistence };

    [Theory]
    [MemberData(nameof(ApplicationJourneys))]
    public async Task Native_completion_commits_application_responsibility_and_idempotent_settlement(bool durable, string mode, PersistenceMode persistence)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var trace = new LeaseTrace { HoldApplication = mode == "before-write", LoseApplicationResponse = mode == "lost-response" };
        var factory = new FixtureFactory("{\"Article\":\"applied once\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock, trace, persistence);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
        // Native checkpoint timestamps use their runtime clock; make the frozen delivery clock
        // eligible for the physically committed item before explicitly running the outbox worker.
        clock.Advance(TimeSpan.FromSeconds(1));
        _ = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.AppliedKind));
        var (before, request) = await NativeTurnAsync(provider, executable);
        Assert.Equal(SpecialistTurnStatus.Accepted, before.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(1, trace.ApplicationAttempts);
        if (mode == "before-write") Assert.Null(before.Applications);
        else Assert.Single(before.Applications!);
        trace.HoldApplication = false;
        if (mode != "normal")
        {
            clock.Advance(TimeSpan.FromSeconds(6));
            var processed = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.AppliedKind));
            Assert.Equal(1, processed.DeliveredCount);
            Assert.Equal(2, trace.ApplicationAttempts);
        }
        var (after, _) = await NativeTurnAsync(provider, executable);
        var application = Assert.Single(after.Applications!);
        Assert.Equal(request.Identity, application.Identity);
        Assert.Equal(after.ChildInvocationId, application.ChildInvocationId);
        var source = (await provider.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(application.SourceId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, source.Status);
        Assert.Equal(application.SourceFingerprint, RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent));
        Assert.Equal(source.Intent.RecordedAt, application.AppliedAt);
        Assert.True(source.RetryPolicy.RetryUntilAcknowledged);
        Assert.Equal(before.ProtectedResult, after.ProtectedResult);
        Assert.Equal(1, (await provider.GetRequiredService<ISpecialistTurnStore>().ReadConversationAsync(after.Handle, request.Identity, default))!.Revision);
        Assert.Equal(1, factory.Calls);
        if (mode == "normal") await VerifyApplicationCheckpointAsync(provider, request.Identity.ActivityExecutionId);
    }

    private static async ValueTask VerifyApplicationCheckpointAsync(ServiceProvider provider, string parentId)
    {
        var parent = (await provider.GetRequiredService<IActivityExecutionStateStore>().FindAsync("run", parentId))!;
        var other = new RuntimePostCommitIntent("other", "run", "Test.Other", DateTimeOffset.UtcNow, parentId, null, null);
        var changes = new RuntimeCheckpointStateChangeSet(null, null,
            [new(parentId, RuntimeStateChangeOperation.Upsert, parent, new Dictionary<string, string>())], [], [], [], []);
        var commit = new RuntimeCheckpointCommit("application-test-commit",
            new("application-test", RuntimeCheckpointNames.ActivityCompleted, "run", DateTimeOffset.UtcNow, [parentId], new Dictionary<string, string>()),
            changes, [other], new Dictionary<string, string>());
        var enricher = provider.GetRequiredService<IEnumerable<IRuntimeCheckpointCommitEnricher>>().OfType<SpecialistCheckpointEnricher>().Single();
        var enriched = await enricher.EnrichAsync(commit);
        Assert.Equal(2, enriched.PostCommitIntents.Count);
        Assert.Same(other, enriched.PostCommitIntents.Single(item => item.IntentId == "other"));
        await enricher.ValidateFinalAsync(enriched);
        var repeated = await enricher.EnrichAsync(enriched);
        Assert.Equal(enriched.PostCommitIntents.Count, repeated.PostCommitIntents.Count);
        var intent = enriched.PostCommitIntents.Single(item => item.Kind == SpecialistSettlementBoundary.AppliedKind);
        Assert.True(intent.IsEquivalentTo(repeated.PostCommitIntents.Single(item => item.Kind == intent.Kind)));
        await ExpectBoundaryAsync(() => enricher.ValidateFinalAsync(enriched with { PostCommitIntents = [other] }).AsTask());
        var conflicting = new RuntimePostCommitIntent(intent.IntentId, intent.WorkflowExecutionId, intent.Kind, intent.RecordedAt,
            intent.ActivityExecutionId, intent.IdempotencyKey, Json("{\"version\":999}"));
        await ExpectBoundaryAsync(() => enricher.EnrichAsync(commit with { PostCommitIntents = [other, conflicting] }).AsTask());
        await ExpectBoundaryAsync(() => enricher.ValidateFinalAsync(commit with { PostCommitIntents = [other, conflicting] }).AsTask());
        await ExpectBoundaryAsync(() => enricher.ValidateFinalAsync(enriched with { PostCommitIntents = [other, intent, intent] }).AsTask());

        async Task ExpectBoundaryAsync(Func<Task> action)
        {
            var failure = await Assert.ThrowsAsync<SpecialistTurnException>(action);
            Assert.Equal("specialist.settlement.failed", failure.Code);
            Assert.Equal(commit.CommitId, failure.Data["contextId"]);
            Assert.Equal("run", failure.Data["workflowExecutionId"]);
            Assert.IsType<InvalidDataException>(failure.InnerException);
        }
    }

}
