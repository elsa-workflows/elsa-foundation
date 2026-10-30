using System.Runtime.CompilerServices;
using Elsa.Agent.Anthropic.Services;
using Microsoft.Extensions.AI;

namespace Acme.Weaver.ProtocolFixtures;

internal sealed class ProtocolAnthropicFactory(ProtocolFiles files) : IAnthropicChatClientFactory
{
    // Credentials are unused, never copied to evidence, and no SDK/network client is constructed.
    public IChatClient Create(string apiKey, string model) => new ProtocolAnthropicClient(files);
}

internal sealed class ProtocolAnthropicClient(ProtocolFiles files) : IChatClient
{
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var command = files.ReadCommand();
        var transcript = messages.ToList();
        var functions = options?.Tools?.OfType<AIFunction>().ToArray() ?? [];
        files.Trace("anthropic.request", new { command.CommandId, messages = transcript.Select(message => new
            { role = message.Role.Value, contents = message.Contents.Select(Describe) }),
            tools = functions.Select(function => new { function.Name, function.Description, function.JsonSchema }) });

        // Only results after the latest user message belong to this turn. Historical results cannot
        // suppress a replayed/new user request carrying the same driver command ID.
        var lastUser = transcript.FindLastIndex(message => message.Role == ChatRole.User);
        var results = transcript.Skip(lastUser + 1).SelectMany(message => message.Contents).OfType<FunctionResultContent>()
            .Where(result => string.Equals(result.CallId, command.CommandId, StringComparison.Ordinal)).ToArray();
        if (results.Length > 0)
        {
            if (results.Length != 1)
                throw new ProtocolFixtureException("The adapter supplied conflicting or duplicate function completions.");
            files.Trace("anthropic.continuation", new { command.CommandId, results = results.Select(Describe) });
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [new TextContent(ProtocolFiles.OutcomeText(results.Select(result => result.Result).ToArray()))] };
        }
        else
        {
            if (functions.Count(function => string.Equals(function.Name, command.ToolName, StringComparison.Ordinal)) != 1)
                throw new ProtocolFixtureException("The commanded tool is not uniquely advertised by the actual adapter.");
            files.Trace("anthropic.call", new { command.CommandId, command.ToolName, command.Arguments });
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            // Do not invoke declaration-only AIFunctions: the unchanged host orchestrator dispatches this.
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant,
                Contents = [new FunctionCallContent(command.CommandId, command.ToolName, command.ToArguments())] };
        }
    }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var contents = new List<AIContent>();
        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken)) contents.AddRange(update.Contents);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents));
    }

    private static object Describe(AIContent content) => content switch
    {
        TextContent text => new { kind = "text", text.Text },
        FunctionCallContent call => new { kind = "functionCall", call.CallId, call.Name, call.Arguments },
        FunctionResultContent result => new { kind = "functionResult", result.CallId, result.Result },
        _ => new { kind = content.GetType().Name }
    };

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }
}
