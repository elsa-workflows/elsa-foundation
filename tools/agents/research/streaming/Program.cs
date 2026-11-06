using System.Net;
using System.Text;
using System.Text.Json;
using Anthropic;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.ClientModel;
using System.ClientModel.Primitives;

var output = args[0];
var observations = new List<object>();
var status = "InProgress";
try
{
    foreach (var provider in new[] { "anthropic", "azure" })
    foreach (var mode in new[] { "success", "missing-terminal", "length", "refusal", "pause", "reasoning", "unknown", "missing-stop" })
    {
        if (provider == "azure" && mode is "pause" or "missing-stop") continue;
        using var transport = new FixtureTransport(provider, mode);
        using var http = new HttpClient(transport);
        using IChatClient client = provider == "anthropic"
            ? new AnthropicClient(new Anthropic.Core.ClientOptions { ApiKey = "fixture-not-a-real-credential", AuthToken = null,
                BaseUrl = "https://provider.fixture.invalid", HttpClient = http, MaxRetries = 0 }).AsIChatClient("fixture-model")
            : new AzureOpenAIClient(new Uri("https://provider.fixture.invalid"), new ApiKeyCredential("fixture-not-a-real-credential"),
                new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21) { Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) })
                .GetChatClient("fixture-model").AsIChatClient();
        var updates = new List<ChatResponseUpdate>();
        Exception? failure = null;
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "Fixture brief.")], new()
            {
                MaxOutputTokens = 64,
                ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonSerializer.SerializeToElement(new
                {
                    type = "object", properties = new { headline = new { type = "string" } }, required = new[] { "headline" }, additionalProperties = false
                }), "SpecialistResult")
            })) updates.Add(update);
        }
        catch (Exception e) { failure = e; }
        var rawReason = updates.Select(u => u.RawRepresentation).OfType<Anthropic.Models.Messages.RawMessageStreamEvent>()
            .Where(e => e.TryPickDelta(out _)).Select(e => e.Json.GetProperty("delta").GetProperty("stop_reason").GetString()).LastOrDefault();
        var anthropicStop = updates.Select(u => u.RawRepresentation).OfType<Anthropic.Models.Messages.RawMessageStreamEvent>().Any(e => e.TryPickStop(out _));
        var azureRefusal = updates.Select(u => u.RawRepresentation).OfType<OpenAI.Chat.StreamingChatCompletionUpdate>().Any(e => !string.IsNullOrEmpty(e.RefusalUpdate));
        var terminal = provider == "anthropic" ? anthropicStop && rawReason is "end_turn" or "stop_sequence"
            : updates.Any(u => u.RawRepresentation is OpenAI.Chat.StreamingChatCompletionUpdate { FinishReason: OpenAI.Chat.ChatFinishReason.Stop });
        var safe = failure is null && terminal && !azureRefusal && !updates.Any(u => u.FinishReason is { } reason && reason != ChatFinishReason.Stop);
        if (args.Contains("--drop-terminal-guard")) safe = failure is null;
        var expected = mode is "success" or "reasoning";
        if (safe != expected) throw new InvalidOperationException(provider + ": " + mode + " terminal guard failed.");
        observations.Add(new { provider, mode, physicalRequests = transport.Count, sdkReturnedNormally = failure is null,
            sdkErrorType = failure?.GetType().FullName, visibleResultCorrect = string.Concat(updates.Select(u => u.Text)) == "{\"headline\":\"Fixture\"}",
            sdkFinishReasons = updates.Select(u => u.FinishReason?.Value).Where(x => x is not null).Distinct(),
            rawTerminalReason = rawReason, anthropicMessageStop = anthropicStop, azureRawRefusal = azureRefusal,
            containsReasoningContent = updates.Any(u => u.Contents.Any(c => c is TextReasoningContent)),
            terminalGuardAccepted = safe, usage = updates.SelectMany(u => u.Contents).OfType<UsageContent>().Select(u => new { u.Details.InputTokenCount, u.Details.OutputTokenCount, u.Details.TotalTokenCount }), contentTypes = updates.SelectMany(u => u.Contents).Select(c => c.GetType().Name).Distinct() });
        if (transport.Count != 1) throw new InvalidOperationException("Each stream must issue one physical request.");

    }
    status = "PassedStreamingProtocolSpike";
}
catch(Exception e) { status = "Failed"; Console.Error.WriteLine(e); }
finally
{
    File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { status, liveModel = false, inferenceCharge = false,
        boundary = "Pinned SDK injected SSE transport research; no sockets or Elsa runtime proof.", observations }, new JsonSerializerOptions { WriteIndented = true }));
}
Console.WriteLine(status);
return status == "PassedStreamingProtocolSpike" ? 0 : 1;

sealed class FixtureTransport(string provider, string mode) : HttpMessageHandler
{
    public int Count;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Count++;
        var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement;
        if (!body.GetProperty("stream").GetBoolean()) throw new InvalidOperationException("SDK did not request streaming.");
        var events = new List<string>();
        void Add(string name, object value) => events.Add("event: " + name + "\ndata: " + JsonSerializer.Serialize(value) + "\n\n");
        if (provider == "anthropic")
        {
            Add("message_start", new { type = "message_start", message = new { id = "fixture-message", type = "message", role = "assistant", model = "fixture-model",
                content = Array.Empty<object>(), stop_reason = (string?)null, stop_sequence = (string?)null, usage = new { input_tokens = 11, output_tokens = 0 } } });
            if (mode == "reasoning")
            {
                Add("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "thinking", thinking = "" } });
                Add("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "thinking_delta", thinking = "fixture-private-reasoning" } });
                Add("content_block_stop", new { type = "content_block_stop", index = 0 });
            }
            var index = mode == "reasoning" ? 1 : 0;
            Add("content_block_start", new { type = "content_block_start", index, content_block = new { type = "text", text = "" } });
            foreach (var text in new[] { "{\"headline\":", "\"Fixture\"}" }) Add("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "text_delta", text } });
            Add("content_block_stop", new { type = "content_block_stop", index });
            if (mode != "missing-terminal")
            {
                Add("message_delta", new { type = "message_delta", delta = new { stop_reason = mode switch { "length" => "max_tokens", "refusal" => "refusal", "pause" => "pause_turn", "unknown" => "fixture_unknown", _ => "end_turn" }, stop_sequence = (string?)null }, usage = new { output_tokens = 7 } });
                if (mode != "missing-stop") Add("message_stop", new { type = "message_stop" });
            }
        }
        else
        {
            void Chunk(object delta, string? finish = null, object? usage = null) => events.Add("data: " + JsonSerializer.Serialize(new
            {
                id = "fixture-message", @object = "chat.completion.chunk", created = 1700000000, model = "fixture-model",
                choices = new[] { new { index = 0, delta, finish_reason = finish } }, usage
            }) + "\n\n");
            Chunk(new { role = "assistant", content = "" });
            foreach (var text in new[] { "{\"headline\":", "\"Fixture\"}" }) Chunk(new { content = text });
            if (mode == "refusal") Chunk(new { refusal = "Fixture refusal." });
            if (mode != "missing-terminal")
            {
                Chunk(new { }, mode == "length" ? "length" : mode == "unknown" ? "fixture_unknown" : "stop");
                events.Add("data: " + JsonSerializer.Serialize(new { id = "fixture-message", @object = "chat.completion.chunk", created = 1700000000, model = "fixture-model",
                    choices = Array.Empty<object>(), usage = new { prompt_tokens = 11, completion_tokens = 7, total_tokens = 18 } }) + "\n\n");
                events.Add("data: [DONE]\n\n");
            }
        }
        var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request,
            Content = new StringContent(string.Concat(events), Encoding.UTF8, "text/event-stream") };
        return response;
    }
}
