using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed partial class SpecialistNativeTests
{
    [Theory]
    [InlineData(false, "success")]
    [InlineData(true, "success")]
    [InlineData(false, "missing-terminal")]
    [InlineData(true, "missing-terminal")]
    [InlineData(false, "refusal")]
    [InlineData(true, "refusal")]
    public async Task Native_streaming_keeps_the_complete_JSON_prefix_unaccepted_until_terminal_validation(bool durable, string mode)
    {
        using var files = new JournalFiles();
        var terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FixtureFactory("{\"Article\":\"streamed result\"}") { StreamMode = mode, StreamTerminalGate = terminal };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, streamResponses: true);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        var work = provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        try
        {
            await factory.StreamPrefixSent.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var prefix = (await NativeTurnAsync(provider, executable)).Document;
            Assert.Equal(SpecialistTurnStatus.Dispatching, prefix.Status);
            Assert.Null(prefix.ProtectedResult);
            Assert.Equal(0, (await provider.GetRequiredService<Elsa.Agent.Specialists.Core.Contracts.ISpecialistTurnStore>()
                .ReadConversationAsync(prefix.Handle, prefix.Identity, CancellationToken.None))!.Revision);
            Assert.NotEqual(WorkflowExecutionStatus.Completed, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        }
        finally { terminal.TrySetResult(); }
        await work;
        await DrainAsync(actor);
        var turn = (await NativeTurnAsync(provider, executable)).Document;
        Assert.Equal(mode == "success" ? SpecialistTurnStatus.Accepted : mode == "refusal" ? SpecialistTurnStatus.Failed : SpecialistTurnStatus.OutcomeUnknown, turn.Status);
        Assert.Equal(mode == "success" ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Faulted,
            (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(1, factory.StreamingCalls);
        Assert.Equal(0, factory.AtomicCalls);
        Assert.Single(turn.ModelRequests!);
        var conversation = (await provider.GetRequiredService<Elsa.Agent.Specialists.Core.Contracts.ISpecialistTurnStore>()
            .ReadConversationAsync(turn.Handle, turn.Identity, CancellationToken.None))!;
        Assert.Equal(mode == "missing-terminal", conversation.OutcomeUnknown);
        Assert.Equal(mode == "success" ? 1 : 0, conversation.Revision);
        if (mode == "success") Assert.NotNull(turn.ProtectedResult);
        else Assert.Null(turn.ProtectedResult);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_streaming_selection_refuses_an_unqualified_lease_before_dispatch(bool durable)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"must not dispatch\"}") { SupportsStreaming = false };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, streamResponses: true);
        var executable = await ExecutableAsync(provider);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        var turn = (await NativeTurnAsync(provider, executable)).Document;
        Assert.Empty(turn.ModelRequests ?? []);
        Assert.Equal(0, factory.Calls);
        Assert.Equal(0, factory.StreamingCalls);
        Assert.Equal(0, factory.AtomicCalls);
        var child = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Single(s => s.ParentActivityExecutionId is not null);
        Assert.Equal("specialist.streaming.unavailable", child.Fault!.Code);
    }
}
