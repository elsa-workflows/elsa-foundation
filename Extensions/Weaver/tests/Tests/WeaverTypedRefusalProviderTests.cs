using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Weaver.Anthropic.Options;
using Elsa.Weaver.Anthropic.Services;
using Elsa.Weaver.Api;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.GitHubCopilot.Options;
using Elsa.Weaver.GitHubCopilot.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Weaver.Tests;

/// <summary>Real adapters/orchestrator/invoker; only their external SDK transports are deterministic fixtures.</summary>
public sealed class WeaverTypedRefusalProviderTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Both_real_adapters_keep_normalized_refusal_classification_in_native_results_and_retained_history(
        bool copilot, bool preparation, bool unavailable)
    {
        var canary = Guid.NewGuid().ToString("N");
        var tool = new RefusingTool(canary, preparation, unavailable);
        var transport = new Transport(tool.Descriptor.Name);
        using var root = WeaverTestServices.Create(services =>
        {
            new WeaverEffectsFeature().ConfigureServices(services);
            services.AddScoped<IWeaverTool>(_ => tool);
            if (copilot)
                services.AddScoped<IWeaverProvider>(provider => new WeaverGitHubCopilotProvider(transport,
                    Options.Create(new WeaverGitHubCopilotOptions { Enabled = true, GitHubToken = Guid.NewGuid().ToString("N"),
                        GitHubTokenEnvironmentVariable = null, UseLoggedInUser = false }),
                    provider.GetRequiredService<IWeaverToolInvoker>(), NullLogger<WeaverGitHubCopilotProvider>.Instance));
            else services.AddSingleton<IWeaverProvider>(new WeaverAnthropicProvider(transport,
                Options.Create(new WeaverAnthropicOptions { Enabled = true, ApiKey = Guid.NewGuid().ToString("N"),
                    ApiKeyEnvironmentVariable = null }), NullLogger<WeaverAnthropicProvider>.Instance));
        });
        string sessionId;
        WeaverToolResult result;
        using (var scope = root.CreateScope())
        {
            var services = scope.ServiceProvider;
            var sessions = services.GetRequiredService<IWeaverSessionService>();
            var session = await sessions.CreateAsync(new("tenant", "actor", "conversation",
                copilot ? WeaverGitHubCopilotProvider.Id : WeaverAnthropicProvider.Id, "explain", null,
                WeaverPolicy.Default, new Dictionary<string, string>()));
            sessionId = session.Id;
            await sessions.AddMessageAsync(session.Id, new(WeaverRole.User, "Inspect the owned resource.", WeaverMessageStatus.Pending, null, [], []));
            var events = new List<WeaverStreamEvent>();
            await foreach (var item in services.GetRequiredService<IWeaverStreamingService>().StreamAsync(session.Id)) events.Add(item);
            Assert.DoesNotContain(events, item => item.Kind == WeaverStreamEventKind.Error);
            result = Assert.IsType<WeaverToolResult>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ContributedResult).Payload);
            Assert.False(result.Succeeded);
            Assert.Null(result.Access); Assert.Null(result.Payload); Assert.Null(result.Receipt);
            Assert.Equal(unavailable ? "module.owner.unavailable" : "module.code.unsupported", result.Error!.Code);
            Assert.Equal(unavailable, result.Error.OwnerUnavailable);
            Assert.Equal(unavailable ? 503 : 409, result.Error.StatusCode);
            Assert.Null(result.Error.Limit);
            Assert.Equal(result.Error.Message, result.Content);
            var completed = Assert.IsType<WeaverToolCallOutcome>(Assert.Single(events, item => item.Kind == WeaverStreamEventKind.ToolCallCompleted).Payload);
            Assert.Equal(result, completed.Result);
            var recorded = Assert.Single((await sessions.ListMessagesAsync(session.Id)).Where(message => message.Role == WeaverRole.Tool));
            Assert.Equal(result.Error, Assert.Single(recorded.ToolResults).Error);
            Assert.False(string.IsNullOrWhiteSpace(recorded.RequestId));
            Assert.True(recorded.HasDisclosureProvenance);
            Assert.NotEmpty(recorded.DisclosureHistory!);
            var audit = await services.GetRequiredService<IWeaverAuditReader>().ListAsync(session.Id);
            Assert.Contains(audit, item => item.Metadata.GetValueOrDefault("code") == result.Error.Code
                && item.Metadata.GetValueOrDefault("ownerUnavailable") == (unavailable ? "true" : "false"));
            foreach (var item in events)
                Assert.DoesNotContain(canary, JsonSerializer.Serialize(item, WeaverSseJsonContext.Default.WeaverStreamEvent));
            Assert.DoesNotContain(canary, JsonSerializer.Serialize(await services.GetRequiredService<IWeaverSessionRecordStore>().LoadAsync(session.Id)));
            Assert.DoesNotContain(canary, JsonSerializer.Serialize(audit));
        }
        if (copilot)
        {
            var returned = Assert.Single(transport.CopilotReturns);
            Assert.Equal("denied", returned.GetProperty("status").GetString());
            Assert.False(returned.GetProperty("executed").GetBoolean());
            Assert.Equal(result.Error.Code, returned.GetProperty("error").GetProperty("code").GetString());
            Assert.DoesNotContain(canary, returned.GetRawText());
        }
        else
        {
            var returned = Assert.Single(transport.AnthropicResults);
            var observed = JsonSerializer.Deserialize<WeaverToolResult>(returned, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal(result.Error, observed!.Error);
            Assert.DoesNotContain(canary, returned);
        }
        // A fresh request scope reads the actual retained result. This is not a host-restart/package witness.
        using var reopened = root.CreateScope();
        var retainedMessages = await reopened.ServiceProvider.GetRequiredService<IWeaverSessionService>().ListMessagesAsync(sessionId);
        Assert.Equal(result.Error, Assert.Single(retainedMessages.Where(message => message.Role == WeaverRole.Tool)).ToolResults.Single().Error);
        Assert.Equal(1, tool.Preparations);
        Assert.Equal(preparation ? 0 : 1, tool.Reads);
        Assert.Empty((await reopened.ServiceProvider.GetRequiredService<IWeaverProposalService>().ListAsync(sessionId)).Value!);
    }

    private sealed class RefusingTool(string canary, bool preparation, bool unavailable) : IWeaverTool
    {
        internal int Preparations, Reads;
        public WeaverToolDescriptor Descriptor { get; } = new("typed_refusal_read", "Read owned resource", "Read the current resource.",
            WeaverToolMutability.ReadOnly, WeaverRisk.ReadOnly, "{\"type\":\"object\"}", [], [])
        { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId, ModuleCodes = ["native.ok"] };
        private WeaverError Error => new("outside." + canary, canary, unavailable ? 503 : 409) { OwnerUnavailable = unavailable, Limit = canary };
        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Preparations++;
            return Task.FromResult(preparation ? new WeaverResult<WeaverToolPreparation>(null, Error)
                : WeaverResult<WeaverToolPreparation>.Success(new(new("test.module", "test.resource", "owned", "1"),
                    JsonSerializer.SerializeToElement(invocation.Arguments), WeaverRisk.ReadOnly,
                    new("test.preview", "1", JsonSerializer.SerializeToElement(new { })), false)));
        }
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        { Reads++; return Task.FromResult(new WeaverResult<WeaverToolResult>(null, Error)); }
    }

    private sealed class Transport(string toolName) : IGitHubCopilotClientFactory, IAnthropicChatClientFactory
    {
        internal readonly List<JsonElement> CopilotReturns = [];
        internal readonly List<string> AnthropicResults = [];
        internal int AnthropicCalls, CopilotCalls;
        public IGitHubCopilotClient Create(GitHubCopilotClientRequest request) => new CopilotClient(this);
        public IChatClient Create(string apiKey, string model) => new AnthropicClient(this, toolName);

        private sealed class CopilotClient(Transport owner) : IGitHubCopilotClient
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task PingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<IReadOnlyCollection<string>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<string>>([]);
            public Task<IGitHubCopilotSession> CreateSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
                => Task.FromResult<IGitHubCopilotSession>(new CopilotSession(owner, request));
            public Task<IGitHubCopilotSession> ResumeSessionAsync(GitHubCopilotSessionRequest request, CancellationToken cancellationToken)
                => CreateSessionAsync(request, cancellationToken);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private sealed class CopilotSession(Transport owner, GitHubCopilotSessionRequest request) : IGitHubCopilotSession
        {
            public string SessionId => request.SessionId;
            public string? WorkspacePath => null;
            public async IAsyncEnumerable<GitHubCopilotStreamEvent> SendAsync(string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                yield return new(GitHubCopilotStreamEventKind.Started);
                if (owner.CopilotCalls++ == 0)
                {
                    var tool = Assert.Single(request.CustomTools!);
                    var returned = await tool.InvokeAsync(new AIFunctionArguments(), cancellationToken);
                    owner.CopilotReturns.Add(JsonSerializer.SerializeToElement(returned, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                }
                yield return new(GitHubCopilotStreamEventKind.MessageDelta, "The refusal is retained.");
                yield return new(GitHubCopilotStreamEventKind.Completed);
            }
            public Task AbortAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
        private sealed class AnthropicClient(Transport owner, string toolName) : IChatClient
        {
            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                foreach (var content in messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>())
                    owner.AnthropicResults.Add(Assert.IsType<string>(content.Result));
                await Task.CompletedTask;
                yield return new() { Contents = [owner.AnthropicCalls++ == 0
                    ? new FunctionCallContent("typed-call", toolName, new Dictionary<string, object?>())
                    : new TextContent("The refusal is retained.")] };
            }
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }
}
