using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Services;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentEffectsCollectionTests
{
    [Theory]
    [InlineData("legacy")]
    [InlineData("memory")]
    [InlineData("missing")]
    [InlineData("blocked")]
    [InlineData("available")]
    public async Task Installed_tools_remain_identifiable_while_provider_advertisement_and_direct_preparation_use_current_admission(string composition)
    {
        var observation = new Observation { Available = composition == "available" };
        var provider = new Provider();
        var legacy = new Tool("legacy_read", null, false);
        var declared = new Tool("declared_read", [], false);
        var coded = new Tool("coded_read", ["sample.ok"], false);
        var performed = new Tool("performed_read", null, true);
        using var root = Build(composition, observation, services =>
        {
            services.AddSingleton<IAgentProvider>(provider);
            foreach (var tool in new[] { legacy, declared, coded, performed }) services.AddSingleton<IAgentTool>(tool);
        });
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        var sessions = services.GetRequiredService<IAgentSessionService>();
        var session = await Session(sessions, provider);
        var registry = services.GetRequiredService<IAgentToolRegistry>();
        Assert.Equal(4, (await registry.ListAsync()).Count);
        Assert.Same(declared, await registry.FindAsync(declared.Descriptor.Name));
        await Prompt(services, session.Id);
        var usable = composition is "memory" or "available";
        Assert.Equal(usable ? 4 : 1, Assert.Single(provider.Inputs).AvailableTools.Count);
        Assert.Contains(legacy.Descriptor.Name, provider.Inputs[0].AvailableTools.Select(x => x.Name));
        Assert.All(new[] { legacy, declared, coded, performed }, tool => Assert.Equal(0, tool.PrepareCalls));
        foreach (var tool in new[] { declared, coded, performed })
        {
            var result = await services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(
                new("call-" + tool.Descriptor.Name, tool.Descriptor.Name, session.Id, session.ActorId, new Dictionary<string, object?>(), [])
                    { TenantId = session.TenantId, TurnId = "direct" }, session.Policy);
            Assert.Equal(usable ? 1 : 0, tool.PrepareCalls);
            if (!usable) Assert.Equal(composition == "legacy" ? "agent.effects.not_enabled" : "schema-version-not-finalized", result.Error?.Code);
        }
        await services.GetRequiredService<IAgentToolInvoker>().DispatchAsync(
            new("legacy-call", legacy.Descriptor.Name, session.Id, session.ActorId, new Dictionary<string, object?>(), [])
                { TenantId = session.TenantId, TurnId = "direct" }, session.Policy);
        Assert.Equal(1, legacy.PrepareCalls);
        if (composition == "blocked")
        {
            observation.Available = true;
            await Prompt(services, session.Id);
            Assert.Equal(4, provider.Inputs[^1].AvailableTools.Count);
            observation.Available = false;
            await Prompt(services, session.Id);
            Assert.Single(provider.Inputs[^1].AvailableTools);
            Assert.Same(declared, await registry.FindAsync(declared.Descriptor.Name));
        }
    }

    [Theory]
    [InlineData("legacy", false)]
    [InlineData("legacy", true)]
    [InlineData("memory", false)]
    [InlineData("memory", true)]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("blocked", false)]
    [InlineData("blocked", true)]
    [InlineData("available", false)]
    [InlineData("available", true)]
    public async Task Fresh_new_sources_are_withheld_before_callbacks_without_disabling_legacy_or_optional_context(string composition, bool required)
    {
        var current = new Source("new", [], required);
        var legacy = new Source("legacy", null);
        using var root = Build(composition, new() { Available = composition == "available" }, services =>
        {
            services.AddSingleton<IAgentContextProvider>(current);
            services.AddSingleton<IAgentContextProvider>(legacy);
        });
        using var scope = root.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new("session", "*", new Dictionary<string, string>()) { CallerAttachments = [current.Attachment()] });
        var usable = composition is "memory" or "available";
        Assert.Equal(usable || !required, result.Succeeded);
        Assert.Equal(usable ? 1 : 0, current.Calls);
        Assert.Equal(1, legacy.Calls);
        Assert.Equal(usable ? 2 : 1, result.Value!.Attachments.Count);
        if (!usable)
        {
            var diagnostic = Assert.Single(result.Value.Diagnostics);
            Assert.Equal(composition == "legacy" ? "agent.effects.not_enabled" : "schema-version-not-finalized", diagnostic.Code);
            Assert.Equal(required, diagnostic.Required);
            Assert.False(diagnostic.OwnerUnavailable);
            Assert.DoesNotContain(current.Canary, JsonSerializer.Serialize(result));
        }
    }

    [Theory]
    [InlineData("context")]
    [InlineData("performed")]
    [InlineData("code")]
    [InlineData("descriptor")]
    public async Task Retained_optional_new_source_blocks_provider_history_until_admission_recovers(string kind)
    {
        var resultOnly = kind != "context";
        var observation = new Observation { Available = true };
        var source = new Source("retained", resultOnly ? null : [], false);
        var provider = new Provider();
        using var root = Build("available", observation, services =>
        {
            services.AddSingleton<IAgentContextProvider>(source);
            services.AddSingleton<IAgentProvider>(provider);
            if (resultOnly) services.AddSingleton<IAgentTool>(new Tool("retained_read", kind == "descriptor" ? [] : null, false, source.Descriptor.ModuleId, source.Descriptor.Id));
        });
        using var scope = root.CreateScope();
        var services = scope.ServiceProvider;
        var sessions = services.GetRequiredService<IAgentSessionService>();
        var session = await Session(sessions, provider);
        var admitted = await services.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            new(session.Id, "*", new Dictionary<string, string>()));
        Assert.True(admitted.Succeeded);
        var context = resultOnly ? Array.Empty<AgentContextAttachment>() : admitted.Value!.Attachments.ToArray();
        var results = resultOnly ? new[] { source.Result(kind) } : [];
        await sessions.AddMessageAsync(session.Id, new(AgentRole.User, "Earlier evidence", AgentMessageStatus.Completed, null,
            context.Select(x => x.Id).ToArray(), context) { HasDisclosureProvenance = true, ToolResults = results });
        var callbacks = source.Calls;
        observation.Available = false;
        var refused = await services.GetRequiredService<IAgentContextCollector>().CollectAsync(session.Policy,
            new(session.Id, "*", new Dictionary<string, string>()) { Disclosure = new(session.Id, context, results, false) });
        Assert.False(refused.Succeeded);
        Assert.True(Assert.Single(refused.Value!.Diagnostics).Required);
        Assert.Equal(callbacks, source.Calls);
        await Prompt(services, session.Id);
        Assert.Empty(provider.Inputs);
        observation.Available = true;
        var recovered = await Prompt(services, session.Id);
        Assert.True(provider.Inputs.Count == 1, JsonSerializer.Serialize(recovered.Select(x => x.Error)));
        Assert.True(source.Calls > callbacks);
        Assert.Equal(results, (await sessions.ListMessagesAsync(session.Id)).First().ToolResults);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_observation_changed_during_source_callback_withholds_returned_context(bool retained)
    {
        var observation = new Observation { Available = true };
        var source = new Source("racing", ["sample.ok"], false) { OnCollect = () => observation.Available = false };
        using var root = Build("available", observation, services => services.AddSingleton<IAgentContextProvider>(source));
        using var scope = root.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new("session", "*", new Dictionary<string, string>())
                { Disclosure = retained ? new("session", [source.Attachment()], [], false) : null });
        Assert.Equal(!retained, result.Succeeded);
        Assert.Equal(1, source.Calls);
        Assert.Empty(result.Value!.Attachments);
        Assert.DoesNotContain(source.Canary, JsonSerializer.Serialize(result));
        Assert.Equal("schema-version-not-finalized", Assert.Single(result.Value.Diagnostics).Code);
    }

    [Fact]
    public async Task Nonapplicable_new_source_does_not_disable_a_legacy_prompt()
    {
        var source = new Source("unrelated", []) { Scope = "unrelated" };
        using var root = Build("legacy", new(), services => services.AddSingleton<IAgentContextProvider>(source));
        using var scope = root.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new("session", "other", new Dictionary<string, string>()));
        Assert.True(result.Succeeded);
        Assert.Empty(result.Value!.Diagnostics);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Shared_observation_changed_during_owner_authorization_prevents_tool_advertisement()
    {
        var observation = new Observation { Available = true };
        var tool = new Tool("racing_read", [], false) { OnAuthorize = () => observation.Available = false };
        var provider = new Provider();
        using var root = Build("available", observation, services =>
        {
            services.AddSingleton<IAgentTool>(tool);
            services.AddSingleton<IAgentProvider>(provider);
        });
        using var scope = root.CreateScope();
        var session = await Session(scope.ServiceProvider.GetRequiredService<IAgentSessionService>(), provider);
        await Prompt(scope.ServiceProvider, session.Id);
        Assert.Empty(Assert.Single(provider.Inputs).AvailableTools);
        Assert.Equal(0, tool.PrepareCalls);
    }

    private static ServiceProvider Build(string composition, Observation observation, Action<IServiceCollection> configure)
        => AgentTestServices.Create(services =>
        {
            if (composition == "memory")
            {
                new FoundationAgentEffectsFeature().ConfigureServices(services);
                services.AddScoped<ISchemaDormancyCheck>(_ => throw new InvalidOperationException("Memory must not resolve an observer."));
            }
            else if (composition != "legacy")
            {
                new AgentEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
                if (composition != "missing") services.AddScoped<ISchemaDormancyCheck>(_ => observation);
            }
            configure(services);
        });

    private static Task<AgentSession> Session(IAgentSessionService sessions, Provider provider)
        => sessions.CreateAsync(new("tenant", "actor", "conversation", provider.ProviderId, "explain", null, AgentPolicy.Default, new Dictionary<string, string>()));

    private static async Task<IReadOnlyList<AgentStreamEvent>> Prompt(IServiceProvider services, string sessionId)
    {
        await services.GetRequiredService<IAgentSessionService>().AddMessageAsync(sessionId,
            new(AgentRole.User, "Explain the current context.", AgentMessageStatus.Pending, null, [], []));
        var events = new List<AgentStreamEvent>();
        await foreach (var item in services.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId)) events.Add(item);
        return events;
    }

    private sealed class Source(string id, IReadOnlyCollection<string>? codes, bool required = true) : IAgentContextProvider
    {
        internal string Canary { get; } = Guid.NewGuid().ToString("N");
        internal string Scope { get; init; } = "*";
        internal Action? OnCollect { get; init; }
        internal int Calls { get; private set; }
        public AgentContextProviderDescriptor Descriptor => new(id, "collection.module", Scope, ["sample.context"], ["1"], [])
            { ModuleCodes = codes, Required = required };
        internal AgentContextAttachment Attachment() => new(id + "-attachment", "collection", "resource", "Resource", "sample.context",
            AgentContextSensitivity.Internal, "sample", Canary, null, new Dictionary<string, string>())
            { ModuleId = Descriptor.ModuleId, ContributorId = id, SchemaVersion = "1" };
        internal AgentToolResult Result(string kind)
        {
            var target = new AgentResourceTarget(Descriptor.ModuleId, "sample.resource", "resource", "1");
            var ran = new AgentPayload("sample.ran", "1", JsonSerializer.SerializeToElement(new { ran = true }));
            return new("call", "retained_read", true, "Ran", ran)
            {
                Receipt = new("invocation", target, target, kind == "performed" ? AgentEffectScope.NoPersistedEffect : AgentEffectScope.ServerPersisted, ran)
                    { ModuleCode = kind == "code" ? "sample.ok" : null },
                Access = new(Descriptor.ModuleId, "1", target) { DisclosureSourceId = id, InvocationId = "invocation" }
            };
        }
        public async ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            OnCollect?.Invoke();
            return new([Attachment()], []);
        }
        public async ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            await Task.Yield();
            OnCollect?.Invoke();
            return new(request.Context, []);
        }
    }

    private sealed class Tool(string name, IReadOnlyCollection<string>? codes, bool performs,
        string moduleId = "test.module", string sourceId = AgentTestContextSource.TestModuleSourceId) : IAgentTool, IAgentToolAuthorization
    {
        internal int PrepareCalls { get; private set; }
        internal Action? OnAuthorize { get; init; }
        public AgentToolDescriptor Descriptor => new(name, name, "Reads a sample resource.", AgentToolMutability.ReadOnly, AgentRisk.ReadOnly, "{}", [], [])
            { ModuleId = moduleId, DisclosureSourceId = sourceId, ModuleCodes = codes, CanPerformWithoutPersistence = performs,
                ResultType = "sample.ran", ResultSchemaVersion = "1" };
        public Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            PrepareCalls++;
            return Task.FromResult(AgentResult<AgentToolPreparation>.Failure("sample.refused", "Preparation reached the owner."));
        }
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Advertisement must not execute a tool.");
        public async ValueTask<AgentPolicyDecision> AuthorizeAsync(string actorId, string tenantId, AgentResourceTarget? target, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            OnAuthorize?.Invoke();
            return new(true, []);
        }
    }

    private sealed class Observation : ISchemaDormancyCheck
    {
        internal bool Available { get; set; }
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => SchemaDormancyRule.Evaluate(requirements,
            family => new(family, null, ["0.0.0", "1.0.0"], Available ? "1.0.0" : "0.0.0", Available ? "1.0.0" : "0.0.0", false, null, [], null, DateTimeOffset.UnixEpoch));
        public ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Evaluate(requirements));
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Observe());
    }

    private sealed class Provider : IAgentProvider
    {
        public string ProviderId => "effects-collection";
        internal List<AgentTurnContext> Inputs { get; } = [];
        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderSession(session.Id, ProviderId, new Dictionary<string, string>()));
        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(context);
            await Task.Yield();
            yield return new("complete", AgentStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }
        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentToolApprovalResult(request.Approved, string.Empty));
        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentProviderDiagnostics(ProviderId, true, "available", AgentProviderKind.ProviderSdkBinding,
                [AgentProviderOperation.Chat], AgentProviderRiskProfile.ReviewRequired, new Dictionary<string, string>()));
    }
}
