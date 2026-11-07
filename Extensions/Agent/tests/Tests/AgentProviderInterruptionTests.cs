using System.Runtime.CompilerServices;
using Elsa.Agent.Anthropic.Options;
using Elsa.Agent.Anthropic.Services;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.GitHubCopilot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

/// <summary>
/// #3441 T015 (FR-021): each provider's behaviour after an interrupted turn is explicit. Interrupted, withdrawn or
/// stopped prompts never reach a provider as accepted conversation, and a Copilot conversation that cannot be resumed
/// is an explicit, recorded outcome.
/// </summary>
public sealed class AgentProviderInterruptionTests
{
    private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);

    // ---- Host-driven loop (the Anthropic path) ----

    [Fact]
    public async Task A_stopped_prompt_and_its_partial_answer_are_left_out_of_the_next_provider_history()
    {
        using var harness = new AgentRestartHarness();
        var host = harness.Host;
        var session = await host.CreateAsync();
        await host.PostAsync(session, "first question");
        var held = harness.Provider.HoldNext();
        using var stop = new CancellationTokenSource();
        var stopped = Task.Run(() => host.StreamAsync(session, stop.Token));
        await held.Entered.Task.WaitAsync(HandoffTimeout);
        stop.Cancel();
        Assert.Equal(AgentStreamEventKind.TurnCancelled, (await stopped.WaitAsync(HandoffTimeout))[^1].Kind);
        var transcript = await host.Sessions.ListMessagesAsync(session.Id);
        Assert.Contains(transcript, x => x is { Role: AgentRole.Assistant, Status: AgentMessageStatus.Failed, Content: "Partial " });

        await host.PostAsync(session, "second question");
        Assert.Equal(AgentStreamEventKind.Completed, (await host.StreamAsync(session))[^1].Kind);

        var history = harness.Provider.Contexts[^1].History;
        Assert.DoesNotContain(history, x => x.Content == "first question");
        Assert.DoesNotContain(history, x => x.Content.Contains("Partial", StringComparison.Ordinal));
        Assert.Equal("second question", Assert.Single(history, x => x.Role == AgentRole.User).Content);
    }

    [Fact]
    public async Task An_interrupted_prompt_never_reaches_the_anthropic_request_after_a_restart()
    {
        var client = new InterruptibleChatClient();
        var provider = new AnthropicAgentProvider(new ChatClientFactory(client),
            Options.Create(new AnthropicAgentOptions { Enabled = true, ApiKey = "sk-test", Model = "claude-test" }), NullLogger<AnthropicAgentProvider>.Instance);
        using var harness = new AgentRestartHarness(configure: services => services.Replace(ServiceDescriptor.Singleton<IAgentProvider>(provider)));
        var host = harness.Host;
        var session = await host.CreateAsync();
        await host.PostAsync(session, "first question");
        client.HoldFirst = true;
        var running = Task.Run(() => host.StreamAsync(session));
        await client.Entered.Task.WaitAsync(HandoffTimeout);

        var restarted = harness.Restart();
        await restarted.PostAsync(session, "second question");
        Assert.Equal(AgentStreamEventKind.Completed, (await restarted.StreamAsync(session))[^1].Kind);

        var sent = string.Join("\n", client.Captured!.Select(x => x.Text));
        Assert.DoesNotContain("first question", sent, StringComparison.Ordinal);
        Assert.Contains("second question", sent, StringComparison.Ordinal);
        client.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => running.WaitAsync(HandoffTimeout));
    }

    // ---- Delegated loop (the Copilot path) ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_copilot_resume_fallback_after_earlier_turns_is_an_explicit_conversation_reset(bool earlierTurns)
    {
        var client = new FallbackCopilotClient();
        var provider = new GitHubCopilotAgentProvider(new CopilotClientFactory(client),
            Options.Create(new GitHubCopilotAgentOptions { Enabled = true, GitHubToken = "test-token" }), new AgentTestToolInvoker(),
            NullLogger<GitHubCopilotAgentProvider>.Instance);
        IReadOnlyList<AgentTurnMessage> history = earlierTurns
            ? [new(AgentRole.User, "earlier"), new(AgentRole.Assistant, "earlier answer"), new(AgentRole.User, "now")]
            : [new(AgentRole.User, "now")];

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(new AgentHarnessTurnRequest("session-1", "actor-1", history, [], [], AgentPolicy.Default) { TenantId = "tenant-1" }))
            events.Add(item);

        Assert.True(client.Created);
        var notices = events.Where(x => x.Payload is AgentProviderNotice).ToList();
        if (!earlierTurns)
        {
            // A first turn has no earlier conversation to lose; creating it is not a reset.
            Assert.Empty(notices);
            return;
        }
        var reset = Assert.Single(notices);
        Assert.Equal(AgentStreamEventKind.Progress, reset.Kind);
        Assert.Equal(GitHubCopilotAgentProvider.ConversationResetCode, ((AgentProviderNotice)reset.Payload!).Code);
        Assert.False(string.IsNullOrWhiteSpace(reset.Content));
    }

    // #3441 Phase 3 review: only a completed earlier exchange makes a fallback a reset.
    [Fact]
    public async Task A_copilot_fallback_after_only_a_failed_first_prompt_is_not_a_conversation_reset()
    {
        var client = new FallbackCopilotClient();
        var provider = new GitHubCopilotAgentProvider(new CopilotClientFactory(client),
            Options.Create(new GitHubCopilotAgentOptions { Enabled = true, GitHubToken = "test-token" }), new AgentTestToolInvoker(),
            NullLogger<GitHubCopilotAgentProvider>.Instance);
        // The first prompt failed before any Copilot conversation existed; it has no answer.
        IReadOnlyList<AgentTurnMessage> history = [new(AgentRole.User, "failed first"), new(AgentRole.User, "now")];

        var events = new List<AgentStreamEvent>();
        await foreach (var item in provider.RunTurnAsync(new AgentHarnessTurnRequest("session-1", "actor-1", history, [], [], AgentPolicy.Default) { TenantId = "tenant-1" }))
            events.Add(item);

        Assert.True(client.Created);
        Assert.DoesNotContain(events, x => x.Payload is AgentProviderNotice);
    }

    [Fact]
    public async Task The_orchestrator_records_a_provider_notice_on_the_turn()
    {
        using var harness = new AgentRestartHarness(configure: services => services.Replace(ServiceDescriptor.Singleton<IAgentProvider>(new NoticeProvider())));
        var host = harness.Host;
        var session = await host.CreateAsync();
        await host.PostAsync(session, "hello");

        var events = await host.StreamAsync(session);

        var shown = Assert.Single(events, x => x.Payload is AgentProviderNotice);
        Assert.Equal(AgentStreamEventKind.Progress, shown.Kind);
        var recorded = Assert.Single(await host.AuditAsync(session), x => x.Kind == AgentAuditEventKind.ProviderDiagnostic);
        Assert.Equal(GitHubCopilotAgentProvider.ConversationResetCode, recorded.Metadata["code"]);
        Assert.Equal(Assert.IsType<AgentTurnStarted>(Assert.Single(events, x => x.Kind == AgentStreamEventKind.Started).Payload).TurnId, recorded.Metadata["turnId"]);
    }

    private sealed class ChatClientFactory(IChatClient client) : IAnthropicChatClientFactory
    {
        public IChatClient Create(string apiKey, string model) => client;
    }

    /// <summary>The first call streams partial text and then waits until it is released; later calls capture what they were sent.</summary>
    private sealed class InterruptibleChatClient : IChatClient
    {
        public bool HoldFirst { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<ChatMessage>? Captured { get; private set; }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (HoldFirst)
            {
                HoldFirst = false;
                yield return new() { Contents = [new TextContent("Partial ")] };
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            else
                Captured = messages.ToList();
            await Task.Yield();
            yield return new() { Contents = [new TextContent("answer")] };
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CopilotClientFactory(IGitHubCopilotClient client) : IGitHubCopilotClientFactory
    {
        public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => client;
    }

    /// <summary>A Copilot client whose resume always fails, as when the SDK no longer holds the conversation.</summary>
    private sealed class FallbackCopilotClient : IGitHubCopilotClient
    {
        public bool Created { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]);
        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
            => Task.FromException<IGitHubCopilotSession>(new InvalidOperationException("The session is unknown to the runtime."));
        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            Created = true;
            return Task.FromResult<IGitHubCopilotSession>(new AnsweringSession());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class AnsweringSession : IGitHubCopilotSession
    {
        public string SessionId => "session-1";
        public string? WorkspacePath => null;
        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new(GitHubCopilotStreamEventKind.MessageDelta, "answer");
            yield return new(GitHubCopilotStreamEventKind.Completed);
        }
        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>A host-step provider that reports one explicit provider notice before answering.</summary>
    private sealed class NoticeProvider : IAgentProvider
    {
        private readonly DeterministicAgentProvider _inner = new();
        public string ProviderId => "notice-test";
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default) => _inner.CreateSessionAsync(session, cancellationToken);

        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            const string label = "The provider conversation was replaced.";
            yield return new(Core.Services.AgentProviderPrimitives.NewId(), AgentStreamEventKind.Progress, label, null, null, DateTimeOffset.UtcNow, null,
                new AgentProviderNotice(GitHubCopilotAgentProvider.ConversationResetCode, label));
            await foreach (var item in _inner.ContinueTurnAsync(context, cancellationToken))
                yield return item;
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => _inner.ApproveToolAsync(request, cancellationToken);
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => _inner.GetDiagnosticsAsync(cancellationToken);
    }
}
