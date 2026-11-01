using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed partial class SpecialistNativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disabled_new_operations_produce_a_coded_native_refusal_without_a_model_child_or_journal_row(bool durable)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"unused\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, allowNewOperations: false);
        var executable = await ExecutableAsync(provider);
        var (actor, _) = await StartAsync(provider, executable);
        await DrainAsync(actor);
        var state = Assert.Single(await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run"));
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("specialist.turn.admission-disabled", state.Fault!.Code);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(0, factory.Calls);
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        if (durable)
        {
            var context = provider.GetRequiredService<SpecialistsDbContext>();
            Assert.Equal(0, await context.Set<SpecialistTurnRecord>().CountAsync());
            Assert.Equal(0, await context.Set<SpecialistOperationRecord>().CountAsync());
        }
    }
}
