using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("Probe failed: " + name);
    checks.Add(name);
}

if (args.Length == 2 && args[0] == "--restore-only")
{
    var reader = new FixtureClient((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "continued"))));
    var processAgent = new ChatClientAgent(reader, instructions: "Write the supplied brief.", name: "Copywriter");
    using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(args[1]));
    var persistedSession = await processAgent.DeserializeSessionAsync(persisted.RootElement);
    await processAgent.RunAsync("Continue in another process.", persistedSession);
    Check(reader.Calls.Single().History.Any(m => m.Role == ChatRole.User && m.Text.Contains("moss gardens")),
        "separate process restores previous user message from persisted SDK session");
    Console.WriteLine(JsonSerializer.Serialize(new { checks, passed = checks.Count, liveModel = false, separateProcess = true }));
    return;
}

var client = new FixtureClient((history, options, token) => Task.FromResult(
    new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"article\":\"draft\"}"))
    { Usage = new UsageDetails { InputTokenCount = 5, OutputTokenCount = 3 } }));
var agent = new ChatClientAgent(client, instructions: "Write the supplied brief.", name: "Copywriter");
var schema = JsonSerializer.SerializeToElement(new
{
    type = "object", properties = new { article = new { type = "string" } },
    required = new[] { "article" }, additionalProperties = false
});
var format = ChatResponseFormat.ForJsonSchema(schema, "article");
var options = new ChatClientAgentRunOptions(new ChatOptions { ResponseFormat = format, ModelId = "fixture-model" });
var session = await agent.CreateSessionAsync();
var response = await agent.RunAsync("First brief: moss gardens.", session, options);
Check(client.Calls[0].Options?.ResponseFormat is ChatResponseFormatJson, "dynamic schema reaches model client");
Check(client.Calls[0].Options?.ModelId == "fixture-model", "model selection reaches model client");
Check(JsonDocument.Parse(response.Text).RootElement.GetProperty("article").GetString() == "draft", "structured result returned");
Check(response.Usage?.InputTokenCount == 5 && response.Usage?.OutputTokenCount == 3, "usage returned");

var state = await agent.SerializeSessionAsync(session);
Check(state.GetRawText().Contains("moss gardens"), "session serialization retains conversation");
var writeIndex = Array.IndexOf(args, "--write-session");
if (writeIndex >= 0) await File.WriteAllTextAsync(args[writeIndex + 1], state.GetRawText());
var recreated = new ChatClientAgent(client, instructions: "Write the supplied brief.", name: "Copywriter");
var restored = args.Contains("--drop-continuity")
    ? await recreated.CreateSessionAsync()
    : await recreated.DeserializeSessionAsync(state);
await recreated.RunAsync("Continue with a headline.", restored);
Check(client.Calls[^1].History.Any(m => m.Role == ChatRole.User && m.Text.Contains("moss gardens")), "restored session sends previous user message");
await recreated.RunAsync("Fresh brief: bridges.", await recreated.CreateSessionAsync());
Check(!client.Calls[^1].History.Any(m => m.Text.Contains("moss gardens")), "fresh session isolates previous conversation");

var toolCalls = 0;
var toolClient = new FixtureClient((history, chatOptions, token) =>
{
    var result = history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();
    return Task.FromResult(result is null
        ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("read-style-1", "read_style", new Dictionary<string, object?>())]))
        : new ChatResponse(new ChatMessage(ChatRole.Assistant, "Style read.")));
});
var tool = AIFunctionFactory.Create(() => { toolCalls++; return "plain language"; }, "read_style");
var toolAgent = new ChatClientAgent(toolClient, name: "Proofreader", tools: [tool]);
await toolAgent.RunAsync("Read the style guide.");
Check(toolCalls == 1, "framework invokes configured function once");
Check(toolClient.Calls.Count == 2 && toolClient.Calls[^1].History.SelectMany(m => m.Contents)
    .OfType<FunctionResultContent>().Any(r => r.CallId == "read-style-1"), "tool result is correlated into next model call");

var protectedCalls = 0;
var approvalClient = new FixtureClient((history, chatOptions, token) => Task.FromResult(
    history.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Any()
        ? new ChatResponse(new ChatMessage(ChatRole.Assistant, "Approved tool completed."))
        : new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("publish-1", "publish", new Dictionary<string, object?>())]))));
var protectedTool = new ApprovalRequiredAIFunction(AIFunctionFactory.Create(() => { protectedCalls++; return "published"; }, "publish"));
var approvalAgent = new ChatClientAgent(approvalClient, tools: [protectedTool]);
var approvalSession = await approvalAgent.CreateSessionAsync();
var proposal = await approvalAgent.RunAsync("Publish.", approvalSession);
var approval = proposal.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().Single();
Check(protectedCalls == 0 && approval.ToolCall is FunctionCallContent call && call.CallId == "publish-1", "approval-required tool pauses before effect");
var pendingState = await approvalAgent.SerializeSessionAsync(approvalSession);
var resumedApprovalAgent = new ChatClientAgent(approvalClient, tools: [protectedTool]);
var resumedApprovalSession = await resumedApprovalAgent.DeserializeSessionAsync(pendingState);
await resumedApprovalAgent.RunAsync(new ChatMessage(ChatRole.User, [approval.CreateResponse(true)]), resumedApprovalSession);
Check(protectedCalls == 1, "SDK approval continuation executes after session restore");
var deniedSession = await approvalAgent.CreateSessionAsync();
var deniedProposal = await approvalAgent.RunAsync("Publish.", deniedSession);
var deniedRequest = deniedProposal.Messages.SelectMany(m => m.Contents).OfType<ToolApprovalRequestContent>().Single();
await approvalAgent.RunAsync(new ChatMessage(ChatRole.User, [deniedRequest.CreateResponse(false)]), deniedSession);
Check(protectedCalls == 1, "denied SDK tool approval produces no effect");

var otherAgent = new ChatClientAgent(client, instructions: "Unrelated specialist.", name: "DifferentVersion");
var foreignState = await otherAgent.DeserializeSessionAsync(state);
await otherAgent.RunAsync("Another task.", foreignState);
Check(client.Calls[^1].History.Any(m => m.Text.Contains("moss gardens")), "SDK session format alone does not enforce Elsa agent/version ownership");

var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var cancelClient = new FixtureClient(async (history, chatOptions, token) =>
{
    entered.SetResult();
    await Task.Delay(Timeout.Infinite, token);
    throw new InvalidOperationException("Unreachable");
});
var cancelAgent = new ChatClientAgent(cancelClient);
using var cts = new CancellationTokenSource();
var pending = cancelAgent.RunAsync("Wait.", cancellationToken: cts.Token);
await entered.Task;
cts.Cancel();
var cancelled = false;
try { await pending; } catch (OperationCanceledException) { cancelled = true; }
Check(cancelled, "in-flight cancellation propagates to model client");

var streamed = new ChatClientAgent(new FixtureClient((_, _, _) => throw new InvalidOperationException("Expected streaming path")));
var text = "";
await foreach (var update in streamed.RunStreamingAsync("Stream.")) text += update.Text;
Check(text == "first second", "streamed text deltas reach caller");

var invalidClient = new FixtureClient((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"wrong\":true}"))));
var invalidAgent = new ChatClientAgent(invalidClient);
var invalidResponse = await invalidAgent.RunAsync("Return an article.", options: options);
Check(!JsonDocument.Parse(invalidResponse.Text).RootElement.TryGetProperty("article", out _), "SDK raw response can violate requested schema; Elsa must validate");

var alpha = new ChatClientAgent(new FixtureClient((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "alpha")))));
var beta = new ChatClientAgent(new FixtureClient((_, _, _) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "beta")))));
Func<string, string> seed = input => input;
Func<string, ValueTask<string>> first = async input => (await alpha.RunAsync(input)).Text;
Func<string, ValueTask<string>> second = async input => (await beta.RunAsync(input)).Text;
var cohorts = new List<int>();
var start = seed.BindAsExecutor("brief");
var left = first.BindAsExecutor("alpha");
var right = second.BindAsExecutor("beta");
var aggregate = new CohortCollector(cohorts);
var team = new WorkflowBuilder(start).AddFanOutEdge(start, [left, right])
    .AddFanInBarrierEdge([left, right], aggregate).WithOutputFrom(aggregate).Build();
await using var teamRun = await InProcessExecution.RunAsync(team, "Compare both specialists.");
var outputs = teamRun.NewEvents.OfType<WorkflowOutputEvent>().Select(x => x.Data).OfType<string>().ToArray();
if (!outputs.SequenceEqual(["alpha|beta"]) || !cohorts.SequenceEqual([2]))
    Console.Error.WriteLine(JsonSerializer.Serialize(new { outputs, cohorts, events = teamRun.OutgoingEvents.Select(e => new { kind = e.GetType().Name, data = e.ToString() }) }));
Check(outputs.SequenceEqual(["alpha|beta"]) && cohorts.SequenceEqual([2]), "MAF fan-in barrier receives both specialist results in one cohort");

Console.WriteLine(JsonSerializer.Serialize(new
{
    package = "Microsoft.Agents.AI", version = typeof(ChatClientAgent).Assembly.GetName().Version?.ToString(),
    checks, passed = checks.Count, liveModel = false, elsaRuntimeIntegration = false,
    limitations = new[] { "Fixture model client; no provider capability proof", "Session restore is not durable effect recovery", "Tool invoked by SDK without Elsa authorization", "Elsa schema validation still required" }
}, new JsonSerializerOptions { WriteIndented = true }));

[YieldsOutput(typeof(string))]
sealed class CohortCollector(List<int> cohorts) : Executor<string>("review")
{
    private readonly List<string> _values = [];
    public override ValueTask HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        _values.Add(message);
        return ValueTask.CompletedTask;
    }
    protected override async ValueTask OnMessageDeliveryFinishedAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        cohorts.Add(_values.Count);
        await context.YieldOutputAsync(string.Join("|", _values.Order(StringComparer.Ordinal)), cancellationToken);
        _values.Clear();
    }
}

sealed class FixtureClient(Func<IReadOnlyList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> response) : IChatClient
{
    public List<(IReadOnlyList<ChatMessage> History, ChatOptions? Options)> Calls { get; } = [];
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        Calls.Add((snapshot, options));
        return response(snapshot, options, cancellationToken);
    }
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new(ChatRole.Assistant, "first ");
        await Task.Yield();
        yield return new(ChatRole.Assistant, "second");
    }
    public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;
    public void Dispose() { }
}
