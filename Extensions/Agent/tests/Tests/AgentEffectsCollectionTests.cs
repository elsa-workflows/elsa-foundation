using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Extensions;
using Elsa.Agent.Services;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Observer_refresh_failures_keep_optional_and_required_context_exception_boundaries(bool retained, bool required)
    {
        var failure = new IOException(Guid.NewGuid().ToString("N"));
        var source = new Source("failing-observation", [], required);
        var legacy = new Source("legacy", null);
        using var root = Build("available", new() { Failure = failure }, services =>
        {
            services.AddSingleton<IAgentContextProvider>(source);
            services.AddSingleton<IAgentContextProvider>(legacy);
        });
        using var scope = root.CreateScope();
        var request = new AgentContextRequest("session", "*", new Dictionary<string, string>())
            { Disclosure = retained ? new("session", [source.Attachment()], [], false) : null };
        var collector = scope.ServiceProvider.GetRequiredService<IAgentContextCollector>();
        if (retained || required)
        {
            var error = await Assert.ThrowsAsync<AgentContributionException>(() => collector.CollectAsync(AgentPolicy.Default, request));
            Assert.Same(failure, error.InnerException);
            Assert.Contains(source.Descriptor.Id, error.Message);
            Assert.DoesNotContain(failure.Message, error.Message);
        }
        else
        {
            var result = await collector.CollectAsync(AgentPolicy.Default, request);
            Assert.True(result.Succeeded);
            Assert.Equal(1, legacy.Calls);
            Assert.Single(result.Value!.Attachments);
            var diagnostic = Assert.Single(result.Value.Diagnostics);
            Assert.Equal("agent.context.source_failed", diagnostic.Code);
            Assert.False(diagnostic.OwnerUnavailable);
            Assert.DoesNotContain(failure.Message, JsonSerializer.Serialize(result));
        }
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_cancellation_during_observation_remains_cancellation_and_never_calls_the_source(bool retained)
    {
        using var cancellation = new CancellationTokenSource();
        var source = new Source("cancelled-observation", [], false);
        using var root = Build("available", new() { OnEvaluate = cancellation.Cancel }, services => services.AddSingleton<IAgentContextProvider>(source));
        using var scope = root.CreateScope();
        var request = new AgentContextRequest("session", "*", new Dictionary<string, string>())
            { Disclosure = retained ? new("session", [source.Attachment()], [], false) : null };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default, request, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task A_refresh_failure_for_new_retained_results_uses_the_legacy_sources_required_exception_boundary()
    {
        var failure = new IOException(Guid.NewGuid().ToString("N"));
        var source = new Source("legacy-result-owner", null, false);
        using var root = Build("available", new() { Failure = failure }, services => services.AddSingleton<IAgentContextProvider>(source));
        using var scope = root.CreateScope();
        var error = await Assert.ThrowsAsync<AgentContributionException>(() =>
            scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
                new("session", "*", new Dictionary<string, string>())
                    { Disclosure = new("session", [], [source.Result("performed")], false) }));
        Assert.Same(failure, error.InnerException);
        Assert.Contains(source.Descriptor.Id, error.Message);
        Assert.DoesNotContain(failure.Message, error.Message);
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task An_uncooperative_admission_refresh_is_bounded_and_cannot_admit_late_context(bool afterCallback, bool retained, bool required)
    {
        var clock = new AgentContextCollectionBoundsTests.TriggeredTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source("slow-observation", [], required);
        var healthy = new Source("healthy-legacy", null);
        var calls = 0;
        CancellationToken observed = default;
        using var root = Build("available", new()
        {
            Available = true,
            BeforeEvaluate = token =>
            {
                if (++calls != (afterCallback ? 2 : 1)) return ValueTask.CompletedTask;
                observed = token;
                entered.SetResult();
                return new(release.Task); // Deliberately ignores cancellation, like a stalled infrastructure call.
            }
        }, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.AddSingleton<IAgentContextProvider>(source);
            services.AddSingleton<IAgentContextProvider>(healthy);
        });
        using var scope = root.CreateScope();
        var collection = scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new("session", "*", new Dictionary<string, string>())
                { Disclosure = retained ? new("session", [source.Attachment()], [], false) : null });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.FireNext();
            var result = await collection.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(observed.IsCancellationRequested);
            Assert.Equal(!retained && !required, result.Succeeded);
            var contribution = result.Value!;
            Assert.Equal(retained ? 0 : 1, contribution.Attachments.Count);
            Assert.DoesNotContain(source.Canary, JsonSerializer.Serialize(result));
            var diagnostic = Assert.Single(contribution.Diagnostics);
            Assert.Equal("agent.context.source_timed_out", diagnostic.Code);
            Assert.Equal(retained || required, diagnostic.Required);
            Assert.Equal(afterCallback ? 1 : 0, source.Calls);
            release.SetResult();
            await release.Task;
            Assert.Equal(retained ? 0 : 1, contribution.Attachments.Count);
            Assert.Single(contribution.Diagnostics);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_stop_does_not_wait_for_an_uncooperative_pre_or_post_source_observer(bool afterCallback)
    {
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source("stopped-observer", [], false);
        var calls = 0;
        using var root = Build("available", new()
        {
            Available = true,
            BeforeEvaluate = _ =>
            {
                if (++calls != (afterCallback ? 2 : 1)) return ValueTask.CompletedTask;
                entered.SetResult();
                return new(release.Task);
            }
        }, services => services.AddSingleton<IAgentContextProvider>(source));
        using var scope = root.CreateScope();
        var collection = scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new("session", "*", new Dictionary<string, string>()), stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(stop.Token, error.CancellationToken);
            Assert.Equal(afterCallback ? 1 : 0, source.Calls);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retained_result_metadata_lookup_is_bounded_independently_of_registry_cooperation(bool callerStop)
    {
        using var stop = new CancellationTokenSource();
        var clock = new AgentContextCollectionBoundsTests.TriggeredTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyCollection<IAgentTool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source("metadata-owner", null, false);
        CancellationToken observed = default;
        using var root = Build("memory", new(), services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            services.Replace(ServiceDescriptor.Scoped<IAgentToolRegistry>(_ => new Registry(token =>
            {
                observed = token;
                entered.SetResult();
                return release.Task;
            })));
            services.AddSingleton<IAgentContextProvider>(source);
        });
        using var scope = root.CreateScope();
        var collection = scope.ServiceProvider.GetRequiredService<IAgentContextCollector>().CollectAsync(AgentPolicy.Default,
            new("session", "*", new Dictionary<string, string>())
                { Disclosure = new("session", [], [source.Result("performed")], false) }, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (callerStop)
            {
                stop.Cancel();
                var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal(stop.Token, error.CancellationToken);
            }
            else
            {
                clock.FireNext();
                var result = await collection.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(result.Succeeded);
                Assert.Empty(result.Value!.Attachments);
                Assert.True(Assert.Single(result.Value.Diagnostics).Required);
                Assert.Equal("agent.context.source_timed_out", result.Value.Diagnostics.Single().Code);
            }
            Assert.True(observed.IsCancellationRequested);
            Assert.Equal(0, source.Calls);
        }
        finally { release.TrySetResult([]); }
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("raised")]
    [InlineData("execute")]
    public async Task Direct_tool_and_proposal_facades_wrap_observation_infrastructure_failures_before_dispatch(string phase)
    {
        var failure = new IOException(Guid.NewGuid().ToString("N"));
        var observation = new Observation { Available = true };
        var mutation = new ProposalMutationTool { PrivilegeClasses = [AgentPrivilegeClass.Activation] };
        var read = new Tool("failing_read", [], false);
        using var fixture = await ProposalFixture.CreateAsync(phase == "prepare" ? read : mutation, configure: services =>
        {
            new AgentEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => observation);
            services.Configure<AgentPrivilegePolicyOptions>(options => options.Activation = true);
        });
        AgentActionProposal? proposal = null;
        if (phase == "execute")
        {
            proposal = await fixture.ProposeAsync();
            Assert.True((await fixture.ApproveAsync(proposal)).Succeeded);
        }
        observation.Failure = failure;
        var error = await Assert.ThrowsAsync<AgentContributionException>(async () =>
        {
            if (proposal is not null) await fixture.ExecuteAsync(proposal);
            else await fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy);
        });
        Assert.Same(failure, error.InnerException);
        Assert.DoesNotContain(failure.Message, error.Message);
        Assert.Contains(phase == "prepare" ? read.Descriptor.Name : proposal?.PreparedInvocation?.InvocationId ?? "Prepared invocation", error.Message);
        Assert.Equal(0, read.PrepareCalls);
        Assert.Equal(phase == "prepare" ? 0 : 1, mutation.PrepareCalls);
        Assert.Equal(0, mutation.DispatchCalls);
        if (proposal is not null) Assert.Equal(AgentActionProposalStatus.Approved, (await fixture.Proposals.FindAsync(proposal.Id))!.Status);
    }

    [Fact]
    public async Task Direct_tool_admission_preserves_caller_cancellation_instead_of_wrapping_it()
    {
        using var stop = new CancellationTokenSource();
        var observation = new Observation { OnEvaluate = stop.Cancel };
        var tool = new Tool("cancelled_read", [], false);
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            new AgentEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => observation);
        });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy, stop.Token));
        Assert.Equal(stop.Token, error.CancellationToken);
        Assert.Equal(0, tool.PrepareCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_Stop_finishes_before_an_uncooperative_observer_and_never_enters_the_next_owner_callback(bool finalReadAdmission)
    {
        using var stop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tool = new PreparedReadTool();
        var checks = 0;
        var observation = new Observation
        {
            Available = true, IgnoreCancellation = true,
            BeforeEvaluate = _ =>
            {
                if (++checks != (finalReadAdmission ? 2 : 1)) return ValueTask.CompletedTask;
                entered.SetResult();
                return new(release.Task);
            }
        };
        using var fixture = await ProposalFixture.CreateAsync(tool, configure: services =>
        {
            new AgentEffectsFeatureTests.ObservedEffectsFeature().ConfigureServices(services);
            services.AddScoped<ISchemaDormancyCheck>(_ => observation);
        });
        var dispatch = fixture.Invoker.DispatchAsync(fixture.Invocation(), fixture.Session.Policy, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            stop.Cancel();
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(stop.Token, error.CancellationToken);
            Assert.Equal(finalReadAdmission ? 1 : 0, tool.Inner.PrepareCalls);
            Assert.Equal(0, tool.ExecuteCalls);
        }
        finally { release.TrySetResult(); }
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

    private sealed class PreparedReadTool : IAgentTool
    {
        internal ProposalMutationTool Inner { get; } = new();
        internal int ExecuteCalls { get; private set; }
        public AgentToolDescriptor Descriptor => Inner.Descriptor with
            { Name = "prepared_read", Mutability = AgentToolMutability.ReadOnly, Risk = AgentRisk.ReadOnly, ModuleCodes = [] };
        public async Task<AgentResult<AgentToolPreparation>> PrepareAsync(AgentToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            var result = await Inner.PrepareAsync(invocation, cancellationToken);
            return result with { Value = result.Value! with { Risk = AgentRisk.ReadOnly } };
        }
        public Task<AgentResult<AgentToolResult>> ExecuteAsync(AgentPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            ExecuteCalls++;
            return Task.FromResult(AgentResult<AgentToolResult>.Success(new(invocation.ToolCallId, Descriptor.Name, true, "Read completed.")));
        }
    }

    private sealed class Registry(Func<CancellationToken, Task<IReadOnlyCollection<IAgentTool>>> list) : IAgentToolRegistry
    {
        public Task<IReadOnlyCollection<IAgentTool>> ListAsync(CancellationToken cancellationToken = default) => list(cancellationToken);
        public Task<IAgentTool?> FindAsync(string toolName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Observation : ISchemaDormancyCheck
    {
        internal bool Available { get; set; }
        internal Exception? Failure { get; set; }
        internal Action? OnEvaluate { get; init; }
        internal Func<CancellationToken, ValueTask>? BeforeEvaluate { get; init; }
        internal bool IgnoreCancellation { get; init; }
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements) => SchemaDormancyRule.Evaluate(requirements,
            family => new(family, null, ["0.0.0", "1.0.0"], Available ? "1.0.0" : "0.0.0", Available ? "1.0.0" : "0.0.0", false, null, [], null, DateTimeOffset.UnixEpoch));
        public async ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (BeforeEvaluate is { } before) await before(cancellationToken);
            OnEvaluate?.Invoke();
            if (!IgnoreCancellation) cancellationToken.ThrowIfCancellationRequested();
            if (Failure is { } error) throw error;
            return Evaluate(requirements);
        }
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
