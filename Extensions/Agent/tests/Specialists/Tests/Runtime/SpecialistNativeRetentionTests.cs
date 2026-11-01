using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;
using Elsa.Agent.Specialists.Tests.Recovery;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed partial class SpecialistNativeTests
{
    [Theory]
    [InlineData(false, "normal")]
    [InlineData(true, "normal")]
    [InlineData(false, "before-write")]
    [InlineData(true, "before-write")]
    [InlineData(false, "lost-response")]
    [InlineData(true, "lost-response")]
    public async Task Native_retention_waits_for_completed_owner_and_confirmed_physical_application_delivery(bool durable, string mode)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var trace = new LeaseTrace { HoldApplication = mode == "before-write", LoseApplicationResponse = mode == "lost-response" };
        var factory = new FixtureFactory("{\"Article\":\"retained\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock, trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var work = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), work);
        await DrainAsync(actor);
        var (before, request) = await NativeTurnAsync(provider, executable);
        var workflowStore = provider.GetRequiredService<IWorkflowExecutionStateStore>();
        var workflow = (await workflowStore.FindAsync("run"))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow.Status);
        var query = new SpecialistRetentionQuery(before.Identity.TenantId, before.Identity.PersistencePartition);
        var pruner = provider.GetRequiredService<SpecialistRetentionPruner>();
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(1, (await pruner.PrunePageAsync(query, default)).Pinned["retention-window"]);
        clock.Advance(TimeSpan.FromDays(31));
        await workflowStore.SaveAsync(workflow with { Status = WorkflowExecutionStatus.Running });
        Assert.Equal(1, (await pruner.PrunePageAsync(query, default)).Pinned["owner-active"]);
        await workflowStore.SaveAsync(workflow);
        var pending = await pruner.PrunePageAsync(query, default);
        Assert.Equal(0, pending.Purged);
        Assert.Equal(1, pending.Pinned["application-unconfirmed"]);
        _ = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.AppliedKind));
        if (mode != "normal")
        {
            var blocked = await pruner.PrunePageAsync(query, default);
            Assert.Equal(0, blocked.Purged);
            Assert.Equal(1, blocked.Pinned[mode == "before-write" ? "application-unconfirmed" : "application-pending"]);
            trace.HoldApplication = false;
            clock.Advance(TimeSpan.FromSeconds(6));
            Assert.Equal(1, (await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.AppliedKind))).DeliveredCount);
        }
        var settled = (await provider.GetRequiredService<ISpecialistTurnStore>().ReadAsync(before.Handle, before.Identity, default))!;
        var conversation = await provider.GetRequiredService<ISpecialistTurnStore>().ReadConversationAsync(before.Conversation!.ConversationId, before.Identity, default);
        await workflowStore.SaveAsync(workflow with { Status = WorkflowExecutionStatus.Running });
        var active = await pruner.PrunePageAsync(query, default);
        Assert.Equal(0, active.Purged);
        Assert.Equal(1, active.Pinned["owner-active"]);
        Assert.NotNull((await provider.GetRequiredService<ISpecialistTurnStore>().ReadAsync(before.Handle, before.Identity, default))!.ProtectedResult);
        await workflowStore.SaveAsync(workflow);
        Assert.Equal(1, (await pruner.PrunePageAsync(query, default)).Purged);
        var marker = (await provider.GetRequiredService<ISpecialistTurnStore>().ReadAsync(before.Handle, before.Identity, default))!;
        Assert.Equal(SpecialistTurnStatus.Accepted, marker.Status);
        Assert.Null(marker.ProtectedRequest);
        Assert.Null(marker.ProtectedResult);
        Assert.Equal(settled.Applications, marker.Applications);
        Assert.Equal(conversation, await provider.GetRequiredService<ISpecialistTurnStore>().ReadConversationAsync(before.Conversation.ConversationId, before.Identity, default));
        var journal = provider.GetRequiredService<SpecialistTurnJournal>();
        Assert.Equal(before.Handle, await journal.StageAsync(request, clock.GetUtcNow(), default));
        var unavailable = await Assert.ThrowsAsync<SpecialistTurnException>(() => journal.ResultAsync(before.Handle, request.Identity, before.ChildInvocationId!, default).AsTask());
        Assert.Equal("specialist.journal.content-unavailable", unavailable.Code);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(1, factory.Opens);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registered_recurring_retention_task_uses_actual_ordinary_scope_runner_and_pruner(bool durable)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var factory = new FixtureFactory("{\"Article\":\"retained\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock);
        var executable = await ExecutableAsync(provider);
        var (actor, _) = await StartAsync(provider, executable);
        var work = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), work);
        await DrainAsync(actor);
        var (before, _) = await NativeTurnAsync(provider, executable);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.AppliedKind))).DeliveredCount);
        clock.Advance(TimeSpan.FromDays(31));
        var pump = Assert.Single(provider.GetServices<Elsa.Tasks.Core.IRecurringTask>().OfType<SpecialistRetentionPumpTask>());
        await pump.ExecuteAsync(default);
        Assert.NotNull((await provider.GetRequiredService<ISpecialistTurnStore>().ReadAsync(before.Handle, before.Identity, default))!.ContentPurgedAt);
        Assert.Equal(1, factory.Calls);
    }

}
