using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class StartClockRollbackTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable, false)]
    [InlineData(PersistenceMode.Balanced, false)]
    [InlineData(PersistenceMode.Ephemeral, false)]
    [InlineData(PersistenceMode.Durable, true)]
    [InlineData(PersistenceMode.Balanced, true)]
    [InlineData(PersistenceMode.Ephemeral, true)]
    public async Task An_immediate_start_continuation_does_not_wait_for_the_wall_clock_to_catch_up(PersistenceMode mode, bool sqlite)
    {
        var clock = new RollbackClock();
        var calls = new Calls();
        void Compose(IServiceCollection services)
        {
            ReferenceWorkflowRuns.ComposeHost(services);
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.AddSingleton(calls);
            services.AddSingleton<IRuntimeCheckpointCommitEnricher>(new RewindAtStart(clock));
            if (!sqlite) services.AddPersistenceModes(options => options.Mode = mode);
        }
        await using var fixture = sqlite
            ? await HttpEndpointHostFixture.StartDurableSqliteAsync(mode, 50, Compose)
            : await HttpEndpointHostFixture.StartAsync(Compose);
        var executable = ReferenceWorkflows.Executable("clock-start", ReferenceWorkflows.Clr("leaf", typeof(Leaf), []));
        await fixture.PublishAsync(executable);
        await fixture.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new(executable.Identity.ArtifactId, "test", workflowExecutionId: "clock-start"));

        Assert.True(clock.Rewound);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowExecutionAsync("clock-start")).Status);
        Assert.Equal(1, calls.Count);
        await using var scope = fixture.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync("clock-start"));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(
            new(clock.Now.AddDays(1), 100, "clock-start")));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync("clock-start"));
    }

    public sealed class Calls { public int Count; }

    [ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
    public sealed class Leaf(Calls calls) : Activity<ActivityUnit>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            calls.Count++;
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
        }
    }

    private sealed class RollbackClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        public bool Rewound { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RewindAtStart(RollbackClock clock) : IRuntimeCheckpointCommitEnricher
    {
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(commit);

        public ValueTask ValidateFinalAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            if (commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted)
            {
                clock.Now = commit.Checkpoint.OccurredAt.AddTicks(-1);
                clock.Rewound = true;
            }
            return ValueTask.CompletedTask;
        }
    }
}
