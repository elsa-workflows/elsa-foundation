using System.Runtime.CompilerServices;
using Elsa.Agent.Anthropic.Options;
using Elsa.Agent.Anthropic.Services;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Core.Services;
using Elsa.Agent.GitHubCopilot.Options;
using Elsa.Agent.GitHubCopilot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Agent.Tests;

/// <summary>
/// Issue #414 item 7: the agent providers logged the raw exception object at Debug while the
/// client-facing error was redacted via <c>Normalize</c>/<c>NormalizeMessage</c> — so an exception
/// embedding the API key/token reached the log sink unredacted. These tests capture everything a
/// sink can render (formatted message, state, and the exception's full <c>ToString()</c> including
/// inner exceptions) and prove the secret never reaches it on any provider Debug-log path, while
/// the redaction marker does.
/// </summary>
public sealed class AgentProviderLogRedactionTests
{
    private const string AnthropicSecret = "sk-ant-SECRET-4242";
    private const string CopilotSecret = "ghp_SECRETTOKEN4242";
    private const string RedactionMarker = "[redacted]";

    // --- Anthropic: streaming-turn failure (AnthropicAgentProvider.cs LogDebug site) ---

    [Fact]
    public async Task Anthropic_streaming_failure_does_not_leak_the_api_key_to_the_log_sink()
    {
        var logger = new CapturingLogger<AnthropicAgentProvider>();
        var options = Options.Create(new AnthropicAgentOptions { Enabled = true, ApiKey = AnthropicSecret, Model = "claude-test" });
        var provider = new AnthropicAgentProvider(
            new ThrowingChatClientFactory(new Exception($"401 Unauthorized: invalid x-api-key '{AnthropicSecret}'.")),
            options,
            logger);
        var context = AgentTurnContext.ForMessage("s1", "Hi", []);

        var events = await Collect(provider.ContinueTurnAsync(context));

        // The client-facing error is redacted (pre-existing behavior, must stay true).
        var error = Assert.Single(events, e => e.Kind == AgentStreamEventKind.Error);
        Assert.DoesNotContain(AnthropicSecret, error.Error!.Message);

        AssertSinkRedacted(logger, AnthropicSecret);
    }

    // --- GitHub Copilot: session-resume failure on the IAgentProvider path (ContinueTurnAsync) ---

    [Fact]
    public async Task Copilot_resume_failure_on_the_provider_path_does_not_leak_the_token_to_the_log_sink()
    {
        var (provider, logger, _) = BuildCopilot(resumeException: SecretfulCopilotException());
        var context = AgentTurnContext.ForMessage("s1", "Hi", []);

        await Collect(provider.ContinueTurnAsync(context));

        AssertSinkRedacted(logger, CopilotSecret);
    }

    // --- GitHub Copilot: session-resume failure on the IAgentHarness path (RunTurnAsync) ---

    [Fact]
    public async Task Copilot_resume_failure_on_the_harness_path_does_not_leak_the_token_to_the_log_sink()
    {
        var (provider, logger, _) = BuildCopilot(resumeException: SecretfulCopilotException());
        var request = new AgentHarnessTurnRequest("s1", "actor-1", [new(AgentRole.User, "do it")], [], [], AgentPolicy.Default);

        await Collect(provider.RunTurnAsync(request));

        AssertSinkRedacted(logger, CopilotSecret);
    }

    // --- GitHub Copilot: diagnostics failure (GetDiagnosticsAsync) ---

    [Fact]
    public async Task Copilot_diagnostics_failure_does_not_leak_the_token_to_the_log_sink()
    {
        var (provider, logger, _) = BuildCopilot(pingException: SecretfulCopilotException());

        var diagnostics = await provider.GetDiagnosticsAsync();

        // The client-facing status is redacted (pre-existing behavior, must stay true).
        Assert.DoesNotContain(CopilotSecret, diagnostics.Status);
        AssertSinkRedacted(logger, CopilotSecret);
    }

    private static Exception SecretfulCopilotException() =>
        new Exception(
            "SDK handshake failed.",
            new Exception($"HTTP 401 from api.github.com: bad credentials for token {CopilotSecret}."));

    /// <summary>
    /// The core assertion: no rendering a log sink can produce (formatted message, state, or the
    /// exception's ToString including inner exceptions and stack) contains the raw secret, and the
    /// redaction marker proves the failure was still logged with its diagnostic content.
    /// </summary>
    private static void AssertSinkRedacted(CapturingLogger logger, string secret)
    {
        Assert.NotEmpty(logger.Entries);

        foreach (var entry in logger.Entries)
        {
            Assert.DoesNotContain(secret, entry.Message);
            Assert.DoesNotContain(secret, entry.Exception?.ToString() ?? "");
        }

        Assert.Contains(logger.Entries, e => e.Message.Contains(RedactionMarker));
    }

    // --- Builders ---

    private static (GitHubCopilotAgentProvider Provider, CapturingLogger<GitHubCopilotAgentProvider> Logger, ScriptableCopilotClient Client) BuildCopilot(
        Exception? resumeException = null,
        Exception? pingException = null)
    {
        var logger = new CapturingLogger<GitHubCopilotAgentProvider>();
        var audit = new InMemoryAgentAuditStore();
        var invoker = new DefaultAgentToolInvoker(
            new DefaultAgentToolRegistry([]),
            new InMemoryAgentProposalService(new NoopAgentActionProposalExecutor(), audit),
            audit);
        var client = new ScriptableCopilotClient { ResumeException = resumeException, PingException = pingException };
        var options = Options.Create(new GitHubCopilotAgentOptions { Enabled = true, GitHubToken = CopilotSecret });
        var provider = new GitHubCopilotAgentProvider(new ScriptableCopilotClientFactory(client), options, invoker, logger);
        return (provider, logger, client);
    }

    private static async Task<List<AgentStreamEvent>> Collect(IAsyncEnumerable<AgentStreamEvent> events)
    {
        var list = new List<AgentStreamEvent>();
        await foreach (var item in events)
            list.Add(item);
        return list;
    }

    // --- Capturing logger ---

    public record LogEntry(LogLevel Level, string Message, Exception? Exception);

    public class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    public sealed class CapturingLogger<T> : CapturingLogger, ILogger<T>;

    // --- Anthropic fakes ---

    private sealed class ThrowingChatClientFactory(Exception exception) : IAnthropicChatClientFactory
    {
        public IChatClient Create(string apiKey, string model) => new ThrowingChatClient(exception);
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("The provider drives streaming only.");

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    // --- GitHub Copilot fakes ---

    private sealed class ScriptableCopilotClientFactory(ScriptableCopilotClient client) : IGitHubCopilotClientFactory
    {
        public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => client;
    }

    private sealed class ScriptableCopilotClient : IGitHubCopilotClient
    {
        public Exception? ResumeException { get; init; }

        public Exception? PingException { get; init; }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PingAsync(CancellationToken cancellationToken)
            => PingException is null ? Task.CompletedTask : Task.FromException(PingException);

        public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyCollection<string>>([]);

        public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IGitHubCopilotSession>(new EmptyCopilotSession(request.SessionId));

        public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
            => ResumeException is null
                ? Task.FromResult<IGitHubCopilotSession>(new EmptyCopilotSession(request.SessionId))
                : Task.FromException<IGitHubCopilotSession>(ResumeException);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyCopilotSession(string sessionId) : IGitHubCopilotSession
    {
        public string SessionId => sessionId;

        public string? WorkspacePath => null;

        public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }

        public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
