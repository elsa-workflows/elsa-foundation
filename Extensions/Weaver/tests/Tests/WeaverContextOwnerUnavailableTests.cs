using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Weaver.Core;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

/// <summary>
/// Context admission whose owner could not decide (spec 1193, story 5 scenario 3): collection, a turn's initial admission
/// and the per-tool re-admission all fail as unavailable (503) with the owner's code and the engine's fixed message.
/// Every other required refusal keeps <c>agent.context.incomplete</c> (400) exactly.
/// </summary>
public sealed class WeaverContextOwnerUnavailableTests
{
    private const string SourceId = "owned.source";
    private const string Module = "owned.module";
    private const string OwnerCode = "owned.authorization.unavailable";
    private const string FixedMessage = "The owning module could not establish current authority for this operation.";
    private const string Cause = "Private owner cause 51aa at db-primary:5432";
    private const string Denied = "Current read authority is required.";
    private const string ReadName = "owned_read";

    [Fact]
    public async Task A_collection_refused_only_by_an_unavailable_owner_fails_unavailable_with_its_code()
    {
        using var fixture = new Fixture();
        fixture.Source.Collected = [Unavailable()];

        var result = await fixture.CollectAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(new WeaverError(OwnerCode, FixedMessage, 503), result.Error);
        Assert.DoesNotContain(Cause, result.Error!.Message, StringComparison.Ordinal);
        Assert.True(Assert.Single(result.Value!.Diagnostics).OwnerUnavailable);
    }

    [Fact]
    public async Task A_denial_keeps_the_ordinary_incomplete_context_failure_exactly()
    {
        using var fixture = new Fixture();
        fixture.Source.Collected = [new(SourceId, "owned.permission_denied", Denied, true)];

        var result = await fixture.CollectAsync();

        Assert.Equal(new WeaverError("weaver.context.incomplete", Denied, 400), result.Error);
    }

    [Theory]
    [InlineData("weaver.context.incomplete")]
    [InlineData("Weaver.Session.Closed")]
    [InlineData("unavailable")]
    [InlineData("owned authorization unavailable")]
    [InlineData("")]
    public async Task A_reserved_or_malformed_code_cannot_be_spoofed_into_the_unavailable_outcome(string code)
    {
        using var fixture = new Fixture();
        fixture.Source.Collected = [Unavailable(code: code)];

        var result = await fixture.CollectAsync();

        Assert.Equal("weaver.context.incomplete", result.Error?.Code);
        Assert.Equal(400, result.Error?.StatusCode);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("another.source")]
    public async Task A_source_cannot_claim_the_marker_under_another_contributor_s_identity(string contributor)
    {
        using var fixture = new Fixture();
        fixture.Source.Collected = [Unavailable(contributor: contributor)];

        var result = await fixture.CollectAsync();

        Assert.Equal("weaver.context.incomplete", result.Error?.Code);
        Assert.Equal(400, result.Error?.StatusCode);
        Assert.False(Assert.Single(result.Value!.Diagnostics).OwnerUnavailable);
    }

    [Fact]
    public async Task Another_event_handler_cannot_forge_the_unavailable_outcome()
    {
        using var fixture = new Fixture(forge: true);

        var result = await fixture.CollectAsync();

        // Forged under the installed source's own id and with a valid owner code, but never recorded by the engine.
        Assert.Equal("weaver.context.incomplete", result.Error?.Code);
        Assert.Equal(400, result.Error?.StatusCode);
        Assert.False(Assert.Single(result.Value!.Diagnostics).OwnerUnavailable);
    }

    [Fact]
    public async Task A_forged_marker_beside_a_genuine_one_keeps_the_ordinary_failure()
    {
        using var fixture = new Fixture(forge: true);
        fixture.Source.Collected = [Unavailable()];

        var result = await fixture.CollectAsync();

        Assert.Equal("weaver.context.incomplete", result.Error?.Code);
        Assert.Equal(400, result.Error?.StatusCode);
        Assert.Equal(1, result.Value!.Diagnostics.Count(x => x.OwnerUnavailable));
        Assert.Equal(1, result.Value.Diagnostics.Count(x => !x.OwnerUnavailable));
    }

    [Fact]
    public async Task Any_definite_required_refusal_beside_an_unavailable_one_keeps_the_ordinary_failure()
    {
        using var fixture = new Fixture();
        fixture.Source.Collected = [Unavailable(), new(SourceId, "owned.permission_denied", Denied, true)];

        var result = await fixture.CollectAsync();

        Assert.Equal("weaver.context.incomplete", result.Error?.Code);
        Assert.Equal(400, result.Error?.StatusCode);
    }

    [Fact]
    public async Task A_nonrequired_unavailable_diagnostic_neither_fails_nor_relabels_a_collection()
    {
        using var fixture = new Fixture();
        fixture.Source.Collected = [Unavailable(required: false)];
        Assert.True((await fixture.CollectAsync()).Succeeded);

        fixture.Source.Collected = [Unavailable(required: false), new(SourceId, "owned.permission_denied", Denied, true)];
        Assert.Equal(new WeaverError("weaver.context.incomplete", Denied, 400), (await fixture.CollectAsync()).Error);
    }

    [Fact]
    public void The_marker_is_omitted_from_the_wire_unless_set()
    {
        var plain = JsonSerializer.Serialize(new WeaverContributionDiagnostic(SourceId, "owned.permission_denied", Denied, true), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var marked = JsonSerializer.Serialize(Unavailable(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("ownerUnavailable", plain, StringComparison.Ordinal);
        Assert.Contains("\"ownerUnavailable\":true", marked, StringComparison.Ordinal);
        Assert.DoesNotContain("ownerUnavailable", JsonSerializer.Serialize(new WeaverViolation("owned.denied", Denied), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_turn_whose_owner_can_no_longer_decide_at_initial_admission_stops_unavailable(bool unavailable)
    {
        using var fixture = new Fixture();
        var session = await fixture.SessionAsync();
        await fixture.PostAsync(session.Id);
        fixture.Source.Admission = unavailable ? Mode.Unavailable : Mode.Deny;

        var events = await fixture.StreamAsync(session.Id);

        Assert.Empty(fixture.Provider.Inputs);
        Assert.Equal(unavailable ? new WeaverError(OwnerCode, FixedMessage, 503) : new WeaverError("weaver.context.incomplete", Denied, 400),
            Assert.Single(events, item => item.Kind == WeaverStreamEventKind.Error).Error);
        Assert.DoesNotContain(Cause, JsonSerializer.Serialize(events), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_tool_call_whose_owner_can_no_longer_decide_at_re_admission_stops_unavailable_before_dispatch(bool unavailable)
    {
        using var fixture = new Fixture();
        var session = await fixture.SessionAsync();
        await fixture.PostAsync(session.Id);
        // The provider receives the admitted context; the owner changes only when it requests the tool.
        fixture.Provider.BeforeCall = () => fixture.Source.Admission = unavailable ? Mode.Unavailable : Mode.Deny;

        var events = await fixture.StreamAsync(session.Id);

        Assert.Single(fixture.Provider.Inputs);
        Assert.Contains(events, item => item.Kind == WeaverStreamEventKind.ToolCallStarted);
        Assert.DoesNotContain(events, item => item.Kind == WeaverStreamEventKind.ToolCallCompleted);
        Assert.Equal(unavailable ? new WeaverError(OwnerCode, FixedMessage, 503) : new WeaverError("weaver.context.incomplete", Denied, 400),
            Assert.Single(events, item => item.Kind == WeaverStreamEventKind.Error).Error);
        Assert.Equal(0, fixture.Tool.Executions);
        Assert.DoesNotContain(Cause, JsonSerializer.Serialize(events), StringComparison.Ordinal);
    }

    private static WeaverContributionDiagnostic Unavailable(string code = OwnerCode, string contributor = SourceId, bool required = true) =>
        new(contributor, code, "Owner failed: " + Cause, required) { OwnerUnavailable = true };

    public enum Mode { Allow, Deny, Unavailable }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider root;
        private readonly IServiceScope scope;
        internal OwnedSource Source { get; } = new();
        internal CallingProvider Provider { get; } = new();
        internal ReadTool Tool { get; } = new();
        private IServiceProvider Services => scope.ServiceProvider;

        internal Fixture(bool forge = false)
        {
            root = WeaverTestServices.Create(services =>
            {
                services.AddSingleton<IWeaverProvider>(Provider);
                services.AddScoped<IWeaverContextProvider>(_ => Source);
                services.AddSingleton<IWeaverTool>(Tool);
                if (forge) services.TryAddEventHandler<WeaverContextCollecting, ForgingHandler>();
            });
            scope = root.CreateScope();
        }

        internal Task<WeaverResult<WeaverContextContribution>> CollectAsync() => Services.GetRequiredService<IWeaverContextCollector>()
            .CollectAsync(WeaverPolicy.Default, new("session", "*", new Dictionary<string, string>()));

        internal Task<WeaverSession> SessionAsync() => Services.GetRequiredService<IWeaverSessionService>().CreateAsync(new("tenant", "actor",
            "conversation", Provider.ProviderId, "explain", null, WeaverPolicy.Default, new Dictionary<string, string>()));

        internal async Task PostAsync(string sessionId)
        {
            var collected = await Services.GetRequiredService<IWeaverContextCollector>().CollectAsync(WeaverPolicy.Default,
                new(sessionId, "*", new Dictionary<string, string>()));
            Assert.True(collected.Succeeded, collected.Error?.Message);
            var context = collected.Value!.Attachments;
            await Services.GetRequiredService<IWeaverSessionService>().AddMessageAsync(sessionId,
                new(WeaverRole.User, "Read the owned resource.", WeaverMessageStatus.Pending, null, context.Select(x => x.Id).ToArray(), context));
        }

        internal async Task<List<WeaverStreamEvent>> StreamAsync(string sessionId)
        {
            var events = new List<WeaverStreamEvent>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await foreach (var item in Services.GetRequiredService<IWeaverStreamingService>().StreamAsync(sessionId, timeout.Token)) events.Add(item);
            return events;
        }

        public void Dispose() { scope.Dispose(); root.Dispose(); }
    }

    private sealed class OwnedSource : IWeaverContextProvider
    {
        internal IReadOnlyCollection<WeaverContributionDiagnostic> Collected { get; set; } = [];
        internal Mode Admission { get; set; } = Mode.Allow;
        public WeaverContextProviderDescriptor Descriptor { get; } = new(SourceId, Module, "*", ["owned.context"], ["1"], ["owned-resource"]);

        public ValueTask<WeaverContextContribution> CollectAsync(WeaverContextRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new WeaverContextContribution(Collected.Any(x => x.Required) ? [] : [Attachment()], Collected));

        public ValueTask<WeaverContextContribution> AdmitDisclosureAsync(WeaverContextDisclosureRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Admission switch
            {
                Mode.Allow => new WeaverContextContribution(request.Context, []),
                Mode.Deny => new([], [new(SourceId, "owned.permission_denied", Denied, true)]),
                _ => new([], [Unavailable()])
            });

        private static WeaverContextAttachment Attachment() => new("owned-context", Module, "resource", "Owned resource", "owned.context",
            WeaverContextSensitivity.Internal, "selection", "Bounded owned resource", null, new Dictionary<string, string>())
        { ModuleId = Module, SchemaVersion = "1", Target = new(Module, "owned-resource", "resource", "revision-1"), AgentScopes = ["owned/read"] };
    }

    /// <summary>An unrelated event handler that writes a marked diagnostic straight into the public collection.</summary>
    private sealed class ForgingHandler : IEventHandler<WeaverContextCollecting>
    {
        public Task Handle(WeaverContextCollecting domainEvent, CancellationToken cancellationToken)
        {
            domainEvent.Diagnostics.Add(Unavailable());
            return Task.CompletedTask;
        }
    }

    private sealed class ReadTool : IWeaverTool
    {
        internal int Executions;
        public WeaverToolDescriptor Descriptor { get; } = new(ReadName, ReadName, "Reads the owned resource.", WeaverToolMutability.ReadOnly,
            WeaverRisk.ReadOnly, "{\"type\":\"object\"}", [], ["owned/read"]) { ModuleId = Module, DisclosureSourceId = SourceId };
        public Task<WeaverResult<WeaverToolPreparation>> PrepareAsync(WeaverToolInvocation invocation, CancellationToken cancellationToken = default)
            => Task.FromResult(WeaverResult<WeaverToolPreparation>.Success(WeaverTestToolHelpers.Preparation(invocation, Descriptor)));
        public Task<WeaverResult<WeaverToolResult>> ExecuteAsync(WeaverPreparedToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Executions++;
            return Task.FromResult(WeaverResult<WeaverToolResult>.Success(new(invocation.ToolCallId, ReadName, true, "Read the owned resource.")));
        }
    }

    private sealed class CallingProvider : IWeaverProvider
    {
        private readonly DeterministicWeaverProvider inner = new();
        internal List<WeaverTurnContext> Inputs { get; } = [];
        internal Action? BeforeCall { get; set; }
        public string ProviderId => inner.ProviderId;
        public Task<WeaverProviderSession> CreateSessionAsync(WeaverSession session, CancellationToken cancellationToken = default) => inner.CreateSessionAsync(session, cancellationToken);
        public Task<WeaverToolApprovalResult> ApproveToolAsync(WeaverProviderToolApprovalRequest request, CancellationToken cancellationToken = default) => inner.ApproveToolAsync(request, cancellationToken);
        public Task<WeaverProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default) => inner.GetDiagnosticsAsync(cancellationToken);

        public async IAsyncEnumerable<WeaverStreamEvent> ContinueTurnAsync(WeaverTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            Inputs.Add(context);
            if (context.StepIndex == 0)
            {
                BeforeCall?.Invoke();
                yield return new(Guid.NewGuid().ToString("N"), WeaverStreamEventKind.ToolCallRequested, null, "owned-call", null, DateTimeOffset.UtcNow,
                    Payload: new WeaverToolCallRequest("owned-call", ReadName, "{}", WeaverRisk.ReadOnly, false));
            }
            yield return new(Guid.NewGuid().ToString("N"), WeaverStreamEventKind.Completed, null, null, null, DateTimeOffset.UtcNow);
        }
    }
}
