using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Tests.Support;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

public sealed class CompensationHistoryTests
{
    [Fact]
    public async Task Actual_handler_retry_journals_owner_decisions_and_callbacks_without_repeating_fault_creation()
    {
        using var host = new CompensationHost(PersistenceMode.Durable, CompensationStore.InMemory());
        var participant = new Participant().Rejecting("charge").FailingTimes("release", 1);
        await using var runtime = await host.StartHostAsync(participant, History);
        await CompensationHost.StartAsync(runtime, Scope(new Step("private-step-name-sentinel", Participant("reserve"), Participant("release")), new Step("Charge", Participant("charge"))));
        await runtime.SweepUntilQuietAsync();
        var run = await runtime.ReadRunAsync(CompensationHost.WorkflowExecutionId);
        run.AssertWorkflowCompleted(); run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Empty(participant.Present);
        var reader = runtime.Services.GetRequiredService<IRuntimeHistoryStore>();
        var page = await reader.ReadPageAsync(new(CompensationHost.WorkflowExecutionId, limit: 500));
        Assert.Null(page.NextContinuationToken);
        Assert.Null(page.NextContinuationToken);
        var scopeId = run.State(ScopeNodeId).InvocationId;
        var journalPage = await reader.ReadPageAsync(new(CompensationHost.WorkflowExecutionId, activityExecutionId: scopeId, limit: 500));
        Assert.Null(journalPage.NextContinuationToken);
        var journal = journalPage.Items;
        Assert.Single(journal, e => e.Name == "CompensationRegistered");
        Assert.Single(journal, e => e.Name == "CompensationStarted");
        Assert.Equal(2, journal.Count(e => e.Name == "CompensationHandlerScheduled"));
        Assert.Single(journal, e => e.Name == "CompensationHandlerFaulted");
        Assert.Single(journal, e => e.Name == "CompensationHandlerCompleted");
        Assert.Single(journal, e => e.Name == "CompensationCompleted");
        Assert.Equal(2, page.Items.Count(e => e.Name == "IncidentRecorded"));
        Assert.Equal(2, page.Items.Count(e => e.Name == "IncidentResolved"));
        foreach (var callback in journal.Where(e => e.Name == "ActivityContinuationStarted"))
        {
            var returned = Assert.Single(journal, e => e.CheckpointId == callback.CheckpointId && e.Name == "ActivityContinuationReturned");
            Assert.True(callback.Sequence < returned.Sequence);
            Assert.Equal(callback.AttemptId, returned.AttemptId);
            Assert.All(journal.Where(e => e.CheckpointId == callback.CheckpointId && e.Name.StartsWith("Compensation", StringComparison.Ordinal)),
                e => Assert.True(returned.Sequence < e.Sequence));
        }
        Assert.NotEmpty(journal.Where(e => e.Name == "ActivityContinuationStarted"));
        Assert.All(page.Items, e => Assert.Null(e.Payload));
        Assert.DoesNotContain("private-step-name-sentinel", JsonSerializer.Serialize(journal.Where(e => e.Name.StartsWith("Compensation", StringComparison.Ordinal))), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Actual_no_handler_absorption_is_resolved_and_compensated_without_a_handler_execution_claim()
    {
        using var host = new CompensationHost(PersistenceMode.Balanced, CompensationStore.InMemory());
        await using var runtime = await host.StartHostAsync(new Participant().Rejecting("reject"), History);
        await CompensationHost.StartAsync(runtime, Scope(new Step("Rejected", Participant("reject"))));
        await runtime.SweepUntilQuietAsync();
        var run = await runtime.ReadRunAsync(CompensationHost.WorkflowExecutionId);
        run.AssertWorkflowCompleted(); run.AssertOutcomes(ScopeNodeId, CompensationScope.CompensatedOutcome);
        Assert.Empty(await runtime.Services.GetRequiredService<IIncidentStateStore>().ListBlockingAsync(CompensationHost.WorkflowExecutionId));
        var page = await runtime.Services.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new(CompensationHost.WorkflowExecutionId, limit: 500));
        Assert.Null(page.NextContinuationToken);
        Assert.Single(page.Items, e => e.Name == "IncidentRecorded");
        Assert.Single(page.Items, e => e.Name == "IncidentResolved");
        Assert.Single(page.Items, e => e.Name == "CompensationStarted");
        Assert.Single(page.Items, e => e.Name == "CompensationCompleted");
        Assert.DoesNotContain(page.Items, e => e.Name.StartsWith("CompensationHandler", StringComparison.Ordinal));
    }

    private static void History(IServiceCollection services)
    {
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Access());
        services.AddRuntimeHistory();
        services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "actual-compensation-history-signing-key-at-least32-bytes");
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(CompensationHost.DefaultPartition.Value)); }
}
