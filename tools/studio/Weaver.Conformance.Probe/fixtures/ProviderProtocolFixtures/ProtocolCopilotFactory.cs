using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.GitHubCopilot.Services;
using Microsoft.Extensions.AI;

namespace Acme.Weaver.ProtocolFixtures;

internal sealed class ProtocolCopilotFactory(ProtocolFiles files) : IGitHubCopilotClientFactory
{
    // Deliberately neither inspect nor retain the request's authentication fields.
    public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => new ProtocolCopilotClient(files);
}

internal sealed class ProtocolCopilotClient(ProtocolFiles files) : IGitHubCopilotClient
{
    public Task StartAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task PingAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyCollection<string>>(["protocol-fixture"]);
    }
    public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken) => Open(request, cancellationToken);
    public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken) => Open(request, cancellationToken);
    private Task<IGitHubCopilotSession> Open(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IGitHubCopilotSession>(new ProtocolCopilotSession(files, request));
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class ProtocolCopilotSession(ProtocolFiles files, GitHubCopilotSessionRequest request) : IGitHubCopilotSession
{
    private readonly CancellationTokenSource aborted = new();
    public string SessionId => request.SessionId;
    public string? WorkspacePath => null;

    public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, aborted.Token);
        linked.Token.ThrowIfCancellationRequested();
        var command = files.ReadCommand();
        var functions = request.CustomTools?.ToArray() ?? [];
        files.Trace("copilot.request", new { command.CommandId, sessionId = SessionId, request.SystemMessage, prompt,
            tools = functions.Select(function => new { function.Name, function.Description, function.JsonSchema }) });
        yield return new(GitHubCopilotStreamEventKind.Started);

        if (functions.Length == 0)
        {
            // The actual adapter supplies its receipt continuation with no callable functions.
            // Preserve the real generic serialized results, never re-invoke the earlier command.
            var results = new List<JsonElement>();
            foreach (var line in prompt.Split('\n'))
            {
                const string prefix = "  Typed result: ";
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                try { using var document = JsonDocument.Parse(line[prefix.Length..]); results.Add(document.RootElement.Clone()); }
                catch (JsonException exception) { throw new ProtocolFixtureException("The adapter continuation contains invalid JSON.", exception); }
            }
            if (results.Count == 0)
                throw new ProtocolFixtureException("No callable tool or actual adapter completion was supplied.");
            if (results.Any(result => result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("toolName", out var name) || name.ValueKind != JsonValueKind.String ||
                !string.Equals(name.GetString(), command.ToolName, StringComparison.Ordinal) ||
                !result.TryGetProperty("toolCallId", out var callId) || callId.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(callId.GetString())))
                throw new ProtocolFixtureException("The adapter completion does not identify the commanded tool call.");
            files.Trace("copilot.continuation", new { command.CommandId, sessionId = SessionId, results });
            yield return new(GitHubCopilotStreamEventKind.MessageDelta, ProtocolFiles.OutcomeText(results));
        }
        else
        {
            var matches = functions.Where(function => string.Equals(function.Name, command.ToolName, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                throw new ProtocolFixtureException("The commanded tool is not uniquely advertised by the actual adapter.");
            await files.BeforeCallAsync("copilot", command, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            yield return new(GitHubCopilotStreamEventKind.ToolExecutionStarted, ToolCallId: command.CommandId, ToolName: command.ToolName);
            linked.Token.ThrowIfCancellationRequested();
            files.Trace("copilot.call", new { command.CommandId, command.ToolName, command.Arguments });
            var result = await matches[0].InvokeAsync(new AIFunctionArguments(command.ToArguments()), linked.Token);
            files.Trace("copilot.return", new { command.CommandId, command.ToolName, result });
            linked.Token.ThrowIfCancellationRequested();
            // This event reports successful SDK function transport, never an owning-resource effect.
            yield return new(GitHubCopilotStreamEventKind.ToolExecutionCompleted, ToolCallId: command.CommandId, ToolName: command.ToolName, Success: true);
            yield return new(GitHubCopilotStreamEventKind.MessageDelta, ProtocolFiles.OutcomeText(result));
        }
        linked.Token.ThrowIfCancellationRequested();
        yield return new(GitHubCopilotStreamEventKind.Completed);
    }

    public Task AbortAsync(CancellationToken cancellationToken) { aborted.Cancel(); return Task.CompletedTask; }
    public ValueTask DisposeAsync() { aborted.Dispose(); return ValueTask.CompletedTask; }
}
