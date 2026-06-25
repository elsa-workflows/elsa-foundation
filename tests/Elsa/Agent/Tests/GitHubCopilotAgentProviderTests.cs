using System.Runtime.CompilerServices;
using Elsa.Agent.Core.Models;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.GitHubCopilot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

public sealed class GitHubCopilotAgentProviderTests
{
    private readonly FakeCopilotClientFactory _factory = new();
    private readonly GitHubCopilotAgentOptions _options = new();
    private readonly GitHubCopilotAgentProvider _provider;

    public GitHubCopilotAgentProviderTests()
    {
        _provider = new(_factory, Options.Create(_options), NullLogger<GitHubCopilotAgentProvider>.Instance);
    }

    [Fact]
    public async Task Diagnostics_reports_disabled_by_default()
    {
        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.False(diagnostics.IsAvailable);
        Assert.Equal("disabled", diagnostics.Metadata["statusCode"]);
        Assert.Equal("none", diagnostics.Metadata["authMode"]);
        Assert.Contains(AgentProviderOperation.Chat, diagnostics.SupportedOperations);
        Assert.DoesNotContain(AgentProviderOperation.ToolApproval, diagnostics.SupportedOperations);
    }

    [Fact]
    public async Task Diagnostics_reports_available_when_enabled_and_sdk_ping_succeeds()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.ModelIds.Add("gpt-5");

        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.True(diagnostics.IsAvailable);
        Assert.Equal("configured", diagnostics.Metadata["statusCode"]);
        Assert.Equal("configured-token", diagnostics.Metadata["authMode"]);
        Assert.Equal("gpt-5", diagnostics.Metadata["models"]);
        Assert.True(_factory.Client.Started);
        Assert.True(_factory.Client.Pinged);
    }

    [Fact]
    public async Task Diagnostics_reports_sdk_failure_without_exposing_token()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.PingException = new InvalidOperationException("not authenticated");

        var diagnostics = await _provider.GetDiagnosticsAsync();

        Assert.False(diagnostics.IsAvailable);
        Assert.Equal("sdk-unavailable", diagnostics.Metadata["statusCode"]);
        Assert.DoesNotContain("test-token", diagnostics.Status);
        Assert.DoesNotContain("test-token", diagnostics.Metadata.Values);
    }

    [Fact]
    public async Task Create_session_uses_elsa_session_id_for_sdk_session()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _options.Model = "gpt-5";
        var session = CreateSession("session-1");

        var providerSession = await _provider.CreateSessionAsync(session);

        Assert.Equal("session-1", providerSession.Id);
        Assert.Equal("session-1", _factory.Client.CreatedSessionRequest?.SessionId);
        Assert.Equal("gpt-5", _factory.Client.CreatedSessionRequest?.Model);
        Assert.Equal("session-1", providerSession.Metadata["sessionId"]);
    }

    [Fact]
    public async Task Stream_maps_started_delta_completed_events()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Started));
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.MessageDelta, "Hello"));
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Completed));

        var events = await StreamAsync(new("session-1", "Explain", []));

        Assert.Collection(
            events,
            started => Assert.Equal(AgentStreamEventKind.Started, started.Kind),
            delta =>
            {
                Assert.Equal(AgentStreamEventKind.MessageDelta, delta.Kind);
                Assert.Equal("Hello", delta.Content);
            },
            completed => Assert.Equal(AgentStreamEventKind.Completed, completed.Kind));
    }

    [Fact]
    public async Task Stream_includes_sanitized_context_in_prompt()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Completed));
        var context = new AgentContextAttachment(
            "ctx-1",
            "workflow.definition",
            "wf-1",
            "Workflow",
            "application/json",
            AgentContextSensitivity.Internal,
            "selection",
            "Workflow summary.",
            new { Name = "Demo" },
            new Dictionary<string, string>());

        _ = await StreamAsync(new("session-1", "Explain", [context]));

        Assert.Contains("Elsa context attachments:", _factory.Client.Session.LastPrompt);
        Assert.Contains("Workflow summary.", _factory.Client.Session.LastPrompt);
        Assert.Contains("Demo", _factory.Client.Session.LastPrompt);
    }

    [Fact]
    public async Task Stream_maps_sdk_error_event()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.Events.Add(new(GitHubCopilotStreamEventKind.Error, ErrorCode: "sdk.error", ErrorMessage: "SDK failed"));

        var events = await StreamAsync(new("session-1", "Explain", []));

        var error = Assert.Single(events);
        Assert.Equal(AgentStreamEventKind.Error, error.Kind);
        Assert.Equal("sdk.error", error.Error?.Code);
        Assert.Equal("SDK failed", error.Error?.Message);
    }

    [Fact]
    public async Task Stream_observes_cancellation()
    {
        _options.Enabled = true;
        _options.GitHubToken = "test-token";
        _factory.Client.Session.WaitForCancellation = true;
        using var cts = new CancellationTokenSource();

        var task = StreamAsync(new("session-1", "Explain", []), cts.Token);
        await Task.Yield();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(_factory.Client.Session.CancellationObserved);
    }

    [Fact]
    public async Task Tool_approval_is_not_exposed_directly()
    {
        var result = await _provider.ApproveToolAsync(new("session-1", "tool-1", true, null));

        Assert.False(result.Accepted);
        Assert.Contains("Elsa-owned proposal approval", result.Message);
    }

    private async Task<List<AgentStreamEvent>> StreamAsync(AgentProviderMessage message, CancellationToken cancellationToken = default)
    {
        var events = new List<AgentStreamEvent>();
        await foreach (var item in _provider.SendMessageAsync(message, cancellationToken))
            events.Add(item);
        return events;
    }

    private static AgentSession CreateSession(string id) => new(
        id,
        "Test session",
        "tenant-1",
        "actor-1",
        "conversation-1",
        GitHubCopilotAgentProvider.Id,
        "explain",
        AgentPolicy.Default,
        AgentSessionStatus.Active,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        null,
        new Dictionary<string, string>());

    private sealed class FakeCopilotClientFactory : IGitHubCopilotClientFactory
    {
        public FakeCopilotClient Client { get; } = new();

        public IGitHubCopilotClient Create(GitHubCopilotClientRequest request)
        {
            Client.LastClientRequest = request;
            return Client;
        }
    }

    private sealed class FakeCopilotClient : IGitHubCopilotClient
    {
        public FakeCopilotSession Session { get; } = new();

        public List<string> ModelIds { get; } = [];

        public GitHubCopilotClientRequest? LastClientRequest { get; set; }

        public GitHubCopilotSessionRequest? CreatedSessionRequest { get; private set; }

        public bool Started { get; private set; }

        public bool Pinged { get; private set; }

        public Exception? PingException { get; set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            return Task.CompletedTask;
        }

        public Task PingAsync(CancellationToken cancellationToken)
        {
            Pinged = true;
            return PingException is null ? Task.CompletedTask : Task.FromException(PingException);
        }

        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>(ModelIds);

        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            CreatedSessionRequest = request;
            Session.SessionId = request.SessionId;
            return Task.FromResult<IGitHubCopilotSession>(Session);
        }

        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
        {
            Session.SessionId = request.SessionId;
            return Task.FromResult<IGitHubCopilotSession>(Session);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeCopilotSession : IGitHubCopilotSession
    {
        public List<GitHubCopilotStreamEvent> Events { get; } = [];

        public string SessionId { get; set; } = string.Empty;

        public string? WorkspacePath => null;

        public string LastPrompt { get; private set; } = string.Empty;

        public bool WaitForCancellation { get; set; }

        public bool CancellationObserved { get; private set; }

        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            if (WaitForCancellation)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved = true;
                    throw;
                }
            }

            foreach (var item in Events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
        }

        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
