using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Elsa.Agent.Specialists.Anthropic;
using Elsa.Agent.Specialists.AzureOpenAI;
using Elsa.Agent.Specialists.Connections;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Agent.Specialists.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Connections.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Xunit;
using static Elsa.Agent.Specialists.Tests.Providers.SpecialistConnectionProviderTests;

namespace Elsa.Agent.Specialists.Tests.Providers;

public sealed class SpecialistProviderSocketTests
{
    [Theory]
    [InlineData("anthropic", "success", null)]
    [InlineData("azure-openai", "success", null)]
    [InlineData("anthropic", "refusal", "specialist.result.refused")]
    [InlineData("azure-openai", "refusal", "specialist.result.refused")]
    [InlineData("anthropic", "truncated", "specialist.result.truncated")]
    [InlineData("azure-openai", "truncated", "specialist.result.truncated")]
    [InlineData("anthropic", "thinking", null)]
    [InlineData("anthropic", "pause", "specialist.result.content")]
    public async Task Production_acceptance_checks_provider_finish_and_visible_result(string vendor, string mode, string? refusal)
    {
        using var certificate = Certificate();
        await using var server = await Server.StartAsync(certificate, context => Success(context, vendor, mode));
        var connection = new Resolved(vendor);
        connection.Values["endpoint"] = server.Address;
        connection.Hosts = Elsa.Connections.Core.Models.AllowedHostsPolicy.Fixed("127.0.0.1");
        var resolver = new Resolver(connection);
        SpecialistConnectionModelProvider provider = vendor == "anthropic"
            ? new AnthropicSpecialistModelProvider(resolver, new Access()) : new AzureOpenAISpecialistModelProvider(resolver, new Access());
        provider.ConfigureTransport = handler => Trust(handler, certificate);
        var descriptor = Request(Model(vendor), await provider.GetCapabilitiesAsync(Model(vendor))).Descriptor;
        var contract = new ActivityContract("specialist", "1.0.0", SpecialistRuntimeIds.ConsumerKey, JsonSerializer.SerializeToElement(descriptor), [],
            new(new("System.Text.Json.JsonElement"), true, ActivityValuePolicy.Default, []), ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.FacadeCapability));
        var runner = new SpecialistModelRunner([provider], new SpecialistSchemaValidator());
        var request = new SpecialistTurnRequest(Identity(), descriptor, contract, SpecialistConnectionProviderTests.Json("{}"), "bindings");
        if (refusal is not null)
        {
            var error = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await runner.RunAsync(request, CancellationToken.None));
            Assert.Equal(refusal, error.Code);
        }
        else
        {
            var result = await runner.RunAsync(request, CancellationToken.None);
            Assert.Equal("Fixture", result.Result.GetProperty("headline").GetString());
            Assert.Equal(11, result.InputTokens);
            Assert.Equal(7, result.OutputTokens);
            Assert.DoesNotContain("fixture-private-reasoning", JsonSerializer.Serialize(result), StringComparison.Ordinal);
        }
        Assert.Single(server.Requests);
    }

    public static IEnumerable<object[]> Cases() =>
        from vendor in new[] { "anthropic", "azure-openai" }
        from mode in new[] { "success", "retry", "redirect", "loss", "cancel-before", "cancel-after", "refusal", "truncated", "revoke-after-open", "revise-after-open", "recreate-after-open", "credential-revoke", "credential-rotate" }
        select new object[] { vendor, mode };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Actual_contributor_and_MAF_use_the_authorized_primary_transport(string vendor, string mode)
    {
        using var certificate = Certificate();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var target = await Server.StartAsync(certificate, context => Success(context, vendor, "success"));
        await using var origin = await Server.StartAsync(certificate, async context =>
        {
            if (mode == "retry")
            {
                context.Response.StatusCode = 429;
                context.Response.Headers["retry-after-ms"] = "1";
                await context.Response.WriteAsync("{\"error\":{\"type\":\"rate_limit_error\",\"code\":\"rate_limit_exceeded\",\"message\":\"fixture\"}}");
            }
            else if (mode == "redirect")
            {
                context.Response.StatusCode = 307;
                context.Response.Headers.Location = target.Address + "/redirected";
            }
            else if (mode == "loss") context.Abort();
            else if (mode == "cancel-after")
            {
                cancellation.Cancel();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); } catch (OperationCanceledException) { }
            }
            else await Success(context, vendor, mode);
        });
        var connection = new Resolved(vendor);
        connection.Values["endpoint"] = origin.Address;
        connection.Hosts = Elsa.Connections.Core.Models.AllowedHostsPolicy.Fixed("127.0.0.1");
        var resolver = new Resolver(connection);
        SpecialistConnectionModelProvider provider = vendor == "anthropic"
            ? new AnthropicSpecialistModelProvider(resolver, new Access()) : new AzureOpenAISpecialistModelProvider(resolver, new Access());
        provider.ConfigureTransport = handler => Trust(handler, certificate);
        var metadata = await provider.GetCapabilitiesAsync(Model(vendor), cancellation.Token);
        await using var lease = await provider.OpenAsync(Request(Model(vendor), metadata), cancellation.Token);
        if (mode == "cancel-before") cancellation.Cancel();
        if (mode == "revoke-after-open") connection.Status = "Revoked";
        if (mode == "revise-after-open") connection.Version++;
        if (mode == "recreate-after-open") connection.IncarnationId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        if (mode == "credential-revoke") connection.CredentialUnavailable = true;
        if (mode == "credential-rotate") connection.Credential = "fixture-new-credential";
        var observer = new ObservingClient(lease.Client);
        var agent = new ChatClientAgent(observer, instructions: "Write a fixture headline.");
        var options = lease.Options.Clone();
        options.ModelId = "fixture-model";
        options.MaxOutputTokens = 64;
        options.ResponseFormat = ChatResponseFormat.ForJsonSchema(SpecialistConnectionProviderTests.Schema, "specialist_result");
        AgentResponse? result = null;
        var error = await Record.ExceptionAsync(async () => result = await agent.RunAsync("Fixture brief.", options: new ChatClientAgentRunOptions(options), cancellationToken: cancellation.Token));
        if (mode is "cancel-before" or "cancel-after") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.False(cancellation.IsCancellationRequested);
        if (mode is "cancel-before" or "revoke-after-open" or "revise-after-open" or "recreate-after-open" or "credential-revoke" or "credential-rotate")
        {
            Assert.NotNull(error);
            Assert.Empty(origin.Requests);
        }
        else
        {
            var actual = Assert.Single(origin.Requests);
            Assert.Equal("HTTP/1.1", actual.Protocol);
            Assert.Equal("fixture-not-a-real-credential", actual.ApiKey);
            Assert.Null(actual.Authorization);
            Assert.True(actual.Body.TryGetProperty(vendor == "anthropic" ? "output_config" : "response_format", out _));
            Assert.Equal(64, actual.Body.GetProperty(vendor == "anthropic" ? "max_tokens" : "max_completion_tokens").GetInt32());
            Assert.Equal(0.2, actual.Body.GetProperty("temperature").GetDouble(), precision: 6);
            if (vendor == "anthropic") Assert.Equal("fixture-model", actual.Body.GetProperty("model").GetString());
            else Assert.Contains("fixture-model", actual.Path, StringComparison.Ordinal);
            if (mode is "success" or "refusal" or "truncated")
            {
                Assert.Null(error);
                Assert.NotNull(result);
                Assert.Equal(11, result.Usage!.InputTokenCount);
                Assert.Equal(7, result.Usage.OutputTokenCount);
                if (mode == "success") Assert.Equal("Fixture", SpecialistConnectionProviderTests.Json(result.Text).GetProperty("headline").GetString());
                // SDK finish/content evidence is checked again by the production result acceptance path.
                if (mode == "truncated") Assert.Equal(ChatFinishReason.Length, observer.Last!.FinishReason);
                if (mode == "refusal") Assert.True(observer.Last!.FinishReason == ChatFinishReason.ContentFilter ||
                    observer.Last.Messages.SelectMany(x => x.Contents).Any(x => x is ErrorContent));
            }
            else Assert.NotNull(error);
        }
        Assert.Empty(target.Requests);
    }

    private sealed class ObservingClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        internal ChatResponse? Last { get; private set; }
        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Last = await base.GetResponseAsync(messages, options, cancellationToken);
            return Last;
        }
    }

    private static async Task Success(HttpContext context, string vendor, string mode)
    {
        context.Response.ContentType = "application/json";
        var content = new List<object>();
        if (mode == "thinking") content.Add(new { type = "thinking", thinking = "fixture-private-reasoning", signature = "fixture-signature" });
        content.Add(new { type = "text", text = "{\"headline\":\"Fixture\"}" });
        object body = vendor == "anthropic"
            ? new { id = "msg-fixture", type = "message", role = "assistant", model = "fixture-model", content, stop_reason = mode == "refusal" ? "refusal" : mode == "truncated" ? "max_tokens" : mode == "pause" ? "pause_turn" : "end_turn", stop_sequence = (string?)null, usage = new { input_tokens = 11, output_tokens = 7 } }
            : new { id = "chatcmpl-fixture", @object = "chat.completion", created = 1700000000, model = "fixture-model", choices = new[] { new { index = 0, message = new { role = "assistant", content = mode == "refusal" ? null : "{\"headline\":\"Fixture\"}", refusal = mode == "refusal" ? "Fixture refusal" : null }, finish_reason = mode == "truncated" ? "length" : "stop" } }, usage = new { prompt_tokens = 11, completion_tokens = 7, total_tokens = 18 } };
        await context.Response.WriteAsync(JsonSerializer.Serialize(body));
    }

    private static X509Certificate2 Certificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback); names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
    }

    // Retain the actual product primary handler and every retry/redirect/proxy/cookie option. Only
    // trust for this fixture's owned self-signed leaf is added; no OS trust or external endpoint is used.
    private static HttpMessageHandler Trust(HttpMessageHandler primary, X509Certificate2 certificate)
    {
        var handler = Assert.IsType<SocketsHttpHandler>(primary);
        handler.SslOptions.RemoteCertificateValidationCallback = (_, actual, _, errors) => actual is not null &&
            (errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0 && actual.GetCertHashString(HashAlgorithmName.SHA256) == certificate.GetCertHashString(HashAlgorithmName.SHA256);
        return handler;
    }
    private sealed record Observed(string Protocol, string Path, JsonElement Body, string? ApiKey, string? Authorization);
    private sealed class Server(WebApplication app) : IAsyncDisposable
    {
        internal ConcurrentQueue<Observed> Requests { get; } = new();
        internal string Address => app.Urls.Single();
        internal static async Task<Server> StartAsync(X509Certificate2 certificate, Func<HttpContext, Task> response)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Test" });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, endpoint => { endpoint.Protocols = HttpProtocols.Http1; endpoint.UseHttps(certificate); }));
            var application = builder.Build();
            var server = new Server(application);
            application.Run(async context =>
            {
                if (context.Request.Method != "POST") { context.Response.StatusCode = 405; return; }
                using var reader = new StreamReader(context.Request.Body);
                var body = SpecialistConnectionProviderTests.Json(await reader.ReadToEndAsync(context.RequestAborted));
                server.Requests.Enqueue(new(context.Request.Protocol, context.Request.Path, body,
                    context.Request.Headers["x-api-key"].FirstOrDefault() ?? context.Request.Headers["api-key"].FirstOrDefault(), context.Request.Headers.Authorization.FirstOrDefault()));
                await response(context);
            });
            await application.StartAsync();
            return server;
        }
        public async ValueTask DisposeAsync()
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await app.StopAsync(stop.Token);
            await app.DisposeAsync();
        }
    }
}
