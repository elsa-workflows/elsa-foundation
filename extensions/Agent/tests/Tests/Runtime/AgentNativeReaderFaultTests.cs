using System.Text.Json;
using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Journal;
using Elsa.Agent.Persistence.EntityFrameworkCore;
using Elsa.Agent.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Runtime;

public sealed partial class AgentNativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_retained_request_key_preserves_the_agent_fault_code_without_claiming_or_opening_a_model(bool durable)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"unused\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null);
        var executable = await ExecutableAsync(provider);
        var (actor, _) = await StartAsync(provider, executable);
        var (before, request) = await NativeTurnAsync(provider, executable);
        Assert.StartsWith("v2:legacy:", before.ProtectedRequest!);
        var unavailable = before with { ProtectedRequest = before.ProtectedRequest!.Replace("v2:legacy:", "v2:retired:", StringComparison.Ordinal) };
        if (durable)
        {
            var context = provider.GetRequiredService<AgentDbContext>();
            var row = await context.Set<AgentTurnRecord>().SingleAsync(value => value.Handle == before.Handle);
            row.Payload = JsonSerializer.Serialize(unavailable, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await context.SaveChangesAsync();
        }
        else provider.GetRequiredService<InMemoryAgentTurnState>().Rows[before.Handle] = unavailable;
        var work = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), work);
        var receipt = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().FindAsync(work.WorkflowExecutionId, work.WorkId))!;
        Assert.Equal("agent.journal.protection", receipt.Result!.Fault!.Code);
        await DrainAsync(actor);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        var retained = (await provider.GetRequiredService<IAgentTurnStore>().ReadAsync(before.Handle, request.Identity, default))!;
        Assert.Equal(AgentTurnStatus.Prepared, retained.Status);
        Assert.Equal(0, retained.AdmissionGeneration);
        Assert.Null(retained.ModelRequests);
        Assert.Equal(unavailable.ProtectedRequest, retained.ProtectedRequest);
        Assert.Equal(0, factory.Calls);
        Assert.Equal(0, factory.Opens);
    }
}
