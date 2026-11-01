using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Anthropic;
using Elsa.Agent.Specialists.AzureOpenAI;
using Elsa.Agent.Specialists.Connections;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore;
using Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;
using Elsa.Agent.Specialists.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Agent.Specialists.Tests.Providers.SpecialistConnectionProviderTests;

namespace Elsa.Agent.Specialists.Tests.Providers;

public sealed partial class SpecialistProviderSocketTests
{
    public static IEnumerable<object[]> StreamingCases() =>
        from vendor in new[] { "anthropic", "azure-openai" }
        from mode in new[] { "success", "thinking", "missing-terminal", "missing-stop", "pause", "refusal", "truncated", "invalid", "oversized", "overrun", "loss", "cancel-after", "cancel-before", "retry" }
        where vendor == "anthropic" || mode is not ("thinking" or "missing-stop" or "pause")
        from sqlite in new[] { false, true }
        select new object[] { vendor, mode, sqlite };

    [Theory]
    [MemberData(nameof(StreamingCases))]
    public async Task Streaming_requires_complete_visible_result_and_preserves_reserved_uncertainty(string vendor, string mode, bool sqlite)
    {
        using var certificate = Certificate();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var server = await Server.StartAsync(certificate, async context =>
        {
            if (mode == "retry")
            {
                context.Response.StatusCode = 429;
                await context.Response.WriteAsync("{\"error\":{\"type\":\"rate_limit_error\",\"code\":\"rate_limit_exceeded\",\"message\":\"fixture\"}}");
                return;
            }
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(StreamingEvents(vendor, mode));
            await context.Response.Body.FlushAsync();
            if (mode == "loss") context.Abort();
            if (mode == "cancel-after")
            {
                await cancellation.CancelAsync();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                catch (OperationCanceledException) { }
            }
        });
        var connection = new Resolved(vendor);
        connection.Values["endpoint"] = server.Address;
        connection.Hosts = Elsa.Connections.Core.Models.AllowedHostsPolicy.Fixed("127.0.0.1");
        SpecialistConnectionModelProvider provider = vendor == "anthropic"
            ? new AnthropicSpecialistModelProvider(new Resolver(connection), new Access()) : new AzureOpenAISpecialistModelProvider(new Resolver(connection), new Access());
        provider.ConfigureTransport = handler => Trust(handler, certificate);
        var descriptor = Request(Model(vendor), await provider.GetCapabilitiesAsync(Model(vendor))).Descriptor with { AllowContinuity = true };
        var contract = new ActivityContract("specialist", "1.0.0", SpecialistRuntimeIds.ConsumerKey, JsonSerializer.SerializeToElement(descriptor), [],
            new(new("System.Text.Json.JsonElement"), true, ActivityValuePolicy.Default, []), ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.FacadeCapability));
        var services = new ServiceCollection();
        new SecretsFeature { EncryptionKey = "fixture-streaming-protection" }.ConfigureServices(services);
        await using var protection = services.BuildServiceProvider();
        var codec = new SpecialistSessionCodec(protection.GetRequiredService<ISecretValueProtector>(), new Access());
        var runner = new SpecialistModelRunner([provider], new SpecialistSchemaValidator(), codec, new(true));
        var directory = Path.Combine(Path.GetTempPath(), "elsa-specialist-stream-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connectionString = "Data Source=" + Path.Combine(directory, "journal.db") + ";Pooling=False";
        try
        {
            await using var context = new SpecialistsSqliteDbContext(new DbContextOptionsBuilder<SpecialistsSqliteDbContext>().UseSqlite(connectionString).Options);
            if (sqlite) await context.Database.EnsureCreatedAsync();
            ISpecialistTurnStore store = sqlite ? new EfSpecialistTurnStore(context, new Access()) : new InMemorySpecialistTurnStore(new Access(), new());
            var journal = new SpecialistTurnJournal(store, protection.GetRequiredService<ISecretValueProtector>(), new Access());
            var request = new SpecialistTurnRequest(Identity(), descriptor, contract, SpecialistConnectionProviderTests.Json("{}"), "bindings");
            var now = DateTimeOffset.UtcNow;
            var handle = await journal.StageAsync(request, now, CancellationToken.None);
            var claim = await journal.ClaimAsync(handle, request.Identity, "child", now, CancellationToken.None);
            request = await journal.PrepareSessionAsync(claim, request, CancellationToken.None);
            Assert.True(await journal.DispatchAsync(claim, now, CancellationToken.None));
            var authority = new SpecialistTurnBudgetAllocator(store, TimeProvider.System).Create(claim, request);
            if (mode == "cancel-before") await cancellation.CancelAsync();
            SpecialistTurnResult? result = null;
            var error = await Record.ExceptionAsync(async () => result = await runner.RunAsync(request, cancellation.Token, authority));
            if (mode is "success" or "thinking")
            {
                Assert.Null(error);
                Assert.NotNull(result);
                Assert.Equal("Fixture", result.Result.GetProperty("headline").GetString());
                Assert.Equal(11, result.InputTokens);
                Assert.Equal(7, result.OutputTokens);
                Assert.True(await journal.AcceptAsync(claim, result, DateTimeOffset.UtcNow, CancellationToken.None));
                var replay = await journal.ResultAsync(handle, request.Identity, "child", CancellationToken.None);
                var history = codec.Restore(replay.Session!, request.Identity);
                Assert.Equal(new[] { Microsoft.Extensions.AI.ChatRole.User, Microsoft.Extensions.AI.ChatRole.Assistant }, history.Select(m => m.Role));
                Assert.Equal("{\"headline\":\"Fixture\"}", history[1].Text);
                Assert.DoesNotContain("fixture-private-reasoning", protection.GetRequiredService<ISecretValueProtector>().Unprotect(replay.Session!.ProtectedValue), StringComparison.Ordinal);
            }
            else
            {
                Assert.NotNull(error);
                Assert.Null(result);
                var code = mode switch
                {
                    "refusal" => "specialist.result.refused", "truncated" => "specialist.result.truncated", "pause" or "missing-stop" => "specialist.result.incomplete",
                    "missing-terminal" => "specialist.result.incomplete",
                    "invalid" => "specialist.result.json", "oversized" => "specialist.result.bounds", "overrun" => "specialist.budget.output-overrun", _ => null
                };
                if (code is not null) Assert.Equal(code, Assert.IsType<SpecialistTurnException>(error).Code);
                if (mode is "cancel-before" or "cancel-after") Assert.IsAssignableFrom<OperationCanceledException>(error);
                // Exercise the same conservative post-dispatch failure transition as the native worker.
                await journal.FailAsync(claim, "fixture.stream-interrupted", error is not SpecialistTurnException || ((SpecialistTurnException)error).OutcomeUnknown, CancellationToken.None);
            }
            var observed = (await store.ReadAsync(handle, request.Identity, CancellationToken.None))!;
            if (mode == "cancel-before") Assert.Empty(observed.ModelRequests ?? []);
            else Assert.Equal(64, Assert.Single(observed.ModelRequests!).MaximumOutputTokens);
            Assert.Equal(mode == "cancel-before" ? 0 : 1, server.Requests.Count);
            if (mode is not ("success" or "thinking"))
            {
                Assert.Equal(error is not SpecialistTurnException || ((SpecialistTurnException)error).OutcomeUnknown ? SpecialistTurnStatus.OutcomeUnknown : SpecialistTurnStatus.Failed, observed.Status);
                Assert.Null(observed.ProtectedResult);
                Assert.Equal(0, (await store.ReadConversationAsync(handle, request.Identity, CancellationToken.None))!.Revision);
                Assert.Null(await store.ClaimAsync(handle, request.Identity, "second-child", "second-fence", DateTimeOffset.UtcNow, CancellationToken.None));
            }
            else Assert.Equal(SpecialistTurnStatus.Accepted, observed.Status);
            if (mode != "cancel-before") Assert.True(Assert.Single(server.Requests).Body.GetProperty("stream").GetBoolean());
        }
        finally { Directory.Delete(directory, true); }
    }

    private static string StreamingEvents(string vendor, string mode)
    {
        var events = new List<string>();
        void Add(string name, object value) => events.Add("event: " + name + "\ndata: " + JsonSerializer.Serialize(value) + "\n\n");
        if (vendor == "anthropic")
        {
            Add("message_start", new { type = "message_start", message = new { id = "fixture-message", type = "message", role = "assistant", model = "fixture-model",
                content = Array.Empty<object>(), stop_reason = (string?)null, stop_sequence = (string?)null, usage = new { input_tokens = 11, output_tokens = 0 } } });
            if (mode == "thinking")
            {
                Add("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "thinking", thinking = "" } });
                Add("content_block_delta", new { type = "content_block_delta", index = 0, delta = new { type = "thinking_delta", thinking = "fixture-private-reasoning" } });
                Add("content_block_stop", new { type = "content_block_stop", index = 0 });
            }
            var index = mode == "thinking" ? 1 : 0;
            Add("content_block_start", new { type = "content_block_start", index, content_block = new { type = "text", text = "" } });
            foreach (var text in mode == "oversized" ? new[] { "{\"headline\":\"", new string('x', 1_048_576), "\"}" } : mode == "invalid" ? new[] { "fixture invalid JSON" } : new[] { "{\"headline\":", "\"Fixture\"}" }) Add("content_block_delta", new { type = "content_block_delta", index, delta = new { type = "text_delta", text } });
            Add("content_block_stop", new { type = "content_block_stop", index });
            if (mode is not ("missing-terminal" or "loss" or "cancel-after"))
            {
                Add("message_delta", new { type = "message_delta", delta = new { stop_reason = mode switch { "truncated" => "max_tokens", "refusal" => "refusal", "pause" => "pause_turn", "unknown" => "fixture_unknown", _ => "end_turn" }, stop_sequence = (string?)null }, usage = new { output_tokens = mode == "overrun" ? 65 : 7 } });
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
            foreach (var text in mode == "oversized" ? new[] { "{\"headline\":\"", new string('x', 1_048_576), "\"}" } : mode == "invalid" ? new[] { "fixture invalid JSON" } : new[] { "{\"headline\":", "\"Fixture\"}" }) Chunk(new { content = text });
            if (mode == "refusal") Chunk(new { refusal = "Fixture refusal." });
            if (mode is not ("missing-terminal" or "loss" or "cancel-after"))
            {
                Chunk(new { }, mode == "truncated" ? "length" : mode == "unknown" ? "fixture_unknown" : "stop");
                events.Add("data: " + JsonSerializer.Serialize(new { id = "fixture-message", @object = "chat.completion.chunk", created = 1700000000, model = "fixture-model",
                    choices = Array.Empty<object>(), usage = new { prompt_tokens = 11, completion_tokens = mode == "overrun" ? 65 : 7, total_tokens = 18 } }) + "\n\n");
                events.Add("data: [DONE]\n\n");
            }
        }
        return string.Concat(events);
    }
}
