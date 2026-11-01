using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Credentials;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ClientModel;
using System.ClientModel.Primitives;

var output = args.FirstOrDefault(value => !value.StartsWith("--", StringComparison.Ordinal))
    ?? throw new ArgumentException("An owned output directory is required.");
Directory.CreateDirectory(output);
var observations = new List<object>();
var schema = JsonSerializer.SerializeToElement(new
{
    type = "object", properties = new { headline = new { type = "string" } },
    required = new[] { "headline" }, additionalProperties = false
});
var status = "InProgress";
try
{
    foreach (var provider in new[] { "anthropic", "azure" })
    {
        await Success(provider);
        await Retry(provider, defaultPolicy: true);
        await Retry(provider, defaultPolicy: false);
        await Loss(provider);
        foreach (var mode in new[] { "refusal", "invalid", "truncated" }) await ResultBoundary(provider, mode);
        await Cancellation(provider, beforeSend: true);
        await Cancellation(provider, beforeSend: false);
    }
    await RichContent();
    await TokenRefresh();
    status = "PassedProviderProtocolSpike";
}
catch (Exception exception)
{
    status = "Failed";
    observations.Add(new { name = "failure", type = exception.GetType().FullName, message = exception is ProbeFailure ? exception.Message : "SDK fixture failed; no provider credentials or raw response are recorded." });
    Console.Error.WriteLine(exception is ProbeFailure ? exception.Message : exception.GetType().FullName);
}
finally
{
    await File.WriteAllTextAsync(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
    {
        status, liveProvider = false, inferenceCharge = false,
        boundary = "Injected HTTP transport calls; no sockets or provider effects are exercised.",
        packages = new { maf = "1.27.0", meaiOpenAI = "10.10.1", anthropic = "12.32.0", azureOpenAI = "2.1.0" },
        observations
    }, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine(status);
return status == "PassedProviderProtocolSpike" ? 0 : 1;

IChatClient Client(string provider, FixtureTransport transport, int? retries)
{
    var http = new HttpClient(transport, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    if (provider == "anthropic")
        return new OwnedClient(new AnthropicClient(new Anthropic.Core.ClientOptions
        {
            ApiKey = "fixture-not-a-real-credential", AuthToken = null,
            BaseUrl = "https://provider.fixture.invalid", HttpClient = http, MaxRetries = retries
        }).AsIChatClient("fixture-model"), http);
    var options = new AzureOpenAIClientOptions { Transport = new HttpClientPipelineTransport(http) };
    if (retries is { } maximum) options.RetryPolicy = new ClientRetryPolicy(maximum);
    return new OwnedClient(new AzureOpenAIClient(new Uri("https://provider.fixture.invalid"), new ApiKeyCredential("fixture-not-a-real-credential"), options)
        .GetChatClient("fixture-model").AsIChatClient(), http);
}

ChatClientAgentRunOptions Options() => new(new ChatOptions
{
    ModelId = "fixture-model", Temperature = 0.2f, MaxOutputTokens = 64,
    ResponseFormat = args.Contains("--drop-schema") ? null : ChatResponseFormat.ForJsonSchema(schema, "SpecialistResult")
});

async Task Success(string provider)
{
    using var transport = new FixtureTransport((_, request, _) => Task.FromResult(Response(provider, request, "success")));
    using var observer = new ObservingClient(Client(provider, transport, 0));
    var agent = new ChatClientAgent(observer, instructions: "Write the supplied brief.", name: "Copywriter");
    var session = await agent.CreateSessionAsync();
    var response = await agent.RunAsync("A fixture brief.", session, Options());
    Check(transport.Count == 1, provider + ": success makes one physical request");
    Check(JsonDocument.Parse(response.Text).RootElement.GetProperty("headline").GetString() == "Fixture", provider + ": JSON result returned");
    Check(response.Usage?.InputTokenCount == 11 && response.Usage?.OutputTokenCount == 7, provider + ": observable usage preserved");
    var request = transport.Requests.Single();
    var body = request.Body;
    if (provider == "anthropic") Check(body.GetProperty("model").GetString() == "fixture-model", "Anthropic exact model serialized");
    else Check(request.Path.Contains("fixture-model", StringComparison.Ordinal) || body.TryGetProperty("model", out var model) && model.GetString() == "fixture-model", "Azure exact deployment serialized");
    Check(body.TryGetProperty(provider == "anthropic" ? "output_config" : "response_format", out _), provider + ": structured schema is present");
    var actualSchema = provider == "anthropic"
        ? body.GetProperty("output_config").GetProperty("format").GetProperty("schema")
        : body.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema");
    Check(JsonElement.DeepEquals(schema, actualSchema), provider + ": exact dynamic schema serialized");
    var maximum = body.TryGetProperty("max_tokens", out var tokens) ? tokens : body.GetProperty("max_completion_tokens");
    Check(maximum.GetInt32() == 64, provider + ": output limit serialized");
    Check(Math.Abs(body.GetProperty("temperature").GetDouble() - 0.2) < 0.0001, provider + ": supported option serialized");
    var state = await agent.SerializeSessionAsync(session);
    observations.Add(new { name = provider + "-success", transportRequests = transport.Count, transportEntries = transport.Entries, requestPath = request.Path,
        schemaForwarded = true, inputTokens = response.Usage?.InputTokenCount, outputTokens = response.Usage?.OutputTokenCount,
        responseContentTypes = observer.ContentTypes, sessionContainsVisibleResult = state.GetRawText().Contains("Fixture", StringComparison.Ordinal) });
}

async Task Retry(string provider, bool defaultPolicy)
{
    using var transport = new FixtureTransport((_, request, _) => Task.FromResult(Response(provider, request, "retry")));
    using var client = Client(provider, transport, defaultPolicy || args.Contains("--enable-retries") ? null : 0);
    var agent = new ChatClientAgent(client, name: "Copywriter");
    Exception? failure = null;
    try { await agent.RunAsync("A fixture brief.", options: Options()); }
    catch (Exception exception) { failure = exception; }
    Check(failure is not null, provider + ": retryable error is surfaced");
    Check(defaultPolicy ? transport.Count > 1 : transport.Count == 1, provider + (defaultPolicy ? ": default retries are observed" : ": disabled retries make one transport request"));
    observations.Add(new { name = provider + (defaultPolicy ? "-default-retry" : "-no-retry"), transportRequests = transport.Count, transportEntries = transport.Entries, errorType = failure!.GetType().FullName });
}

async Task Loss(string provider)
{
    using var transport = new FixtureTransport((_, _, _) => throw new HttpRequestException("Controlled response loss after request observation."));
    using var client = Client(provider, transport, 0);
    var agent = new ChatClientAgent(client, name: "Copywriter");
    Exception? failure = null;
    try { await agent.RunAsync("A fixture brief.", options: Options()); }
    catch (Exception exception) { failure = exception; }
    Check(failure is not null && transport.Count == 1, provider + ": response loss is one observed request, never no-effect evidence");
    observations.Add(new { name = provider + "-response-loss", transportRequests = transport.Count, transportEntries = transport.Entries, errorType = failure!.GetType().FullName, outcome = "unknown-provider-effect" });
}

async Task Cancellation(string provider, bool beforeSend)
{
    using var cancellation = new CancellationTokenSource();
    using var transport = new FixtureTransport(async (_, _, token) =>
    {
        if (beforeSend) return Response(provider, new HttpRequestMessage(), "success");
        await cancellation.CancelAsync();
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new ProbeFailure("Cancellation fixture unexpectedly continued.");
    });
    using var client = Client(provider, transport, 0);
    var agent = new ChatClientAgent(client, name: "Copywriter");
    if (beforeSend) await cancellation.CancelAsync();
    Exception? failure = null;
    try { await agent.RunAsync("A fixture brief.", options: Options(), cancellationToken: args.Contains("--drop-cancellation") ? CancellationToken.None : cancellation.Token); }
    catch (Exception exception) { failure = exception; }
    Check(failure is OperationCanceledException, provider + ": cancellation propagates");
    Check(transport.Count == (beforeSend ? 0 : 1), provider + ": cancellation physical boundary is explicit");
    observations.Add(new { name = provider + (beforeSend ? "-cancel-before" : "-cancel-after"), transportRequests = transport.Count, transportEntries = transport.Entries,
        outcome = beforeSend ? "fixture-rejected-cancelled-entry" : "unknown-provider-effect", errorType = failure!.GetType().FullName });
}

async Task RichContent()
{
    using var transport = new FixtureTransport((_, request, _) => Task.FromResult(Response("anthropic", request, "thinking")));
    using var observer = new ObservingClient(Client("anthropic", transport, 0));
    var agent = new ChatClientAgent(observer, name: "Copywriter");
    var session = await agent.CreateSessionAsync();
    var response = await agent.RunAsync("A fixture brief.", session, Options());
    var businessText = args.Contains("--flatten-reasoning") ? string.Concat(observer.Last!.Messages.SelectMany(message => message.Contents).Select(content => content.ToString())) : response.Text;
    Check(!businessText.Contains("fixture-private-reasoning", StringComparison.Ordinal), "private reasoning is not business result text");
    var state = await agent.SerializeSessionAsync(session);
    Check(observer.ContentTypes.SequenceEqual(new[] { "TextReasoningContent", "TextContent" }), "actual Anthropic reasoning and visible content types observed");
    Check(state.GetRawText().Contains("fixture-private-reasoning", StringComparison.Ordinal), "raw SDK session retains reasoning fixture");
    observations.Add(new { name = "anthropic-rich-content", transportRequests = transport.Count, transportEntries = transport.Entries, responseContentTypes = observer.ContentTypes,
        reasoningInBusinessText = false, rawSdkSessionContainsReasoning = state.GetRawText().Contains("fixture-private-reasoning", StringComparison.Ordinal),
        disposition = "Production codec must select approved visible content; raw SDK session is not the persistence contract." });
}

async Task ResultBoundary(string provider, string mode)
{
    using var transport = new FixtureTransport((_, request, _) => Task.FromResult(Response(provider, request, mode)));
    using var observer = new ObservingClient(Client(provider, transport, 0));
    var agent = new ChatClientAgent(observer, name: "Copywriter");
    var response = await agent.RunAsync("A fixture brief.", options: Options());
    Check(transport.Count == 1, provider + ": result boundary has one request");
    observations.Add(new { name = provider + "-" + mode, transportRequests = transport.Count,
        finishReason = observer.Last!.FinishReason?.Value, responseContentTypes = observer.ContentTypes,
        sdkReturnedNormally = true, visibleTextLength = response.Text.Length,
        disposition = "SDK transport success is not acceptance of the specialist business result." });
    if (mode == "refusal") Check(provider == "anthropic" ? observer.Last.FinishReason == ChatFinishReason.ContentFilter : observer.Last.Messages.SelectMany(message => message.Contents).OfType<ErrorContent>().Any(content => content.ErrorCode == "Refusal"), provider + ": refusal signal preserved");
    if (mode == "invalid") Check(response.Text == "fixture invalid business result", provider + ": malformed business output is returned unvalidated");
    if (mode == "truncated") Check(observer.Last.FinishReason == ChatFinishReason.Length, provider + ": truncation signal preserved even with syntactically valid JSON");
}

async Task TokenRefresh()
{
    using var transport = new FixtureTransport((_, request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
        RequestMessage = request,
        Content = new StringContent("{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"fixture\"}}", Encoding.UTF8, "application/json")
    }));
    using var http = new HttpClient(transport, disposeHandler: false);
    using var credentials = new FixtureTokenProvider();
    using var client = new AnthropicClient(new Anthropic.Core.ClientOptions
    {
        ApiKey = null, AuthToken = null, Credentials = credentials, BaseUrl = "https://provider.fixture.invalid",
        HttpClient = http, MaxRetries = 0
    }).AsIChatClient("fixture-model");
    var agent = new ChatClientAgent(client, name: "Copywriter");
    Exception? failure = null;
    try { await agent.RunAsync("A fixture brief.", options: Options()); }
    catch (Exception exception) { failure = exception; }
    Check(failure is not null && transport.Count == 2 && credentials.Refreshes == 1,
        "Anthropic token refresh independently retries once even with MaxRetries zero");
    observations.Add(new { name = "anthropic-token-refresh", transportRequests = transport.Count,
        refreshes = credentials.Refreshes, maxRetries = 0,
        disposition = "Refreshing-token profile is not admitted by a one-request budget without a separate dispatch guard." });
}

static HttpResponseMessage Response(string provider, HttpRequestMessage request, string mode)
{
    var retry = mode == "retry";
    var text = mode == "invalid" ? "fixture invalid business result" : mode == "refusal" ? "Fixture refusal" : "{\"headline\":\"Fixture\"}";
    object body;
    if (retry)
        body = provider == "anthropic" ? new { type = "error", error = new { type = "rate_limit_error", message = "fixture" } }
            : new { error = new { type = "rate_limit_error", code = "rate_limit_exceeded", message = "fixture" } };
    else if (provider == "anthropic")
    {
        var content = new List<object>();
        if (mode == "thinking") content.Add(new { type = "thinking", thinking = "fixture-private-reasoning", signature = "fixture-signature" });
        content.Add(new { type = "text", text = text });
        body = new { id = "msg-fixture", type = "message", role = "assistant", model = "fixture-model", content,
            stop_reason = mode == "refusal" ? "refusal" : mode == "truncated" ? "max_tokens" : "end_turn", stop_sequence = (string?)null, usage = new { input_tokens = 11, output_tokens = 7 } };
    }
    else body = new { id = "chatcmpl-fixture", @object = "chat.completion", created = 1700000000, model = "fixture-model",
        choices = new[] { new { index = 0, message = new { role = "assistant", content = mode == "refusal" ? null : text, refusal = mode == "refusal" ? "Fixture refusal" : null }, finish_reason = mode == "truncated" ? "length" : "stop" } },
        usage = new { prompt_tokens = 11, completion_tokens = 7, total_tokens = 18 } };
    var response = new HttpResponseMessage(retry ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK)
    { RequestMessage = request, Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    if (retry) response.Headers.TryAddWithoutValidation("retry-after-ms", "1");
    return response;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new ProbeFailure("Probe failed: " + message);
}

sealed class ProbeFailure(string message) : Exception(message);

sealed class FixtureTransport(Func<int, HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Count { get; private set; }
    public int Entries { get; private set; }
    public List<(string Path, JsonElement Body)> Requests { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host != "provider.fixture.invalid") throw new ProbeFailure("Unexpected provider endpoint.");
        Entries++;
        cancellationToken.ThrowIfCancellationRequested();
        var count = ++Count;
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri.AbsolutePath, JsonDocument.Parse(body).RootElement.Clone()));
        return await respond(count, request, cancellationToken);
    }
}

sealed class ObservingClient(IChatClient inner) : DelegatingChatClient(inner)
{
    public string[] ContentTypes { get; private set; } = [];
    public ChatResponse? Last { get; private set; }
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        Last = response;
        ContentTypes = response.Messages.SelectMany(message => message.Contents).Select(content => content.GetType().Name).ToArray();
        return response;
    }
}

sealed class FixtureTokenProvider : IAccessTokenProvider
{
    public int Refreshes { get; private set; }
    public ValueTask<AccessToken> GetTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (forceRefresh) Refreshes++;
        return ValueTask.FromResult(new AccessToken("fixture-token-" + Refreshes));
    }
    public void Dispose() { }
}

sealed class OwnedClient(IChatClient inner, HttpClient http) : DelegatingChatClient(inner)
{
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) http.Dispose();
    }
}
