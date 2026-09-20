using Microsoft.Extensions.AI;

namespace Elsa.Agent.GitHubCopilot.Services;

public interface IGitHubCopilotClientFactory
{
    IGitHubCopilotClient Create(GitHubCopilotClientRequest request);
}

public interface IGitHubCopilotClient : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken);

    Task PingAsync(CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken);

    Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken);

    Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken);
}

public interface IGitHubCopilotSession : IAsyncDisposable
{
    string SessionId { get; }

    string? WorkspacePath { get; }

    IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, CancellationToken cancellationToken);

    Task AbortAsync(CancellationToken cancellationToken);
}

public sealed record GitHubCopilotClientRequest(
    string? GitHubToken,
    bool UseLoggedInUser,
    string? RuntimeUrl,
    string? RuntimeConnectionToken,
    string? BaseDirectory,
    string? WorkingDirectory);

public sealed record GitHubCopilotSessionRequest(
    string SessionId,
    string? Model,
    string? ReasoningEffort,
    bool Streaming,
    string? SystemMessage,
    IReadOnlyCollection<string> AvailableTools,
    IReadOnlyCollection<string> ExcludedTools,
    IReadOnlyCollection<AIFunction>? CustomTools = null);

public enum GitHubCopilotStreamEventKind
{
    Started,
    MessageDelta,
    ToolExecutionStarted,
    ToolExecutionCompleted,
    Completed,
    Error
}

public sealed record GitHubCopilotStreamEvent(
    GitHubCopilotStreamEventKind Kind,
    string? Content = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? ToolCallId = null,
    string? ToolName = null,
    bool? Success = null);
