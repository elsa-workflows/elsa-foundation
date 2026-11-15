using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using CShells.Features;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Services;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Tests;

/// <summary>Bare typed diagnostics require current effects support without acquiring resource identity.</summary>
public sealed class WeaverBareErrorDisclosureTests
{
    [Theory]
    [InlineData(false, "held")]
    [InlineData(false, "throw")]
    [InlineData(false, "cancel")]
    [InlineData(true, "held")]
    [InlineData(true, "throw")]
    [InlineData(true, "cancel")]
    public async Task Current_and_retained_bare_errors_stop_before_provider_disclosure_and_survive_restoration(bool retained, string failure)
    {
        using var fixture = new Fixture();
        var session = await fixture.SessionAsync();
        WeaverToolResult original;
        if (retained)
        {
            var first = await fixture.StreamAsync(session.Id);
            Assert.Null(first.Failure);
            Assert.Contains(first.Events, item => item.Kind == WeaverStreamEventKind.Completed);
            original = fixture.Result!;
            fixture.Observation.Mode = failure;
            await fixture.PromptAsync(session.Id, "Explain the previous refusal.");
        }
        else
            fixture.AfterRefusal = () => fixture.Observation.Mode = failure;

        var enteredBefore = fixture.Provider.Inputs.Count;
        using var stop = new CancellationTokenSource();
        var running = fixture.StreamAsync(session.Id, stop.Token);
        try
        {
            if (failure == "cancel")
            {
                await fixture.Observation.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
                stop.Cancel();
            }
            var failed = await running.WaitAsync(TimeSpan.FromSeconds(10));
            original = fixture.Result!;
            AssertBare(original);
            Assert.True(fixture.Observation.FailedChecks > 0);
            Assert.Equal(enteredBefore + (retained ? 0 : 1), fixture.Provider.Inputs.Count);
            Assert.DoesNotContain(failed.Events, item => item.Kind is WeaverStreamEventKind.Completed
                or WeaverStreamEventKind.ContributedResult or WeaverStreamEventKind.ToolCallCompleted);
            if (failure == "held")
            {
                Assert.Null(failed.Failure);
                Assert.Equal("schema-version-not-finalized", Assert.Single(failed.Events, item => item.Kind == WeaverStreamEventKind.Error).Error?.Code);
            }
            else if (failure == "throw")
            {
                var domain = Assert.IsType<WeaverContributionException>(failed.Failure);
                Assert.Contains(Causes(domain), cause => ReferenceEquals(cause, fixture.Observation.Cause));
            }
            else
                Assert.True(failed.Failure is OperationCanceledException
                    || failed.Events.Any(item => item.Kind == WeaverStreamEventKind.TurnCancelled));
            Assert.False(fixture.Services.GetRequiredService<IWeaverTurnRegistry>().IsActive(session.Id));
            Assert.Equal(0, fixture.Owner.ResultChecks);
            Assert.Equal(0, fixture.Principal.Calls);
            Assert.Equal(fixture.AuthorityCallsAtRefusal, fixture.Authority.Calls);

            // Reopening a request scope restores the exact normalized diagnostic, without retrying its tool.
            fixture.Observation.Mode = "available";
            if (failure == "cancel")
            {
                fixture.Observation.Release.TrySetResult();
                await fixture.Observation.LateCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(enteredBefore + (retained ? 0 : 1), fixture.Provider.Inputs.Count);
            }
            fixture.AfterRefusal = null;
            await fixture.PromptAsync(session.Id, "Explain the retained refusal after restoration.");
            using var reopened = fixture.Root.CreateScope();
            var recovered = await Fixture.StreamAsync(reopened.ServiceProvider, session.Id);
            Assert.Null(recovered.Failure);
            Assert.Contains(recovered.Events, item => item.Kind == WeaverStreamEventKind.Completed);
            Assert.Contains(fixture.Provider.Inputs[^1].History, message => message.Role == WeaverRole.Tool
                && JsonSerializer.Deserialize<WeaverToolResult>(message.Content, new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Error == original.Error);
            var messages = await reopened.ServiceProvider.GetRequiredService<IWeaverSessionService>().ListMessagesAsync(session.Id);
            Assert.Equal(original, Assert.Single(messages, message => message.Role == WeaverRole.Tool).ToolResults.Single());
            Assert.Equal(1, fixture.Tool.Preparations);
            Assert.Equal(0, fixture.Tool.Executions);
            Assert.Equal(0, fixture.Owner.ResultChecks);
            Assert.Equal(0, fixture.Principal.Calls);
            Assert.Equal(fixture.AuthorityCallsAtRefusal, fixture.Authority.Calls);
        }
        finally
        {
            stop.Cancel();
            fixture.Observation.Release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bare_history_uses_new_Error_presence_without_tool_metadata_and_preserves_legacy_compatibility(bool typed)
    {
        // No effects selection, tool, owner source, or principal is needed for legacy bare refusal text.
        // Adding Error alone introduces the effects contract, even if its original tool is now absent.
        using var fixture = new Fixture(effects: false);
        fixture.Registry.Hidden = true;
        var session = await fixture.SessionAsync();
        var result = new WeaverToolResult("old-call", "uninstalled_tool", false, "A normalized refusal.")
        { Error = typed ? new("module.code.unsupported", "A normalized refusal.", 409) : null };
        await fixture.Services.GetRequiredService<IWeaverSessionService>().AddMessageAsync(session.Id,
            new(WeaverRole.Tool, JsonSerializer.Serialize(result), WeaverMessageStatus.Completed, null, [], [])
            {
                RequestId = "old-refusal", DeliveryFingerprint = WeaverInvocationIntegrity.Digest(result),
                ToolResults = [result], HasDisclosureProvenance = true,
                DisclosureHistory = WeaverDisclosureHistory.Freeze([new WeaverDisclosureSnapshot([], [result])])
            });

        var streamed = await fixture.StreamAsync(session.Id);

        Assert.Null(streamed.Failure);
        if (typed)
        {
            Assert.Equal("weaver.effects.not_enabled", Assert.Single(streamed.Events, item => item.Kind == WeaverStreamEventKind.Error).Error?.Code);
            Assert.Empty(fixture.Provider.Inputs);
        }
        else
        {
            Assert.Contains(streamed.Events, item => item.Kind == WeaverStreamEventKind.Completed);
            Assert.Contains(Assert.Single(fixture.Provider.Inputs).History, message => message.Role == WeaverRole.Tool);
        }
        Assert.Equal(0, fixture.Observation.Calls);
        Assert.Equal(0, fixture.Owner.ResultChecks);
        Assert.Equal(0, fixture.Principal.Calls);
        Assert.Equal(0, fixture.Authority.Calls);
        Assert.Equal(0, fixture.Tool.Preparations);
    }

    [Fact]
    public async Task A_typed_error_with_resource_access_keeps_its_normal_owner_disclosure_callback()
    {
        using var fixture = new Fixture();
        var session = await fixture.SessionAsync();
        var result = new WeaverToolResult("owned-call", fixture.Tool.Descriptor.Name, false, "Normalized owner refusal.")
        {
            Error = new("module.code.unsupported", "Normalized owner refusal.", 409),
            Access = new("test.module", "1", null)
            { DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId, InvocationId = "owned-invocation" }
        };
        await fixture.Services.GetRequiredService<IWeaverSessionService>().AddMessageAsync(session.Id,
            new(WeaverRole.Tool, JsonSerializer.Serialize(result), WeaverMessageStatus.Completed, null, [], [])
            {
                RequestId = "owned-invocation", DeliveryFingerprint = WeaverInvocationIntegrity.Digest(result),
                ToolResults = [result], HasDisclosureProvenance = true,
                DisclosureHistory = WeaverDisclosureHistory.Freeze([new WeaverDisclosureSnapshot([], [result])])
            });

        var streamed = await fixture.StreamAsync(session.Id);

        Assert.IsType<WeaverContributionException>(streamed.Failure);
        Assert.True(fixture.Owner.ResultChecks > 0);
        Assert.True(fixture.Authority.Calls > 0);
        Assert.True(fixture.Observation.Calls > 0);
        Assert.Empty(fixture.Provider.Inputs);
        Assert.Equal(0, fixture.Tool.Preparations);
    }

    private static void AssertBare(WeaverToolResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Access); Assert.Null(result.Payload); Assert.Null(result.Receipt);
        Assert.Equal("module.code.unsupported", result.Error?.Code);
        Assert.Equal(WeaverModuleCodes.UnsupportedMessage, result.Content);
        Assert.Equal(result.Error?.Message, result.Content);
    }

    private static IEnumerable<Exception> Causes(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var child in aggregate.InnerExceptions)
                foreach (var cause in Causes(child)) yield return cause;
        else if (error.InnerException is { } directCause)
            foreach (var cause in Causes(directCause)) yield return cause;
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly Observation Observation = new();
        internal readonly RefusingTool Tool = new();
        internal readonly Owner Owner = new();
        internal readonly Principal Principal = new();
        internal readonly Authority Authority = new();
        internal readonly Provider Provider = new();
        internal readonly Registry Registry;
        internal readonly ServiceProvider Root;
        private readonly IServiceScope _scope;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal Action? AfterRefusal;
        internal WeaverToolResult? Result;
        internal int AuthorityCallsAtRefusal;

        internal Fixture(bool effects = true)
        {
            Registry = new(Tool);
            Root = WeaverTestServices.Create(services =>
            {
                if (effects) new ObservedEffects().ConfigureServices(services);
                services.AddScoped<ISchemaDormancyCheck>(_ => Observation);
                services.AddSingleton<IWeaverProvider>(Provider);
                services.AddSingleton<IWeaverTool>(Tool);
                services.RemoveAll<IWeaverContextProvider>();
                services.AddScoped<IWeaverContextProvider>(_ => Owner);
                services.AddScoped<IWeaverPrincipalSource>(_ => Principal);
                services.Replace(ServiceDescriptor.Scoped<IWeaverToolRegistry>(_ => Registry));
                services.Replace(ServiceDescriptor.Scoped<IWeaverToolAuthorizer>(_ => Authority));
                services.Replace(ServiceDescriptor.Scoped<IWeaverToolInvoker>(provider => new AfterDispatch(
                    ActivatorUtilities.CreateInstance<DefaultWeaverToolInvoker>(provider), dispatch =>
                    {
                        Result = dispatch.Result;
                        AssertBare(Assert.IsType<WeaverToolResult>(Result));
                        AuthorityCallsAtRefusal = Authority.Calls;
                        // Remove metadata only after the genuine invoker has normalized and admitted its refusal.
                        Registry.Hidden = true;
                        AfterRefusal?.Invoke();
                    })));
            });
            _scope = Root.CreateScope();
        }

        internal async Task<WeaverSession> SessionAsync()
        {
            var session = await Services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant", "actor", "conversation",
                Provider.ProviderId, "build", null, WeaverPolicy.Default, new Dictionary<string, string>()));
            await PromptAsync(session.Id, "Read the resource.");
            return session;
        }
        internal Task<WeaverMessage> PromptAsync(string sessionId, string content)
            => Services.GetRequiredService<IWeaverSessionService>().AddMessageAsync(sessionId, new(WeaverRole.User, content, WeaverMessageStatus.Pending, null, [], []));
        internal Task<Streamed> StreamAsync(string sessionId, CancellationToken token = default) => StreamAsync(Services, sessionId, token);
        internal static async Task<Streamed> StreamAsync(IServiceProvider services, string sessionId, CancellationToken token = default)
        {
            var events = new List<WeaverStreamEvent>();
            try
            {
                await foreach (var item in services.GetRequiredService<IWeaverStreamingService>().StreamAsync(sessionId, token)) events.Add(item);
                return new(events, null);
            }
            catch (Exception error) when (error is WeaverContributionException or OperationCanceledException) { return new(events, error); }
        }
        public void Dispose() { _scope.Dispose(); Root.Dispose(); }
    }

    private sealed record Streamed(List<WeaverStreamEvent> Events, Exception? Failure);

    [ShellFeature("WeaverBareErrorEffectsProbe", DependsOn = new object[] { typeof(WeaverFeature) })]
    [RequiresSchemaVersion("effects.bare-error.fixture", "1.0.0")]
    public class ObservedEffects : WeaverEffectsFeature;

    private sealed class Observation : ISchemaDormancyCheck
    {
        internal string Mode = "available";
        internal int Calls, FailedChecks;
        internal readonly IOException Cause = new("Private shared-observation cause.");
        internal readonly TaskCompletionSource Blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource LateCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SchemaAvailability Evaluate(IEnumerable<SchemaVersionRequirement> requirements)
            => SchemaDormancyRule.Evaluate(requirements, family => Mode == "available"
                ? new(family, null, ["1.0.0"], "1.0.0", "1.0.0", false, null, [], null, DateTimeOffset.UnixEpoch) : null);
        public async ValueTask<SchemaAvailability> EvaluateAsync(IEnumerable<SchemaVersionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Mode != "available") FailedChecks++;
            if (Mode == "throw") throw Cause;
            if (Mode == "cancel")
            {
                Blocked.TrySetResult();
                await Release.Task; // Deliberately ignores cancellation: admission must bound this observer.
                LateCompletion.TrySetResult();
            }
            return Evaluate(requirements);
        }
        public ValueTask EnsureAvailableAsync(IEnumerable<SchemaVersionRequirement> requirements, string? featureId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public IReadOnlyList<SchemaFamilyObservation> Observe() => [];
        public ValueTask<IReadOnlyList<SchemaFamilyObservation>> ReadStatusAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Observe());
    }

    private sealed class RefusingTool : IWeaverTool
    {
        internal int Preparations, Executions;
        public WeaverToolDescriptor Descriptor { get; } = new("bare_error_read", "Read", "Reads a resource.",
            WeaverToolMutability.ReadOnly, WeaverRisk.ReadOnly, "{}", [], [])
        { ModuleId = "test.module", DisclosureSourceId = WeaverTestContextSource.TestModuleSourceId, ModuleCodes = [] };
        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
        { Preparations++; return Task.FromResult(new WeaverResult<WeaverToolPreparation>(null, new("outside.private", "Private rejected message.", 409))); }
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        { Executions++; throw new InvalidOperationException("A refused preparation cannot execute."); }
    }

    private sealed class AfterDispatch(IWeaverToolInvoker inner, Action<WeaverToolDispatch> after) : IWeaverToolInvoker
    {
        public async Task<WeaverToolDispatch> DispatchAsync(WeaverToolInvocation invocation, WeaverPolicy policy, CancellationToken cancellationToken = default)
        { var result = await inner.DispatchAsync(invocation, policy, cancellationToken); after(result); return result; }
    }

    private sealed class Registry(IWeaverTool tool) : IWeaverToolRegistry
    {
        internal bool Hidden;
        public Task<IReadOnlyCollection<IWeaverTool>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<IWeaverTool>>(Hidden ? [] : [tool]);
        public Task<IWeaverTool?> FindAsync(string toolName, CancellationToken cancellationToken = default)
            => Hidden ? throw new InvalidOperationException("Bare diagnostic admission must not resolve its former resource tool.")
                : Task.FromResult<IWeaverTool?>(toolName == tool.Descriptor.Name ? tool : null);
    }

    private sealed class Owner : IWeaverContextProvider
    {
        internal int ResultChecks;
        public WeaverContextProviderDescriptor Descriptor { get; } = new(WeaverTestContextSource.TestModuleSourceId, "test.module", "owner", [], ["1"], []);
        public ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WeaverContextContribution([], []));
        public ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
        { ResultChecks += request.Results.Count; throw new InvalidOperationException("Bare diagnostics are not owner resource evidence."); }
    }

    private sealed class Principal : IWeaverPrincipalSource
    {
        internal int Calls;
        public ClaimsPrincipal? GetCurrent(string actorId, string tenantId)
        { Calls++; throw new InvalidOperationException("Bare diagnostics require no resource principal."); }
    }

    private sealed class Authority : IWeaverToolAuthorizer
    {
        internal int Calls;
        public Task<WeaverPolicyDecision> AuthorizeAsync(WeaverToolDescriptor tool, string actorId, string tenantId, WeaverResourceTarget? target, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new WeaverPolicyDecision(true, [])); }
    }

    private sealed class Provider : IWeaverProvider
    {
        private readonly DeterministicWeaverProvider _inner = new();
        internal readonly List<WeaverTurnContext> Inputs = [];
        public string ProviderId => _inner.ProviderId;
        public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default) => _inner.CreateSessionAsync(session, cancellationToken);
        public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => _inner.ApproveToolAsync(request, cancellationToken);
        public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => _inner.GetDiagnosticsAsync(cancellationToken);
        public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Inputs.Add(context);
            await Task.CompletedTask;
            if (Inputs.Count == 1 && context.AvailableTools.Any())
                yield return new("request", WeaverStreamEventKind.ToolCallRequested, null, null, null, DateTimeOffset.UtcNow,
                    Payload: new WeaverToolCallRequest("bare-call", "bare_error_read", "{}", WeaverRisk.ReadOnly, false));
            else yield return new(Guid.NewGuid().ToString("N"), WeaverStreamEventKind.MessageDelta, "The retained refusal is explained.", null, null, DateTimeOffset.UtcNow);
        }
    }
}
