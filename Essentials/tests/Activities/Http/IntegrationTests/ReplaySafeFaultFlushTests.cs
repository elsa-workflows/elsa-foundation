using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Api.Coalescing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Fault is a ReplaySafe leaf whose body always faults. Under the folding modes its attempt claim is not flushed
/// before the body runs; it waits in the working set, and what makes that safe is the mandatory IncidentRecorded
/// boundary, which commits the incident together with the claim. That holds under Ephemeral even when the host keeps no
/// record of runs that complete without one: a fault is always recorded. This runs the real Fault on the durable SQLite
/// runtime and reads back what was committed.
/// </summary>
public sealed class ReplaySafeFaultFlushTests
{
    private const string FaultCode = "replay-safe.fault";
    private const string FaultMessage = "The replay-safe fault ran.";

    /// <summary>Verifies that each folding mode durably commits the fault incident and deferred leaf claims at the incident boundary.</summary>
    [Theory]
    [InlineData(PersistenceMode.Balanced, true)]
    [InlineData(PersistenceMode.Ephemeral, true)]
    [InlineData(PersistenceMode.Ephemeral, false)]
    public async Task A_ReplaySafe_Fault_commits_its_incident_together_with_the_deferred_claims(PersistenceMode mode, bool recordCompletion)
    {
        var fault = ReferenceWorkflows.Clr("fault", typeof(Fault), [("code", FaultCode), ("message", FaultMessage)]);
        Assert.Equal(SideEffectProfile.ReplaySafe, fault.ActivityContract!.SideEffectProfile);
        var executable = ReferenceWorkflows.Executable(
            "replay-safe-fault",
            ReferenceWorkflows.Sequence("sequence", ReferenceWorkflows.Clr("write", typeof(WriteLine), [("text", "before the fault")]), fault));

        await using var fixture = await HttpEndpointHostFixture.StartDurableSqliteAsync(mode, maxActivitiesPerCommit: 50, recordCompletion: recordCompletion);
        await fixture.PublishAsync(executable);
        var executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId);

        Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.WorkflowExecutionAsync(executionId)).Status);

        await using var scope = fixture.Services.CreateAsyncScope();
        var incident = Assert.Single(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(executionId));
        Assert.Equal("fault", incident.ExecutableNodeId);
        Assert.Equal("ActivityReturnedFault", incident.FailureType);
        Assert.Equal(FaultMessage, incident.Message);

        var states = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId);
        var written = Assert.Single(states, state => state.Execution.ExecutableNodeId == "write");
        var faulted = Assert.Single(states, state => state.Execution.ExecutableNodeId == "fault");
        Assert.Equal(ActivityExecutionStatus.Completed, written.Status);
        Assert.Equal(ActivityExecutionStatus.Faulted, faulted.Status);
        Assert.Equal(FaultCode, faulted.Fault!.Code);
        AssertDurableClaimedAttempt(written, ActivityTransitionKind.Complete, incidentId: null);
        AssertDurableClaimedAttempt(faulted, ActivityTransitionKind.Fault, incident.IncidentId);

        var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>();
        Assert.Empty(await context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());

        // Neither leaf's claim reached the database as a commit of its own, yet both are durable above, so each was
        // folded into a later commit. The incident did get its own commit: IncidentRecorded flushed at its boundary.
        var markers = await context.RuntimeCheckpointCommits.AsNoTracking()
            .Select(marker => new { marker.WorkflowExecutionId, marker.CommitId })
            .ToArrayAsync();
        var commitIds = markers
            .Where(marker => EfRelationalIdentity.Decode(marker.WorkflowExecutionId) == executionId)
            .Select(marker => EfRelationalIdentity.Decode(marker.CommitId))
            .ToArray();
        foreach (var leaf in new[] { written, faulted })
            Assert.DoesNotContain(commitIds, id => id.Contains($":activity-attempt-claimed:{leaf.Execution.ActivityExecutionId}:", StringComparison.Ordinal));
        Assert.Contains(commitIds, id => id.EndsWith($":incident-recorded:{incident.IncidentId}", StringComparison.Ordinal));
    }

    /// <summary>Asserts that the persisted state contains one ended attempt with the expected transition, incident, and activation claim.</summary>
    private static void AssertDurableClaimedAttempt(ActivityExecutionState state, ActivityTransitionKind transition, string? incidentId)
    {
        var attempt = Assert.Single(state.Attempts!);
        Assert.NotNull(attempt.EndedAt);
        Assert.Equal(transition, attempt.TransitionKind);
        Assert.Equal(incidentId, attempt.IncidentId);
        Assert.Equal(attempt.AttemptId, state.Metadata[RuntimeMetadataKeys.ActivityAttemptActivationClaim]);
    }
}
