using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Agent.Core;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Services;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Tests;

/// <summary>
/// Collector-to-insertion races and direct message-owner controls for the experimental session policy owner
/// (#3440, T016). Requests run through the mapped endpoint delegates, the real default collector and the real
/// default session owner, each in its own request scope; TaskCompletionSource barriers hold an installed
/// context source. These are deterministic behavior tests, not native/provider evidence.
/// </summary>
public sealed class AgentSessionAdmissionTests
{
    private const string Actor = "actor-1";
    private const string Tenant = "tenant-1";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly AgentClientBinding Binding = new("client-1", "generation-1");

    // ---- Collector-to-insertion races through HTTP ----

    [Fact]
    public async Task Distinct_overlapping_deliveries_admit_one_pending_prompt_and_refuse_the_other_explicitly()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var held = fixture.Source.HoldNext();

        var first = Task.Run(() => fixture.PostAsync(session.Id, Delivery("first", "r-1")));
        await held.Entered.Task.WaitAsync(Timeout);
        var (secondStatus, secondBody) = await fixture.PostAsync(session.Id, Delivery("second", "r-2"));
        held.Release.SetResult();
        var (firstStatus, firstBody) = await first.WaitAsync(Timeout);

        Assert.Equal(200, secondStatus);
        AssertFailure(firstStatus, firstBody, 409, "agent.message.delivery_conflict");
        var accepted = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.Equal(MessageId(secondBody), accepted.Id);
        Assert.Equal("r-2", accepted.RequestId);
        Assert.Equal(AgentMessageStatus.Pending, accepted.Status);
        // The refused delivery manufactured no accepted turn and leaked no claim: only the accepted prompt owns policy.
        AssertFailure(await fixture.ChangeAsync(session.Id, "full-auto", 1), 409, "agent.session.busy");
        await fixture.Sessions.SetMessageStatusAsync(session.Id, accepted.Id, AgentMessageStatus.Cancelled);
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, "full-auto", 1)).Status);
    }

    [Fact]
    public async Task Identical_overlapping_deliveries_keep_the_first_accepted_identity_and_run_one_provider_turn()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var held = fixture.Source.HoldNext();
        var delivery = Delivery("hello", "r-1");

        var late = Task.Run(() => fixture.PostAsync(session.Id, delivery));
        await held.Entered.Task.WaitAsync(Timeout);
        var (wonStatus, wonBody) = await fixture.PostAsync(session.Id, delivery);
        held.Release.SetResult();
        var (lateStatus, lateBody) = await late.WaitAsync(Timeout);

        Assert.Equal(200, wonStatus);
        Assert.Equal(200, lateStatus);
        var accepted = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.Equal(accepted.Id, MessageId(wonBody));
        Assert.Equal(accepted.Id, MessageId(lateBody));
        // The accepted message keeps the winning delivery's own collection; the late collection never rejoins it.
        Assert.Equal("Collected #2", Assert.Single(accepted.Context).Summary);
        Assert.Equal("Collected #2", Assert.Single(await fixture.Sessions.ListContextAsync(session.Id)).Summary);
        Assert.Equal(1, accepted.AcceptedPolicy!.Revision);

        Assert.Equal(AgentStreamEventKind.Completed, (await fixture.StreamAsync(session.Id))[^1].Kind);
        Assert.Equal("agent.message.no_pending", (await fixture.StreamAsync(session.Id))[^1].Error?.Code);
        Assert.Equal(1, fixture.Provider.Turns);
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, "full-auto", 1)).Status);
    }

    [Fact]
    public async Task A_conflicting_delivery_racing_an_accepted_identity_is_refused_without_replacing_it()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var held = fixture.Source.HoldNext();

        var conflicting = Task.Run(() => fixture.PostAsync(session.Id, Delivery("changed input", "r-1")));
        await held.Entered.Task.WaitAsync(Timeout);
        var (acceptedStatus, acceptedBody) = await fixture.PostAsync(session.Id, Delivery("original input", "r-1"));
        held.Release.SetResult();
        var (conflictStatus, conflictBody) = await conflicting.WaitAsync(Timeout);

        Assert.Equal(200, acceptedStatus);
        AssertFailure(conflictStatus, conflictBody, 409, "agent.message.delivery_conflict");
        var accepted = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.Equal(MessageId(acceptedBody), accepted.Id);
        Assert.Equal("original input", accepted.Content);
        // A sequential conflicting retry is refused by the same owner identity check.
        AssertFailure(await fixture.PostAsync(session.Id, Delivery("changed input", "r-1")), 409, "agent.message.delivery_conflict");
        Assert.Equal(2, fixture.Source.Collections);
    }

    [Fact]
    public async Task Policy_first_a_change_committed_during_collection_refuses_insertion_and_the_retry_carries_the_new_revision()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var held = fixture.Source.HoldNext();
        var delivery = Delivery("hello", "r-1");

        var prompt = Task.Run(() => fixture.PostAsync(session.Id, delivery));
        await held.Entered.Task.WaitAsync(Timeout);
        // No reservation precedes collection, so the competing update wins while the collector is held.
        var (changed, changedBody) = await fixture.ChangeAsync(session.Id, "full-auto", 1);
        held.Release.SetResult();
        var (refused, refusedBody) = await prompt.WaitAsync(Timeout);

        Assert.Equal(200, changed);
        Assert.Equal(2, changedBody.RootElement.GetProperty("data").GetProperty("revision").GetInt64());
        AssertFailure(refused, refusedBody, 409, "agent.message.policy_changed");
        Assert.Empty(await fixture.Sessions.ListMessagesAsync(session.Id));
        Assert.Empty(await fixture.Sessions.ListContextAsync(session.Id));
        // The refusal released nothing it did not own and left no claim: an unchanged probe acknowledges idle.
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, "full-auto", 2)).Status);

        var (accepted, acceptedBody) = await fixture.PostAsync(session.Id, delivery);

        Assert.Equal(200, accepted);
        var message = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.Equal(MessageId(acceptedBody), message.Id);
        Assert.Equal(2, message.AcceptedPolicy!.Revision);
        Assert.Equal(AgentAutonomyMode.FullAuto, message.AcceptedPolicy.Policy.AutonomyMode);
        AssertProvenance(message, "r-1", "Collected #2");
        Assert.Equal(2, fixture.Source.Collections);
    }

    [Fact]
    public async Task Admission_first_an_inserted_prompt_owns_the_policy_through_its_turn_and_keeps_its_attribution()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();

        var (accepted, acceptedBody) = await fixture.PostAsync(session.Id, Delivery("hello", "r-1"));
        Assert.Equal(200, accepted);
        Assert.Equal(1, acceptedBody.RootElement.GetProperty("data").GetProperty("message").GetProperty("acceptedPolicy").GetProperty("revision").GetInt64());

        // The update arrives after insertion: it loses as busy rather than relabelling the accepted work.
        AssertFailure(await fixture.ChangeAsync(session.Id, "full-auto", 1), 409, "agent.session.busy");
        var held = fixture.Provider.HoldNext();
        var stream = Task.Run(() => fixture.StreamAsync(session.Id));
        await held.Entered.Task.WaitAsync(Timeout);
        AssertFailure(await fixture.ChangeAsync(session.Id, "full-auto", 1), 409, "agent.session.busy");
        held.Release.SetResult();
        Assert.Equal(AgentStreamEventKind.Completed, (await stream.WaitAsync(Timeout))[^1].Kind);

        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, "full-auto", 1)).Status);
        var message = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.Equal(1, message.AcceptedPolicy!.Revision);
        Assert.Equal(AgentAutonomyMode.AutoReadOnly, message.AcceptedPolicy.Policy.AutonomyMode);
        AssertProvenance(message, "r-1", "Collected #1");
    }

    [Theory]
    [InlineData("required-fault")]
    [InlineData("contributor-cancelled")]
    [InlineData("request-aborted")]
    [InlineData("request-aborted-ignored-by-contributor")]
    public async Task A_contributor_cancellation_or_fault_during_admission_accepts_nothing_and_holds_no_claim(string failure)
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var held = fixture.Source.HoldNext();
        held.IgnoreCancellation = failure == "request-aborted-ignored-by-contributor";
        held.Failure = failure switch
        {
            "required-fault" => new InvalidOperationException("secret-contributor-detail"),
            "contributor-cancelled" => new OperationCanceledException("The contributor cancelled itself."),
            _ => null
        };
        using var aborted = new CancellationTokenSource();
        var delivery = Delivery("hello", "r-1");

        var prompt = Task.Run(() => fixture.PostAsync(session.Id, delivery, aborted: aborted.Token));
        await held.Entered.Task.WaitAsync(Timeout);
        // While the contributor is held there is no reservation, so policy remains changeable.
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, "auto-read-only", 1)).Status);
        if (failure.StartsWith("request-aborted", StringComparison.Ordinal))
        {
            aborted.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prompt.WaitAsync(Timeout));
            // A contributor that ignored cancellation completes later; its result never rejoins admission.
            held.Release.SetResult();
        }
        else
        {
            held.Release.SetResult();
            var (status, body) = await prompt.WaitAsync(Timeout);
            AssertFailure(status, body, 409, "agent.context.composition_invalid");
            Assert.DoesNotContain("secret-contributor-detail", body.RootElement.GetRawText(), StringComparison.Ordinal);
        }

        Assert.Empty(await fixture.Sessions.ListMessagesAsync(session.Id));
        Assert.Empty(await fixture.Sessions.ListContextAsync(session.Id));
        Assert.Equal("agent.message.no_pending", (await fixture.StreamAsync(session.Id))[^1].Error?.Code);
        Assert.Equal(0, fixture.Provider.Turns);
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, "full-auto", 1)).Status);

        // Nothing was accepted, so the same delivery identity is admitted afresh under the current revision.
        var (retried, retriedBody) = await fixture.PostAsync(session.Id, delivery);
        Assert.Equal(200, retried);
        var message = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.Equal(MessageId(retriedBody), message.Id);
        Assert.Equal(2, message.AcceptedPolicy!.Revision);
        AssertProvenance(message, "r-1", "Collected #2");
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("transport")]
    [InlineData("context-cache")]
    public async Task A_failure_after_accepted_insertion_is_recovered_by_the_same_delivery_without_another_collection_or_turn(string failure)
    {
        var audit = new FailingAuditSink();
        using var fixture = new Fixture(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IAgentAuditSink>(sp => audit.Bind(sp.GetRequiredService<InMemoryAgentAuditStore>())));
            if (failure == "context-cache")
                services.Replace(ServiceDescriptor.Singleton<IAgentSessionService>(sp => new ContextFailingSessions(
                    new InMemoryAgentSessionService(sp.GetRequiredService<IAgentAuditSink>(), sp.GetRequiredService<TimeProvider>()))));
        });
        var session = await fixture.CreateAsync();
        var delivery = Delivery("hello", "r-1");
        if (failure == "audit") audit.FailNext(AgentAuditEventKind.MessageAccepted);
        if (failure == "context-cache") ((ContextFailingSessions)fixture.Sessions).FailNext = true;

        // The endpoint contains the failure as a server error, or the lost transport surfaces it to the host.
        if (failure == "transport")
            await Assert.ThrowsAnyAsync<IOException>(() => fixture.PostAsync(session.Id, delivery, failingResponse: true));
        else
            Assert.True((await fixture.PostAsync(session.Id, delivery)).Status >= 500);

        var accepted = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.Equal(AgentMessageStatus.Pending, accepted.Status);
        var (recovered, recoveredBody) = await fixture.PostAsync(session.Id, delivery);

        Assert.Equal(200, recovered);
        var data = recoveredBody.RootElement.GetProperty("data");
        Assert.Equal(accepted.Id, MessageId(recoveredBody));
        Assert.Equal("agent.message.duplicate", Assert.Single(data.GetProperty("warnings").EnumerateArray()).GetProperty("code").GetString());
        Assert.Equal(1, fixture.Source.Collections);
        Assert.Equal(accepted.Id, Assert.Single(await fixture.UserMessagesAsync(session.Id)).Id);
        AssertProvenance(accepted, "r-1", "Collected #1");
        if (failure == "context-cache")
        {
            // A forwarding session replacement cannot participate: the update is explicitly unsupported, not shadowed.
            AssertFailure(await fixture.ChangeAsync(session.Id, "full-auto", 1), 409, "agent.session.autonomy_unsupported");
        }
        else
        {
            Assert.Equal(1, accepted.AcceptedPolicy!.Revision);
            AssertFailure(await fixture.ChangeAsync(session.Id, "full-auto", 1), 409, "agent.session.busy");
        }

        Assert.Equal(AgentStreamEventKind.Completed, (await fixture.StreamAsync(session.Id))[^1].Kind);
        // The recovered identity authorizes no second provider turn, even after its own turn completed.
        var (again, againBody) = await fixture.PostAsync(session.Id, delivery);
        Assert.Equal(200, again);
        Assert.Equal(accepted.Id, MessageId(againBody));
        Assert.Equal("agent.message.no_pending", (await fixture.StreamAsync(session.Id))[^1].Error?.Code);
        Assert.Equal(1, fixture.Provider.Turns);
        Assert.Equal(1, fixture.Source.Collections);
        if (failure != "context-cache")
        {
            Assert.Equal(200, (await fixture.ChangeAsync(session.Id, "full-auto", 1)).Status);
            Assert.Equal(1, Assert.Single(await fixture.UserMessagesAsync(session.Id)).AcceptedPolicy!.Revision);
        }
    }

    // ---- Direct message-owner controls ----

    [Fact]
    public async Task Concurrent_identical_owner_deliveries_insert_once_and_claim_once()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var start = Barrier();
        var request = UserRequest("hello", "r-1", "fingerprint-1");

        var deliveries = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await fixture.Sessions.AddMessageAsync(session.Id, request);
        })).ToArray();
        start.SetResult();
        var accepted = await Task.WhenAll(deliveries).WaitAsync(Timeout);

        var message = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.All(accepted, x => Assert.Equal(message.Id, x.Id));
        Assert.All(accepted, x => Assert.Equal(1, x.AcceptedPolicy!.Revision));
        // Exactly one prompt claim exists: terminalizing the one accepted message makes the session idle.
        await AssertBusyUntilTerminalAsync(fixture, session, message);
    }

    [Fact]
    public async Task Concurrent_distinct_owner_deliveries_insert_one_and_refuse_the_rest_without_leaking_claims()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var start = Barrier();

        var deliveries = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
        {
            await start.Task;
            try { return (Message: (AgentMessage?)await fixture.Sessions.AddMessageAsync(session.Id, UserRequest($"prompt {index}", $"r-{index}", $"fingerprint-{index}")), Refusal: (Exception?)null); }
            catch (AgentContributionException refusal) { return (Message: (AgentMessage?)null, Refusal: refusal); }
        })).ToArray();
        start.SetResult();
        var outcomes = await Task.WhenAll(deliveries).WaitAsync(Timeout);

        var winner = Assert.Single(outcomes, x => x.Message is not null).Message!;
        Assert.Equal(7, outcomes.Count(x => x.Refusal is not null));
        Assert.Equal(winner.Id, Assert.Single(await fixture.UserMessagesAsync(session.Id)).Id);
        await AssertBusyUntilTerminalAsync(fixture, session, winner);
    }

    [Fact]
    public async Task The_owner_compares_delivery_identity_before_policy_revision_and_pending_state()
    {
        using var fixture = new Fixture();
        var session = await fixture.CreateAsync();
        var original = await fixture.Sessions.AddMessageAsync(session.Id, UserRequest("hello", "r-1", "fingerprint-1") with { ExpectedPolicyRevision = 1 });

        // An identical delivery recovers the accepted identity even when its authorization basis is stale.
        var replayed = await fixture.Sessions.AddMessageAsync(session.Id, UserRequest("hello", "r-1", "fingerprint-1") with { ExpectedPolicyRevision = 7 });
        Assert.Equal(original.Id, replayed.Id);
        // A conflicting reuse is a conflict, never a policy or busy refusal, and never replaces the original.
        await Assert.ThrowsAsync<AgentContributionException>(() =>
            fixture.Sessions.AddMessageAsync(session.Id, UserRequest("other", "r-1", "fingerprint-2") with { ExpectedPolicyRevision = 7 }));
        // A distinct delivery authorized under a stale revision is refused as a policy change before the busy check.
        await Assert.ThrowsAsync<AgentSessionPolicyChangedException>(() =>
            fixture.Sessions.AddMessageAsync(session.Id, UserRequest("other", "r-2", "fingerprint-3") with { ExpectedPolicyRevision = 7 }));
        var message = Assert.Single(await fixture.UserMessagesAsync(session.Id));
        Assert.Equal("hello", message.Content);
        await AssertBusyUntilTerminalAsync(fixture, session, message);
    }

    // ---- Helpers ----

    private static object Delivery(string content, string requestId) => new
    {
        content, requestId, promptMode = "explain",
        source = new { moduleId = "admission.module", actionId = "explain", label = "Explain" }
    };

    private static AgentMessageCreateRequest UserRequest(string content, string requestId, string fingerprint)
        => new(AgentRole.User, content, AgentMessageStatus.Pending, null, [], []) { RequestId = requestId, DeliveryFingerprint = fingerprint, HasDisclosureProvenance = true };

    private static async Task AssertBusyUntilTerminalAsync(Fixture fixture, AgentSession session, AgentMessage message)
    {
        AssertFailure(await fixture.ChangeAsync(session.Id, "full-auto", 1), 409, "agent.session.busy");
        await fixture.Sessions.SetMessageStatusAsync(session.Id, message.Id, AgentMessageStatus.Cancelled);
        Assert.Equal(200, (await fixture.ChangeAsync(session.Id, "full-auto", 1)).Status);
    }

    private static void AssertProvenance(AgentMessage message, string requestId, string summary)
    {
        Assert.Equal(requestId, message.RequestId);
        Assert.Equal(new AgentPromptSource("admission.module", "explain", "Explain"), message.Source);
        Assert.Equal("explain", message.PromptMode);
        Assert.Equal(Binding, message.Client);
        Assert.True(message.HasDisclosureProvenance);
        Assert.False(string.IsNullOrEmpty(message.DeliveryFingerprint));
        var context = Assert.Single(message.Context);
        Assert.Equal("admission.source", context.ContributorId);
        Assert.Equal(summary, context.Summary);
        Assert.Equal(new[] { context.Id }, message.ContextAttachmentIds);
    }

    private static string MessageId(JsonDocument body) => body.RootElement.GetProperty("data").GetProperty("message").GetProperty("id").GetString()!;

    private static void AssertFailure((int Status, JsonDocument Body) response, int expectedStatus, string code)
        => AssertFailure(response.Status, response.Body, expectedStatus, code);

    private static void AssertFailure(int status, JsonDocument body, int expectedStatus, string code)
    {
        Assert.Equal(expectedStatus, status);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("data").ValueKind);
        Assert.Equal(code, body.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static TaskCompletionSource Barrier() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IDisposable
    {
        private readonly IReadOnlyCollection<Endpoint> _endpoints;

        public Fixture(Action<IServiceCollection>? configure = null)
        {
            Root = AgentTestServices.Create(services =>
            {
                services.AddRouting().AddElsaEndpoints();
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
                services.AddSingleton<IAgentProvider>(Provider);
                services.AddScoped<IAgentContextProvider>(_ => Source);
                // Barriers are released explicitly; the collection bound must not race them on a loaded machine.
                services.Configure<AgentContextOptions>(options => options.SourceTimeout = TimeSpan.FromMinutes(5));
                configure?.Invoke(services);
            });
            Scope = Root.CreateScope();
            var app = new TestEndpointRouteBuilder(Scope.ServiceProvider);
            AgentApi.MapAgentApi(app);
            _endpoints = app.DataSources.SelectMany(x => x.Endpoints).ToList();
        }

        public ProposalTestClock Clock { get; } = new();
        public GatedContextSource Source { get; } = new();
        public CountingProvider Provider { get; } = new();
        public ServiceProvider Root { get; }
        public IServiceScope Scope { get; }
        public IAgentSessionService Sessions => Scope.ServiceProvider.GetRequiredService<IAgentSessionService>();

        public Task<AgentSession> CreateAsync() => Sessions.CreateAsync(new(
            Tenant, Actor, "conversation-1", DeterministicAgentProvider.Id, "explain", "Admission session", AgentPolicy.Default,
            new Dictionary<string, string>()));

        public async Task<IReadOnlyList<AgentMessage>> UserMessagesAsync(string sessionId)
            => (await Sessions.ListMessagesAsync(sessionId)).Where(x => x.Role == AgentRole.User).ToList();

        public Task<(int Status, JsonDocument Body)> ChangeAsync(string sessionId, string mode, long expectedRevision)
            => SendAsync("ChangeAutonomy", new { autonomyMode = mode, expectedRevision }, sessionId);

        public Task<(int Status, JsonDocument Body)> PostAsync(string sessionId, object body, CancellationToken aborted = default, bool failingResponse = false)
            => SendAsync("PostMessage", body, sessionId, aborted, failingResponse);

        /// <summary>Runs the session's next foreground turn in its own request scope, as the stream endpoint does.</summary>
        public async Task<List<AgentStreamEvent>> StreamAsync(string sessionId)
        {
            using var request = Root.CreateScope();
            var events = new List<AgentStreamEvent>();
            await foreach (var item in request.ServiceProvider.GetRequiredService<IAgentStreamingService>().StreamAsync(sessionId))
                events.Add(item);
            return events;
        }

        private async Task<(int Status, JsonDocument Body)> SendAsync(string operation, object body, string sessionId,
            CancellationToken aborted = default, bool failingResponse = false)
        {
            var endpoint = Assert.Single(_endpoints, x => x.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpoints" + operation);
            using var request = Root.CreateScope();
            var context = new DefaultHttpContext
            {
                RequestServices = request.ServiceProvider,
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Actor), new Claim("elsa.identity.tenant_id", Tenant)], "test")),
                RequestAborted = aborted
            };
            context.Request.RouteValues["sessionId"] = sessionId;
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body));
            context.Request.ContentType = "application/json";
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            var response = new MemoryStream();
            context.Response.Body = failingResponse ? new FailingResponseStream() : response;

            await endpoint.RequestDelegate!(context);

            return (context.Response.StatusCode, JsonDocument.Parse(response.ToArray()));
        }

        public void Dispose()
        {
            Scope.Dispose();
            Root.Dispose();
        }
    }

    /// <summary>An installed required context source whose next collection can be held, faulted or cancelled.</summary>
    internal sealed class GatedContextSource : IAgentContextProvider
    {
        private int _collections;
        private Hold? _next;

        public AgentContextProviderDescriptor Descriptor { get; } = new("admission.source", "admission.module", "*", ["admission.context"], ["1"], []);
        public int Collections => Volatile.Read(ref _collections);

        public Hold HoldNext()
        {
            var hold = new Hold();
            Volatile.Write(ref _next, hold);
            return hold;
        }

        public async ValueTask<AgentContextContribution> CollectAsync(AgentContextRequest request, CancellationToken cancellationToken = default)
        {
            var number = Interlocked.Increment(ref _collections);
            if (Interlocked.Exchange(ref _next, null) is { } hold)
            {
                hold.Entered.TrySetResult();
                if (hold.IgnoreCancellation) await hold.Release.Task;
                else await hold.Release.Task.WaitAsync(cancellationToken);
                if (hold.Failure is { } failure) throw failure;
            }
            return new([new AgentContextAttachment("admission-context", "admission.module", "resource-1", "Admitted resource", "admission.context",
                AgentContextSensitivity.Internal, "admission", $"Collected #{number}", null, new Dictionary<string, string>())
            { ModuleId = "admission.module", SchemaVersion = "1", Client = Binding }], []);
        }

        public ValueTask<AgentContextContribution> AdmitDisclosureAsync(AgentContextDisclosureRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new AgentContextContribution(request.Context, []));

        internal sealed class Hold
        {
            public TaskCompletionSource Entered { get; } = Barrier();
            public TaskCompletionSource Release { get; } = Barrier();
            public bool IgnoreCancellation { get; set; }
            public Exception? Failure { get; set; }
        }
    }

    /// <summary>The deterministic provider, counting real provider turns; one turn can be held inside the provider.</summary>
    internal sealed class CountingProvider : IAgentProvider
    {
        private readonly DeterministicAgentProvider _inner = new();
        private int _turns;
        private (TaskCompletionSource Entered, TaskCompletionSource Release)? _next;

        public string ProviderId => _inner.ProviderId;
        public int Turns => Volatile.Read(ref _turns);

        public (TaskCompletionSource Entered, TaskCompletionSource Release) HoldNext()
        {
            var hold = (Barrier(), Barrier());
            _next = hold;
            return hold;
        }

        public Task<AgentProviderSession> CreateSessionAsync(AgentSession session, CancellationToken cancellationToken = default)
            => _inner.CreateSessionAsync(session, cancellationToken);

        public async IAsyncEnumerable<AgentStreamEvent> ContinueTurnAsync(AgentTurnContext context, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _turns);
            if (_next is { } hold)
            {
                _next = null;
                hold.Entered.TrySetResult();
                await hold.Release.Task.WaitAsync(cancellationToken);
            }
            await foreach (var item in _inner.ContinueTurnAsync(context, cancellationToken))
                yield return item;
        }

        public Task<AgentToolApprovalResult> ApproveToolAsync(AgentProviderToolApprovalRequest request, CancellationToken cancellationToken = default)
            => _inner.ApproveToolAsync(request, cancellationToken);

        public Task<AgentProviderDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
            => _inner.GetDiagnosticsAsync(cancellationToken);
    }

    /// <summary>Forwards to the real audit store; the next event of one kind fails after it was stored.</summary>
    private sealed class FailingAuditSink : IAgentAuditSink
    {
        private IAgentAuditSink _inner = null!;
        private AgentAuditEventKind? _failNext;

        public FailingAuditSink Bind(IAgentAuditSink inner)
        {
            _inner = inner;
            return this;
        }

        public void FailNext(AgentAuditEventKind kind) => _failNext = kind;

        public async Task EmitAsync(AgentAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            await _inner.EmitAsync(auditEvent, cancellationToken);
            if (_failNext == auditEvent.Kind)
            {
                _failNext = null;
                throw new InvalidOperationException("The audit sink failed after the message was accepted.");
            }
        }
    }

    /// <summary>A forwarding session replacement whose context retention fails once after insertion.</summary>
    private sealed class ContextFailingSessions(IAgentSessionService inner) : IAgentSessionService
    {
        public bool FailNext { get; set; }
        public Task<AgentSession> CreateAsync(AgentSessionCreateRequest request, CancellationToken cancellationToken = default) => inner.CreateAsync(request, cancellationToken);
        public Task<AgentSession?> FindAsync(string sessionId, CancellationToken cancellationToken = default) => inner.FindAsync(sessionId, cancellationToken);
        public Task<AgentMessage> AddMessageAsync(string sessionId, AgentMessageCreateRequest request, CancellationToken cancellationToken = default) => inner.AddMessageAsync(sessionId, request, cancellationToken);
        public Task<IReadOnlyCollection<AgentMessage>> ListMessagesAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListMessagesAsync(sessionId, cancellationToken);
        public Task<AgentMessage?> FindMessageAsync(string sessionId, string messageId, CancellationToken cancellationToken = default) => inner.FindMessageAsync(sessionId, messageId, cancellationToken);
        public Task<AgentMessage?> FindLatestMessageAsync(string sessionId, AgentRole? role = null, CancellationToken cancellationToken = default) => inner.FindLatestMessageAsync(sessionId, role, cancellationToken);
        public Task SetMessageStatusAsync(string sessionId, string messageId, AgentMessageStatus status, CancellationToken cancellationToken = default) => inner.SetMessageStatusAsync(sessionId, messageId, status, cancellationToken);
        public Task<IReadOnlyCollection<AgentContextAttachment>> ListContextAsync(string sessionId, CancellationToken cancellationToken = default) => inner.ListContextAsync(sessionId, cancellationToken);

        public Task AddContextAsync(string sessionId, IReadOnlyCollection<AgentContextAttachment> attachments, CancellationToken cancellationToken = default)
        {
            if (!FailNext) return inner.AddContextAsync(sessionId, attachments, cancellationToken);
            FailNext = false;
            throw new InvalidOperationException("The session context cache failed after the message was accepted.");
        }
    }

    /// <summary>A transport whose response cannot be delivered after the endpoint committed its work.</summary>
    private sealed class FailingResponseStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("The client connection was lost.");
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException(new IOException("The client connection was lost."));
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromException(new IOException("The client connection was lost."));
    }
}
