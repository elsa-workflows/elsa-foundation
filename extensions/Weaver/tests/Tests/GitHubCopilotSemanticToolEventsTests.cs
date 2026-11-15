using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.GitHubCopilot.Options;
using Elsa.Weaver.GitHubCopilot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Weaver.Tests;

public sealed class GitHubCopilotSemanticToolEventsTests
{
    [Theory]
    [InlineData("prepare-denied")]
    [InlineData("execute-denied")]
    [InlineData("unsuccessful")]
    public async Task SDK_transport_success_cannot_hide_a_semantic_refusal(string mode)
    {
        var fixture = new Fixture(mode);
        var events = await fixture.Run();
        var invocation = Assert.Single(fixture.Invocations);
        var result = Assert.IsType<WeaverToolResult>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ContributedResult).Payload);
        Assert.False(result.Succeeded); Assert.Null(result.Payload); Assert.Null(result.Receipt);
        var completed = Assert.IsType<WeaverToolCallOutcome>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ToolCallCompleted).Payload);
        Assert.False(completed.Succeeded);
        var started = Assert.IsType<WeaverToolCallRequest>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ToolCallStarted).Payload);
        Assert.Equal(invocation.ToolCallId, started.ToolCallId);
        Assert.Equal(fixture.Tool.Descriptor.Risk, started.Risk);
        Assert.False(started.RequiresApproval);
        Assert.Equal(invocation.ToolCallId, completed.ToolCallId);
        Assert.Equal("The declared tool operation was refused.", completed.Summary);
        Assert.Equal(invocation.ToolCallId, result.ToolCallId);
        Assert.Equal(mode == "prepare-denied" ? 0 : 1, fixture.Tool.Executions);
        Assert.Equal(1, fixture.Session.TransportSuccesses);
        if (mode != "unsuccessful")
        {
            var sdkReturn = JsonSerializer.SerializeToElement(Assert.Single(fixture.Session.Returns), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal("denied", sdkReturn.GetProperty("status").GetString());
            Assert.False(sdkReturn.GetProperty("executed").GetBoolean());
            Assert.Equal("test.semantic_refused", sdkReturn.GetProperty("error").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Successful_read_has_one_semantic_completion_with_native_identity_and_no_invented_receipt()
    {
        var fixture = new Fixture("read"); var events = await fixture.Run();
        var invocation = Assert.Single(fixture.Invocations);
        var result = Assert.IsType<WeaverToolResult>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ContributedResult).Payload);
        var outcome = Assert.IsType<WeaverToolCallOutcome>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ToolCallCompleted).Payload);
        Assert.Equal(invocation.ToolCallId, outcome.ToolCallId); Assert.Equal(invocation.ToolCallId, result.ToolCallId);
        Assert.True(outcome.Succeeded); Assert.Equal("The declared read completed.", outcome.Summary);
        Assert.Same(result, outcome.Result); Assert.Null(result.Receipt);
        Assert.Equal(1, fixture.Tool.Executions); Assert.Equal(1, fixture.Session.TransportSuccesses);
    }

    [Fact]
    public async Task Pending_proposal_reports_declared_start_risk_and_final_prepared_risk_without_success()
    {
        var fixture = new Fixture("proposal"); var events = await fixture.Run();
        var started = Assert.IsType<WeaverToolCallRequest>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ToolCallStarted).Payload);
        Assert.Equal(WeaverRisk.ReviewRequired, started.Risk); Assert.True(started.RequiresApproval);
        var proposal = Assert.IsType<WeaverActionProposal>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ProposalCreated).Payload);
        Assert.Equal(WeaverRisk.Admin, proposal.PreparedInvocation!.Risk);
        Assert.Equal(WeaverActionProposalStatus.AwaitingApproval, proposal.Status);
        Assert.DoesNotContain(events, item => item.Kind is WeaverStreamEventKind.ToolCallCompleted or WeaverStreamEventKind.ContributedResult);
        Assert.Equal(0, fixture.Tool.Executions); Assert.Single(fixture.Invocations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_and_parallel_same_name_calls_keep_their_actual_independent_outcomes(bool parallel)
    {
        var fixture = new Fixture("mixed", count: 2, parallel: parallel); var events = await fixture.Run();
        var invocations = fixture.Invocations.ToArray(); Assert.Equal(2, invocations.Length);
        Assert.Equal(2, invocations.Select(item => item.ToolCallId).Distinct(StringComparer.Ordinal).Count());
        var started = events.Where(item => item.Kind == WeaverStreamEventKind.ToolCallStarted).Select(item => Assert.IsType<WeaverToolCallRequest>(item.Payload)).ToArray();
        var completed = events.Where(item => item.Kind == WeaverStreamEventKind.ToolCallCompleted).Select(item => Assert.IsType<WeaverToolCallOutcome>(item.Payload)).ToArray();
        var results = events.Where(item => item.Kind == WeaverStreamEventKind.ContributedResult).Select(item => Assert.IsType<WeaverToolResult>(item.Payload)).ToArray();
        Assert.Equal(2, started.Length); Assert.Equal(2, completed.Length); Assert.Equal(2, results.Length);
        foreach (var invocation in invocations)
        {
            Assert.Single(started, item => item.ToolCallId == invocation.ToolCallId);
            var outcome = Assert.Single(completed, item => item.ToolCallId == invocation.ToolCallId);
            Assert.Equal((int)invocation.Arguments["ordinal"]! == 1, outcome.Succeeded);
            Assert.Equal(outcome.Succeeded, Assert.Single(results, item => item.ToolCallId == invocation.ToolCallId).Succeeded);
        }
        Assert.Equal(2, fixture.Session.TransportSuccesses); Assert.Equal(1, fixture.Tool.Executions);
    }

    [Fact]
    public async Task Unrelated_builtin_tool_failure_and_SDK_error_are_preserved()
    {
        var fixture = new Fixture("read") { IncludeTools = false };
        fixture.Session.BuiltinFailure = true;
        var events = await fixture.Run();
        var started = Assert.IsType<WeaverToolCallRequest>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ToolCallStarted).Payload);
        Assert.Equal("sdk-builtin-call", started.ToolCallId);
        var completed = Assert.IsType<WeaverToolCallOutcome>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ToolCallCompleted).Payload);
        Assert.Equal("sdk-builtin-call", completed.ToolCallId); Assert.False(completed.Succeeded);
        Assert.Equal("Built-in SDK tool failed.", completed.Summary);
        Assert.Equal("test.sdk_failure", Assert.Single(events, item => item.Kind == WeaverStreamEventKind.Error).Error!.Code);
        Assert.Empty(fixture.Invocations); Assert.DoesNotContain(events, item => item.Kind == WeaverStreamEventKind.ContributedResult);
    }

    [Fact]
    public async Task Custom_function_transport_failure_is_not_suppressed_as_a_semantic_success_duplicate()
    {
        var fixture = new Fixture("read"); fixture.Session.CustomTransportFailure = true;
        var events = await fixture.Run();
        Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ContributedResult);
        var completed = events.Where(item => item.Kind == WeaverStreamEventKind.ToolCallCompleted).Select(item => Assert.IsType<WeaverToolCallOutcome>(item.Payload)).ToArray();
        Assert.Equal(2, completed.Length);
        Assert.True(Assert.Single(completed, item => item.ToolCallId == Assert.Single(fixture.Invocations).ToolCallId).Succeeded);
        Assert.False(Assert.Single(completed, item => item.ToolCallId == "sdk-custom-1").Succeeded);
    }

    private sealed class Fixture
    {
        internal readonly SemanticTool Tool;
        internal readonly Session Session;
        internal readonly ConcurrentQueue<WeaverToolInvocation> Invocations = new();
        internal bool IncludeTools = true;
        internal Fixture(string mode, int count = 1, bool parallel = false) { Tool = new(mode, parallel); Session = new(count, parallel); }
        internal async Task<List<WeaverStreamEvent>> Run()
        {
            var invoker = new WeaverTestToolInvoker(async (invocation, _) =>
            {
                Invocations.Enqueue(invocation);
                var preparation = await Tool.PrepareAsync(invocation);
                if (!preparation.Succeeded) return WeaverToolDispatch.Denied(preparation.Error!);
                var prepared = WeaverTestToolHelpers.Prepared(invocation, Tool.Descriptor, preparation.Value!);
                if (Tool.Mode == "proposal")
                {
                    var now = DateTimeOffset.UtcNow;
                    return WeaverToolDispatch.Proposed(new("test-proposal", invocation.SessionId, null, Tool.Descriptor.Name, "tool-invocation",
                        "Review the declared operation", "Review before application.", prepared.Risk, null, [], [], [], null, [], null, null, true,
                        WeaverActionProposalStatus.AwaitingApproval, null, null, now, now) { PreparedInvocation = prepared });
                }
                var result = await Tool.ExecuteAsync(prepared);
                return result.Succeeded ? WeaverToolDispatch.Executed(result.Value!) : WeaverToolDispatch.Denied(result.Error!);
            });
            var provider = new WeaverGitHubCopilotProvider(new ClientFactory(Session),
                Options.Create(new WeaverGitHubCopilotOptions { Enabled = true, GitHubToken = "fixture-only-token" }), invoker,
                NullLogger<WeaverGitHubCopilotProvider>.Instance);
            var request = new WeaverHarnessTurnRequest("test-session", "test-actor", [new(WeaverRole.User, "Perform the declared tool operation.")], [],
                IncludeTools ? [Tool] : [], WeaverPolicy.Default) { TenantId = "test-tenant", TurnId = "test-turn" };
            var events = new List<WeaverStreamEvent>();
            await foreach (var item in provider.RunTurnAsync(request)) events.Add(item);
            return events;
        }
    }

    private sealed class SemanticTool(string mode, bool parallel) : IWeaverTool
    {
        private readonly TaskCompletionSource bothPrepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int preparations;
        internal int Executions;
        internal string Mode => mode;
        public WeaverToolDescriptor Descriptor { get; } = new("test_semantic_tool", "Declared tool", "Performs its declared operation.",
            mode == "proposal" ? WeaverToolMutability.Mutating : WeaverToolMutability.ReadOnly,
            mode == "proposal" ? WeaverRisk.ReviewRequired : WeaverRisk.Admin, "{\"type\":\"object\"}", [], []) { ModuleId = "test.module" };
        public async Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            if (parallel)
            {
                if (Interlocked.Increment(ref preparations) == 2) bothPrepared.TrySetResult();
                await bothPrepared.Task.WaitAsync(cancellationToken);
            }
            if (mode == "prepare-denied" || mode == "mixed" && (int)invocation.Arguments["ordinal"]! == 2)
                return WeaverResult<WeaverToolPreparation>.Failure("test.semantic_refused", "The declared tool operation was refused.", 409);
            return WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor) with { Risk = WeaverRisk.Admin });
        }
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Executions);
            return Task.FromResult(mode == "execute-denied"
                ? WeaverResult<WeaverToolResult>.Failure("test.semantic_refused", "The declared tool operation was refused.", 409)
                : WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, invocation.ToolName, mode != "unsuccessful",
                    mode == "unsuccessful" ? "The declared tool operation was refused." : "The declared read completed.")));
        }
    }

    private sealed class ClientFactory(Session session) : IGitHubCopilotClientFactory
    { public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => new Client(session); }
    private sealed class Client(Session session) : IGitHubCopilotClient
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken) { session.Tools = request.CustomTools ?? []; return Task.FromResult<IGitHubCopilotSession>(session); }
        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken) => CreateSessionAsync(request, cancellationToken);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Session(int count, bool parallel) : IGitHubCopilotSession
    {
        internal IReadOnlyCollection<AIFunction> Tools = [];
        internal readonly ConcurrentQueue<object?> Returns = new();
        internal int TransportSuccesses;
        internal bool BuiltinFailure, CustomTransportFailure;
        public string SessionId => "test-session";
        public string? WorkspacePath => null;
        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new(GitHubCopilotStreamEventKind.Started);
            if (BuiltinFailure)
            {
                yield return new(GitHubCopilotStreamEventKind.ToolExecutionStarted, ToolCallId: "sdk-builtin-call", ToolName: "sdk_builtin_tool");
                yield return new(GitHubCopilotStreamEventKind.ToolExecutionCompleted, ToolCallId: "sdk-builtin-call", Success: false, ErrorMessage: "Built-in SDK tool failed.");
                yield return new(GitHubCopilotStreamEventKind.Error, ErrorCode: "test.sdk_failure", ErrorMessage: "SDK transport failed.");
            }
            else if (Tools.Count > 0)
            {
                var tool = Assert.Single(Tools);
                var pending = new List<Task<object?>>();
                for (var ordinal = 1; ordinal <= count; ordinal++)
                {
                    yield return new(GitHubCopilotStreamEventKind.ToolExecutionStarted, ToolCallId: "sdk-custom-" + ordinal, ToolName: tool.Name);
                    var task = tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["ordinal"] = ordinal }), cancellationToken).AsTask();
                    pending.Add(task);
                    if (!parallel) await task;
                }
                var results = await Task.WhenAll(pending);
                for (var index = 0; index < results.Length; index++)
                {
                    Returns.Enqueue(results[index]);
                    if (!CustomTransportFailure) Interlocked.Increment(ref TransportSuccesses);
                    yield return new(GitHubCopilotStreamEventKind.ToolExecutionCompleted, ToolCallId: "sdk-custom-" + (index + 1),
                        Success: !CustomTransportFailure, ErrorMessage: CustomTransportFailure ? "Custom SDK transport failed." : null);
                }
            }
            yield return new(GitHubCopilotStreamEventKind.Completed);
        }
        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
