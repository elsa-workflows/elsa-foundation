using System.Threading.Channels;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Elsa.Agent.GitHubCopilot.Services;

internal sealed class GitHubCopilotSdkClientFactory(ILoggerFactory loggerFactory) : IGitHubCopilotClientFactory
{
    public IGitHubCopilotClient Create(GitHubCopilotClientRequest request)
    {
        var options = new CopilotClientOptions
        {
            GitHubToken = request.GitHubToken,
            UseLoggedInUser = request.UseLoggedInUser,
            BaseDirectory = request.BaseDirectory,
            WorkingDirectory = request.WorkingDirectory,
            Logger = loggerFactory.CreateLogger<CopilotClient>()
        };

        if (!string.IsNullOrWhiteSpace(request.RuntimeUrl))
            options.Connection = RuntimeConnection.ForUri(request.RuntimeUrl, request.RuntimeConnectionToken);

        return new GitHubCopilotSdkClient(new CopilotClient(options));
    }
}

internal sealed class GitHubCopilotSdkClient(CopilotClient client) : IGitHubCopilotClient
{
    public Task StartAsync(CancellationToken cancellationToken) => client.StartAsync(cancellationToken);

    public async Task PingAsync(CancellationToken cancellationToken) => _ = await client.PingAsync("elsa", cancellationToken);

    public async Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        var models = await client.ListModelsAsync(cancellationToken);
        return models.Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
    }

    public async Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
    {
        var session = await client.CreateSessionAsync(BuildConfig(request), cancellationToken);
        return new GitHubCopilotSdkSession(session);
    }

    public async Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
    {
        var session = await client.ResumeSessionAsync(request.SessionId, BuildResumeConfig(request), cancellationToken);
        return new GitHubCopilotSdkSession(session);
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();

    private static SessionConfig BuildConfig(GitHubCopilotSessionRequest request)
    {
        var config = new SessionConfig
        {
            SessionId = request.SessionId
        };

        ApplyConfig(config, request);
        return config;
    }

    private static ResumeSessionConfig BuildResumeConfig(GitHubCopilotSessionRequest request)
    {
        var config = new ResumeSessionConfig();
        ApplyConfig(config, request);
        return config;
    }

    private static void ApplyConfig(SessionConfigBase config, GitHubCopilotSessionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Model))
            config.Model = request.Model;
        config.ReasoningEffort = request.ReasoningEffort;
        config.Streaming = request.Streaming;
        config.AvailableTools = request.AvailableTools.ToList();
        config.ExcludedTools = request.ExcludedTools.ToList();
        if (request.CustomTools is { Count: > 0 })
        {
            // Tools backed by an AIFunction are auto-invoked by the harness during the turn.
            config.Tools = request.CustomTools.Cast<AIFunctionDeclaration>().ToList();
        }
        config.OnPermissionRequest = (_, _) => Task.FromResult(PermissionDecision.Reject("Elsa requires workflow, file, package, runtime, and external-service mutations to be proposed through Elsa-owned approval flows."));

        if (!string.IsNullOrWhiteSpace(request.SystemMessage))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,
                Content = request.SystemMessage
            };
        }
    }
}

internal sealed class GitHubCopilotSdkSession(CopilotSession session) : IGitHubCopilotSession
{
    public string SessionId => session.SessionId;

    public string? WorkspacePath => session.WorkspacePath;

    public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<GitHubCopilotStreamEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        using var subscription = session.On<SessionEvent>(evt => OnSessionEvent(evt, channel.Writer));
        await using var cancellationRegistration = cancellationToken.Register(static state =>
        {
            var tuple = (Tuple<GitHubCopilotSdkSession, ChannelWriter<GitHubCopilotStreamEvent>>)state!;
            _ = tuple.Item1.AbortAsync(CancellationToken.None);
            tuple.Item2.TryComplete();
        }, Tuple.Create(this, channel.Writer));

        try
        {
            await session.SendAsync(new MessageOptions { Prompt = prompt }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            channel.Writer.TryWrite(new(GitHubCopilotStreamEventKind.Error, ErrorCode: "agent.provider.github_copilot.send_failed", ErrorMessage: ex.Message));
            channel.Writer.TryComplete();
        }

        await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken))
            yield return item;
    }

    public Task AbortAsync(CancellationToken cancellationToken) => session.AbortAsync(cancellationToken);

    public ValueTask DisposeAsync() => session.DisposeAsync();

    private static void OnSessionEvent(SessionEvent evt, ChannelWriter<GitHubCopilotStreamEvent> writer)
    {
        switch (evt)
        {
            case SessionStartEvent:
                writer.TryWrite(new(GitHubCopilotStreamEventKind.Started));
                break;
            case AssistantMessageDeltaEvent delta:
                writer.TryWrite(new(GitHubCopilotStreamEventKind.MessageDelta, delta.Data.DeltaContent));
                break;
            case ToolExecutionStartEvent start:
                writer.TryWrite(new(GitHubCopilotStreamEventKind.ToolExecutionStarted, ToolCallId: start.Data.ToolCallId, ToolName: start.Data.ToolName));
                break;
            case ToolExecutionCompleteEvent complete:
                writer.TryWrite(new(GitHubCopilotStreamEventKind.ToolExecutionCompleted, ToolCallId: complete.Data.ToolCallId, Success: complete.Data.Success, ErrorMessage: complete.Data.Error?.ToString()));
                break;
            case SessionErrorEvent error:
                writer.TryWrite(new(GitHubCopilotStreamEventKind.Error, ErrorCode: "agent.provider.github_copilot.sdk_error", ErrorMessage: error.Data.Message));
                writer.TryComplete();
                break;
            case SessionIdleEvent:
                writer.TryWrite(new(GitHubCopilotStreamEventKind.Completed));
                writer.TryComplete();
                break;
        }
    }
}
