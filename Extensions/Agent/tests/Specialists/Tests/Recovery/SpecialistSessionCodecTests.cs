using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Agent.Specialists.Services;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Recovery;

public sealed class SpecialistSessionCodecTests
{
    private const string Conversation = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static SpecialistInvocationIdentity Identity => new("tenant", "tenant", "workflow", "activity", "operation", "artifact", "hash", "version", "template");
    private static SpecialistSessionTarget Target => new(Conversation, 1);
    private static ChatMessage[] Messages => [new(ChatRole.User, "private business brief"), new(ChatRole.Assistant, "{\"headline\":\"accepted\"}")];

    [Fact]
    public void The_portable_projection_contains_only_visible_role_text_and_exact_binding()
    {
        using var protection = Protection("old", ("old", "fixture-old-key"));
        var codec = Codec(protection);
        var raw = new[]
        {
            new ChatMessage(ChatRole.User, "private business brief") { RawRepresentation = new object(), AdditionalProperties = new() { ["private"] = "private-sdk-state" } },
            new ChatMessage(ChatRole.Assistant, [new TextReasoningContent("private-reasoning"), new TextContent("{\"headline\":\"accepted\"}") { RawRepresentation = new object() }])
        };
        var snapshot = codec.Capture(SpecialistSessionCodec.VisibleMessages(raw), Identity, Target);
        Assert.DoesNotContain("private business brief", snapshot.ProtectedValue, StringComparison.Ordinal);
        var plaintext = protection.GetRequiredService<ISecretValueProtector>().Unprotect(snapshot.ProtectedValue);
        Assert.Contains("private business brief", plaintext, StringComparison.Ordinal);
        Assert.DoesNotContain("private-reasoning", plaintext, StringComparison.Ordinal);
        Assert.DoesNotContain("private-sdk-state", plaintext, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.", plaintext, StringComparison.Ordinal);
        var restored = codec.Restore(snapshot, Identity);
        Assert.Equal(new[] { ChatRole.User, ChatRole.Assistant }, restored.Select(x => x.Role));
        Assert.All(restored, message =>
        {
            Assert.Null(message.RawRepresentation);
            Assert.Null(message.AdditionalProperties);
            Assert.IsType<TextContent>(Assert.Single(message.Contents));
        });
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("partition")]
    [InlineData("version")]
    [InlineData("template")]
    [InlineData("conversation")]
    [InlineData("revision")]
    public void Valid_ciphertext_cannot_be_substituted_into_another_resource_binding(string changed)
    {
        using var protection = Protection("old", ("old", "fixture-old-key"));
        var codec = Codec(protection);
        var snapshot = codec.Capture(Messages, Identity, Target);
        var identity = Identity;
        switch (changed)
        {
            case "tenant": identity = identity with { TenantId = "foreign" }; break;
            case "partition": identity = identity with { PersistencePartition = "foreign" }; break;
            case "version": identity = identity with { DefinitionVersionId = "foreign" }; break;
            case "template": identity = identity with { TemplateHash = "foreign" }; break;
            case "conversation": snapshot = snapshot with { ConversationId = new string('b', 64) }; break;
            case "revision": snapshot = snapshot with { Revision = 2 }; break;
        }
        Assert.Equal("specialist.session.scope", Assert.Throws<SpecialistTurnException>(() => codec.Restore(snapshot, identity)).Code);
    }

    [Fact]
    public void Global_scope_is_refused_before_protected_data_is_opened()
    {
        using var protection = Protection("old", ("old", "fixture-old-key"));
        var wrapper = new CountingProtector(protection.GetRequiredService<ISecretValueProtector>());
        var codec = new SpecialistSessionCodec(wrapper, new Access(PersistenceAccessContext.Global));
        var exception = Assert.Throws<SpecialistTurnException>(() => codec.Restore(new(Conversation, 1, "not-ciphertext"), Identity));
        Assert.Equal("specialist.session.scope", exception.Code);
        Assert.Equal(0, wrapper.Reads);
    }

    [Fact]
    public void Rotation_reads_retained_keys_and_missing_keys_refuse_without_losing_the_cause()
    {
        using var old = Protection("old", ("old", "fixture-old-key"));
        var snapshot = Codec(old).Capture(Messages, Identity, Target);
        using var rotated = Protection("new", ("old", "fixture-old-key"), ("new", "fixture-new-key"));
        Assert.Equal("private business brief", Codec(rotated).Restore(snapshot, Identity)[0].Text);
        var next = Codec(rotated).Capture(Messages, Identity, Target);
        Assert.StartsWith("v2:new:", next.ProtectedValue, StringComparison.Ordinal);
        using var missing = Protection("new", ("new", "fixture-new-key"));
        var failure = Assert.Throws<SpecialistTurnException>(() => Codec(missing).Restore(snapshot, Identity));
        Assert.Equal("specialist.session.unavailable", failure.Code);
        Assert.NotNull(failure.InnerException);
        Assert.DoesNotContain("private business brief", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("purpose", "other", "specialist.session.codec")]
    [InlineData("codec", "future/99", "specialist.session.codec")]
    [InlineData("role", "system", "specialist.session.bounds")]
    [InlineData("extra", "private", "specialist.session.unavailable")]
    public void Authenticated_unsupported_shapes_are_not_sdk_restore_input(string field, string value, string code)
    {
        using var protection = Protection("old", ("old", "fixture-old-key"));
        var codec = Codec(protection);
        var snapshot = codec.Capture(Messages, Identity, Target);
        var protector = protection.GetRequiredService<ISecretValueProtector>();
        var content = JsonNode.Parse(protector.Unprotect(snapshot.ProtectedValue))!.AsObject();
        if (field == "role") content["messages"]![0]!["role"] = value;
        else if (field == "extra") content["messages"]![0]!["reasoning"] = value;
        else content[field] = value;
        snapshot = snapshot with { ProtectedValue = protector.Protect(content.ToJsonString()) };
        Assert.Equal(code, Assert.Throws<SpecialistTurnException>(() => codec.Restore(snapshot, Identity)).Code);
    }

    [Fact]
    public void History_capacity_is_reserved_before_model_dispatch()
    {
        using var protection = Protection("old", ("old", "fixture-old-key"));
        var codec = Codec(protection);
        var full = Enumerable.Range(0, SpecialistSessionCodec.MaximumMessages).Select(index => new ChatMessage(index % 2 == 0 ? ChatRole.User : ChatRole.Assistant, "{}")).ToArray();
        Assert.Equal("specialist.session.bounds", Assert.Throws<SpecialistTurnException>(() => codec.EnsureCapacity(full, "{}", Identity, Target)).Code);
        Assert.Equal("specialist.session.bounds", Assert.Throws<SpecialistTurnException>(() => codec.Capture([new(ChatRole.User, "unpaired")], Identity, Target)).Code);
    }

    [Fact]
    public async Task Supplementary_character_expansion_is_reserved_before_any_model_open()
    {
        using var protection = Protection("old", ("old", "fixture-old-key"));
        var codec = Codec(protection);
        var history = Enumerable.Range(0, 6).Select(index => new ChatMessage(index % 2 == 0 ? ChatRole.User : ChatRole.Assistant,
            JsonSerializer.Serialize(new Dictionary<string, string> { [index % 2 == 0 ? "brief" : "headline"] = new string('x', 900_000) }))).ToArray();
        var retained = codec.Capture(history, Identity, new(Conversation, 3));
        var factory = new Factory { Response = "{\"headline\":\"" + string.Concat(Enumerable.Repeat("😀", 262_130)) + "\"}" };
        var runner = new SpecialistModelRunner([factory], new SpecialistSchemaValidator(), codec);
        var request = Request("next") with { PreparedSession = retained, SessionTarget = new(Conversation, 4) };
        var refusal = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await runner.RunAsync(request, CancellationToken.None));
        Assert.Equal("specialist.session.bounds", refusal.Code);
        Assert.Equal(0, factory.Opens);
    }

    [Fact]
    public async Task Actual_MAF_restores_one_prefix_and_a_new_fresh_target_stays_isolated()
    {
        using var protection = Protection("old", ("old", "fixture-old-key"));
        var codec = Codec(protection);
        var factory = new Factory();
        var runner = new SpecialistModelRunner([factory], new SpecialistSchemaValidator(), codec);
        var first = await runner.RunAsync(Request("one"), CancellationToken.None);
        Assert.NotNull(first.Session);
        var secondRequest = Request("two") with { PreparedSession = first.Session, SessionTarget = new(Conversation, 2), Identity = Identity with { OperationKey = "second" } };
        var second = await runner.RunAsync(secondRequest, CancellationToken.None);
        Assert.NotNull(second.Session);
        Assert.Equal(4, codec.Restore(second.Session, Identity).Count);
        _ = await runner.RunAsync(Request("fresh") with { SessionTarget = new(new string('b', 64), 1), Identity = Identity with { OperationKey = "fresh" } }, CancellationToken.None);
        Assert.Equal(new[] { 1, 3, 1 }, factory.Requests.Select(x => x.Count));
        Assert.Equal(new[] { ChatRole.User, ChatRole.Assistant, ChatRole.User }, factory.Requests[1].Select(x => x.Role));
        Assert.Equal("one", JsonDocument.Parse(factory.Requests[1][0].Text).RootElement.GetProperty("brief").GetString());
        Assert.Equal("two", JsonDocument.Parse(factory.Requests[1][2].Text).RootElement.GetProperty("brief").GetString());
        Assert.All(factory.Requests.SelectMany(x => x), message => Assert.All(message.Contents, content => Assert.IsType<TextContent>(content)));
        var plaintext = protection.GetRequiredService<ISecretValueProtector>().Unprotect(second.Session.ProtectedValue);
        Assert.DoesNotContain("private-system-instructions", plaintext, StringComparison.Ordinal);
        Assert.DoesNotContain("private-reasoning", plaintext, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_admission_or_disallowed_continuity_cannot_open_a_model(bool disallowed)
    {
        using var protection = Protection("old", ("old", "fixture-old-key"));
        var codec = Codec(protection);
        var factory = new Factory();
        var runner = new SpecialistModelRunner([factory], new SpecialistSchemaValidator(), codec);
        var request = Request("one") with { SessionTarget = null };
        if (disallowed) request = request with { Descriptor = request.Descriptor with { AllowContinuity = false }, PreparedSession = codec.Capture(Messages, Identity, Target) };
        var failure = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await runner.RunAsync(request, CancellationToken.None));
        Assert.Equal(disallowed ? "specialist.session.disallowed" : "specialist.session.admission", failure.Code);
        Assert.Equal(0, factory.Opens);
    }

    private static SpecialistTurnRequest Request(string brief)
    {
        var inputSchema = Json("""{"type":"object","properties":{"brief":{"type":"string"}},"required":["brief"],"additionalProperties":false}""");
        var resultSchema = Json("""{"type":"object","properties":{"headline":{"type":"string"}},"required":["headline"],"additionalProperties":false}""");
        var descriptor = new SpecialistRuntimeDescriptor("private-system-instructions", new("fixture", "connection", "model"), Json("{}"), inputSchema, resultSchema,
            "fixture/1", SpecialistRuntimeIds.SchemaProfile, new(1, 30_000, 128), true);
        var contract = new ActivityContract("specialist", "1", SpecialistRuntimeIds.ConsumerKey, JsonSerializer.SerializeToElement(descriptor),
            [new("brief-key", "brief", new("String"), true, false, false, null, ActivityValuePolicy.Default)],
            new(new("System.Text.Json.JsonElement"), true, ActivityValuePolicy.Default, []), ["Done"], new(SpecialistRuntimeIds.ConsumerKey, SpecialistRuntimeIds.FacadeCapability));
        return new(Identity, descriptor, contract, JsonSerializer.SerializeToElement(new { brief }), "binding", SessionTarget: Target);
    }

    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    private static SpecialistSessionCodec Codec(IServiceProvider services) => new(services.GetRequiredService<ISecretValueProtector>(), new Access(PersistenceAccessContext.Scoped(new("tenant"))));
    private static ServiceProvider Protection(string active, params (string Id, string Value)[] keys)
    {
        var services = new ServiceCollection();
        new SecretsFeature { ActiveKeyId = active, Keys = keys.ToDictionary(x => x.Id, x => x.Value) }.ConfigureServices(services);
        return services.BuildServiceProvider();
    }
    private sealed class Access(PersistenceAccessContext current) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => current; }
    private sealed class CountingProtector(ISecretValueProtector inner) : ISecretValueProtector
    {
        public int Reads { get; private set; }
        public string Protect(string value) => inner.Protect(value);
        public string Unprotect(string value) { Reads++; return inner.Unprotect(value); }
    }
    private sealed class Factory : ISpecialistModelClientFactory
    {
        public string ProviderKey => "fixture";
        public int Opens { get; private set; }
        public string Response { get; set; } = "{\"headline\":\"accepted\"}";
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
        public ValueTask<SpecialistModelClientLease> OpenAsync(SpecialistModelOpenRequest request, CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult(new SpecialistModelClientLease(new Client(this), new(), "fixture/1", 1));
        }
        private sealed class Client(Factory owner) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                Assert.Equal("private-system-instructions", options!.Instructions);
                owner.Requests.Add(messages.ToArray());
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                    [new TextReasoningContent("private-reasoning"), new TextContent(owner.Response)]))
                { FinishReason = ChatFinishReason.Stop, Usage = new() { InputTokenCount = 11, OutputTokenCount = 7 } });
            }
            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }
}
