using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TestRunHeldHttpTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonElement Input = JsonSerializer.SerializeToElement(new { value = "same" });
    private static readonly WorkflowTestRunHttpReply Answer = new(201, "text/plain", new string('x', 25),
        new Dictionary<string, string> { ["X"] = "Y" });

    [Fact]
    public async Task Concurrent_matching_calls_share_the_first_answer_and_keep_independent_cancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new Fixture(async (request, _, token) =>
        {
            entered.SetResult();
            await finish.Task.WaitAsync(token);
            return Result(request, WorkflowExecutionCommandDispatchStatus.Accepted, Answer);
        });
        await fixture.PrepareAsync("held");
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = fixture.Dispatcher.DispatchAsync("held", Input, testTimeout.Token).AsTask();
        await entered.Task.WaitAsync(testTimeout.Token);
        var retry = fixture.Dispatcher.DispatchAsync("held", Input, testTimeout.Token).AsTask();
        using var waitingCaller = new CancellationTokenSource();
        var interrupted = fixture.Dispatcher.DispatchAsync("held", Input, waitingCaller.Token).AsTask();

        try
        {
            var conflict = fixture.Dispatcher.DispatchAsync("held", JsonSerializer.SerializeToElement(new { value = "different" }),
                testTimeout.Token).AsTask();
            await Assert.ThrowsAsync<InvalidOperationException>(() => conflict.WaitAsync(testTimeout.Token));
            Assert.False(retry.IsCompleted);
            waitingCaller.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interrupted);
            Assert.Equal(1, fixture.Kernel.Calls);
        }
        finally { finish.TrySetResult(); }

        AssertReply(await first, alreadyDispatched: false);
        AssertReply(await retry, alreadyDispatched: true);
        Assert.Equal(1, fixture.Kernel.Calls);
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Rejected)]
    [InlineData(WorkflowExecutionCommandDispatchStatus.Duplicate)]
    public async Task A_provisional_or_metadata_only_outcome_cannot_seal_an_authored_reply(WorkflowExecutionCommandDispatchStatus status)
    {
        var fixture = new Fixture((request, count, _) => ValueTask.FromResult(count == 1
            ? Result(request, status, status == WorkflowExecutionCommandDispatchStatus.Rejected ? Answer with { StatusCode = 503 } : null)
            : Result(request, WorkflowExecutionCommandDispatchStatus.Accepted, Answer)));
        await fixture.PrepareAsync("held");

        var provisional = await fixture.Dispatcher.DispatchAsync("held", Input);
        Assert.Equal(status, provisional.Dispatch.CommandDispatch.Status);
        Assert.Null((await fixture.Targets.FindAsync("held"))!.DispatchResult);
        AssertReply(await fixture.Dispatcher.DispatchAsync("held", Input), alreadyDispatched: false);
        AssertReply(await fixture.Dispatcher.DispatchAsync("held", Input), alreadyDispatched: true);
        Assert.Equal(2, fixture.Kernel.Calls);
    }

    [Fact]
    public async Task Reply_budget_keeps_first_delivery_and_reclaims_expired_and_cleaned_entries()
    {
        var fixture = new Fixture((request, _, _) => ValueTask.FromResult(Result(request, WorkflowExecutionCommandDispatchStatus.Accepted,
            request.TestScope!.ScopeId == "oversize" ? Answer with { Body = new string('x', 100) } : Answer)), replyLimit: 80);
        await fixture.PrepareAsync("first");
        await fixture.PrepareAsync("overflow");
        AssertReply(await fixture.Dispatcher.DispatchAsync("first", Input), alreadyDispatched: false);
        AssertReply(await fixture.Dispatcher.DispatchAsync("overflow", Input), alreadyDispatched: false);
        AssertMetadataOnly(await fixture.Dispatcher.DispatchAsync("overflow", Input), "overflow");
        AssertReply(await fixture.Dispatcher.DispatchAsync("first", Input), alreadyDispatched: true);

        await fixture.Targets.MarkStoppedAsync("first");
        await fixture.PrepareAsync("expiring", Now.AddSeconds(1));
        AssertReply(await fixture.Dispatcher.DispatchAsync("expiring", Input), alreadyDispatched: false);
        AssertReply(await fixture.Dispatcher.DispatchAsync("expiring", Input), alreadyDispatched: true);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));

        await fixture.PrepareAsync("next");
        AssertReply(await fixture.Dispatcher.DispatchAsync("next", Input), alreadyDispatched: false);
        AssertReply(await fixture.Dispatcher.DispatchAsync("next", Input), alreadyDispatched: true);
        Assert.Null((await fixture.Targets.FindAsync("expiring"))!.DispatchResult!.Dispatch.TestRunHttpReply);
        await fixture.Targets.RemoveAsync("next");

        await fixture.PrepareAsync("oversize");
        Assert.Equal(100, (await fixture.Dispatcher.DispatchAsync("oversize", Input)).Dispatch.TestRunHttpReply!.Body!.Length);
        AssertMetadataOnly(await fixture.Dispatcher.DispatchAsync("oversize", Input), "oversize");
        await fixture.PrepareAsync("replacement");
        await fixture.Dispatcher.DispatchAsync("replacement", Input);
        AssertReply(await fixture.Dispatcher.DispatchAsync("replacement", Input), alreadyDispatched: true);
    }

    private static void AssertReply(TestRunTargetDispatchResult result, bool alreadyDispatched)
    {
        Assert.Equal(alreadyDispatched, result.AlreadyDispatched);
        var reply = result.Dispatch.TestRunHttpReply;
        Assert.NotNull(reply);
        Assert.Equal(Answer.StatusCode, reply.StatusCode);
        Assert.Equal(Answer.ContentType, reply.ContentType);
        Assert.Equal(Answer.Body, reply.Body);
        Assert.Equal("Y", reply.Headers["X"]);
    }

    private static void AssertMetadataOnly(TestRunTargetDispatchResult result, string scopeId)
    {
        Assert.True(result.AlreadyDispatched);
        Assert.Equal($"actor-{scopeId}", result.Dispatch.WorkflowExecutionId);
        Assert.Null(result.Dispatch.TestRunHttpReply);
    }

    private static WorkflowExecutionStartDispatchResult Result(WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutionCommandDispatchStatus status, WorkflowTestRunHttpReply? reply)
    {
        var scopeId = request.TestScope!.ScopeId;
        var actor = $"actor-{scopeId}";
        var executable = new WorkflowExecutableIdentity(request.ArtifactId, "definition", "draft:snapshot", "draft", "sha256:artifact");
        return new(actor, executable, new(scopeId, actor, status, Now,
                status == WorkflowExecutionCommandDispatchStatus.Rejected ? "TargetMismatch" : null),
            new(actor, actor, "actor-foundation", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.None, Now))
            { TestRunHttpReply = reply };
    }

    private sealed class Fixture
    {
        private readonly InMemoryWorkflowTestScopeStore _scopes = new();

        internal Fixture(Func<WorkflowExecutionStartDispatchRequest, int, CancellationToken, ValueTask<WorkflowExecutionStartDispatchResult>> run,
            int replyLimit = 1024)
        {
            Targets = new MemoryTestRunTargetStore(Clock, replyLimit);
            Kernel = new FakeDispatcher(run);
            Dispatcher = new TestRunTargetDispatcher(Targets, _scopes, Kernel, Clock, new TestRunHeldDispatches(), kernel: Kernel);
        }

        internal FakeTimeProvider Clock { get; } = new(Now);
        internal MemoryTestRunTargetStore Targets { get; }
        internal FakeDispatcher Kernel { get; }
        internal TestRunTargetDispatcher Dispatcher { get; }

        internal async Task PrepareAsync(string id, DateTimeOffset? expiresAt = null)
        {
            var scope = new WorkflowTestScope(id, expiresAt ?? Now.AddMinutes(30), null, new(WorkflowExecutionPartition.DefaultValue));
            var executable = new WorkflowExecutableIdentity("artifact", "definition", "draft:snapshot", "draft", "sha256:artifact");
            var binding = new WorkflowTriggerBinding("binding", executable.ArtifactId, executable.DefinitionId, executable.ArtifactVersion,
                executable.ArtifactHash, "http", "Http", "sha256:http", null,
                new Dictionary<string, string> { ["http:responseMode"] = "Sync" }, Now);
            await _scopes.CreateAsync(scope, Now);
            await Targets.SaveAsync(new TestRunTarget(id, "Elsa.HttpEndpoint", "Elsa.HttpEndpoint", "http-request", executable, id,
                binding, scope, "test", $"legacy-{id}", $"start-{id}", new Dictionary<string, JsonElement>(),
                new Dictionary<string, object?>(), driverState: null, createdAt: Now));
        }
    }

    private sealed class FakeDispatcher(Func<WorkflowExecutionStartDispatchRequest, int, CancellationToken, ValueTask<WorkflowExecutionStartDispatchResult>> run)
        : IWorkflowDirectTestRunDispatcher
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public bool SupportsTrigger(string driverId) => driverId == "Elsa.HttpEndpoint";
        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope, WorkflowExecutionCommandDispatchOptions? dispatchOptions,
            CancellationToken cancellationToken) => run(request, Interlocked.Increment(ref _calls), cancellationToken);

        public ValueTask<WorkflowDirectTestRunObservation?> ObserveAsync(WorkflowTestScopeRecord scope,
            WorkflowExecutableSourceReference source, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask RequestStopAsync(WorkflowTestScope scope, string workflowExecutionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public ValueTask<bool> IsStoppedAsync(WorkflowTestScope scope, string workflowExecutionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
