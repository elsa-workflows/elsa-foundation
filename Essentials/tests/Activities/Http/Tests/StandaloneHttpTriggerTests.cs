using System.Text;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Http.Services;
using Elsa.Activities.Http.StandaloneTriggers;
using Elsa.Activities.Testing;
using Elsa.Http.Core;
using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class StandaloneHttpTriggerTests
{
    [Fact]
    public async Task Async_partial_receipt_uses_real_accepted_capture_and_does_not_disclose_target_names()
    {
        using var fixture = new Fixture(partial: true);
        await fixture.InvokeAsync();
        var json = fixture.Response();
        Assert.Equal(202, fixture.Http.Response.StatusCode);
        Assert.Equal("PartiallyAccepted", json.GetProperty("admission").GetString());
        Assert.Equal("AcceptAvailable", json.GetProperty("admissionPolicy").GetString());
        Assert.Equal(1, json.GetProperty("acceptedCount").GetInt32());
        Assert.Equal(1, json.GetProperty("refusedCount").GetInt32());
        Assert.DoesNotContain("private-definition", json.GetRawText());
        Assert.DoesNotContain("private-subscription", json.GetRawText());
        Assert.Equal("18446744073709551615", fixture.Port.Signal!.Payload.GetProperty("amount").GetRawText());
        Assert.False(fixture.Port.Signal.Metadata.TryGetProperty("reservation", out _));
        Assert.Equal([RuntimeIngressTransportState.AwaitingResponse, RuntimeIngressTransportState.WriteCompleted], fixture.Observer.Items.Select(item => item.State));
    }

    [Theory]
    [InlineData(false, 503)]
    [InlineData(true, 400)]
    public async Task Zero_or_require_all_refusal_never_acknowledges_acceptance(bool invalidInput, int expectedStatus)
    {
        using var fixture = new Fixture();
        fixture.Port.Refusal = new("all-subscribers-required", [new("private-subscription-b", 1,
            invalidInput ? "Input" : "Admission", invalidInput ? "IncompatibleValue" : "target-unavailable")], ["private-subscription-a"]);
        await fixture.InvokeAsync();
        Assert.Equal(expectedStatus, fixture.Http.Response.StatusCode);
        Assert.Equal(0, fixture.Response().GetProperty("acceptedCount").GetInt32());
        Assert.Contains(fixture.Response().GetProperty("recipients").EnumerateArray(), item => item.GetProperty("admission").GetString() == "NotAcceptedDueToPolicy");
        Assert.Empty(fixture.Observer.Items);
    }

    [Theory]
    [InlineData("{invalid", 1024, 400)]
    [InlineData("{\"amount\":12345}", 2, 413)]
    public async Task Invalid_or_oversized_body_has_no_admission_effect(string body, int maximum, int expected)
    {
        using var fixture = new Fixture(body: body, maxBytes: maximum);
        await fixture.InvokeAsync();
        Assert.Equal(expected, fixture.Http.Response.StatusCode);
        Assert.Equal(0, fixture.Port.Calls);
        Assert.Empty(fixture.Observer.Items);
    }

    [Fact]
    public async Task Source_authentication_precedes_disclosure_of_ambiguous_reservations()
    {
        using var fixture = new Fixture();
        fixture.Reader.Rows = [fixture.Reservation with { Metadata = new Dictionary<string, string>(fixture.Reservation.Metadata)
            { [HttpEndpointRouting.AuthorizeMetadataKey] = bool.TrueString } }, fixture.Reservation with { OwnerId = "other-private-owner" }];
        await fixture.InvokeAsync();
        Assert.Equal(401, fixture.Http.Response.StatusCode);
        Assert.Equal(0, fixture.Port.Calls);
        Assert.DoesNotContain("other-private-owner", fixture.Response().GetRawText());
    }

    [Fact]
    public async Task Retained_replay_uses_captured_mode_after_current_resource_mode_changes()
    {
        using var fixture = new Fixture();
        fixture.Reader.Rows = [fixture.Reservation with { OwnerRevision = 2, Metadata = new Dictionary<string, string>(fixture.Reservation.Metadata)
            { ["http:standaloneMode"] = "WorkflowResponse", ["http:triggerState"] = "Paused" } }];
        fixture.Port.Replayed = true;
        await fixture.InvokeAsync();
        Assert.Equal(202, fixture.Http.Response.StatusCode);
        Assert.True(fixture.Response().GetProperty("replayed").GetBoolean());
        Assert.Equal("AsyncFanOut", fixture.CapturedMode);
        Assert.Equal(1, fixture.Port.Calls);
    }

    [Fact]
    public async Task Workflow_response_observes_an_instruction_from_a_fresh_scope_without_passing_the_exchange_to_a_workflow()
    {
        using var fixture = new Fixture(mode: "WorkflowResponse");
        var original = await HttpResponseInstructionFixture.StoreWith(new(201, new Dictionary<string, string[]>(),
            JsonSerializer.SerializeToElement("peer-response"), "text/plain"));
        foreach (var activity in await original.ListAllAsync("wf-1"))
            await fixture.Activities.SaveAsync(activity with { Execution = activity.Execution with {
                WorkflowExecutionId = fixture.Receipt.Item.SubscriptionDelivery!.Recipients[0].StartIdentity.WorkflowExecutionId } });
        await fixture.InvokeAsync();
        Assert.Equal(201, fixture.Http.Response.StatusCode);
        Assert.Equal("peer-response", Encoding.UTF8.GetString(((MemoryStream)fixture.Http.Response.Body).ToArray()));
        Assert.Equal(RuntimeIngressTransportState.WriteCompleted, fixture.Observer.Items.Last().State);
    }

    [Fact]
    public async Task Workflow_response_deadline_is_504_and_does_not_reverse_durable_admission()
    {
        using var fixture = new Fixture(mode: "WorkflowResponse", timeout: TimeSpan.FromMilliseconds(50));
        await fixture.InvokeAsync();
        Assert.Equal(504, fixture.Http.Response.StatusCode);
        Assert.Equal(fixture.Receipt.Item.OutboxItemId, fixture.Response().GetProperty("deliveryId").GetString());
        Assert.Equal(1, fixture.Port.Calls);
        Assert.Equal(RuntimeIngressTransportState.TimedOut, fixture.Observer.Items.Last().State);
    }

    [Fact]
    public async Task Workflow_response_missing_instruction_returns_the_accepted_delivery_identity()
    {
        using var fixture = new Fixture(mode: "WorkflowResponse");
        var recipient = Assert.Single(fixture.Receipt.Item.SubscriptionDelivery!.Recipients);
        var now = DateTimeOffset.UtcNow;
        await fixture.Executions.SaveAsync(new(recipient.StartIdentity.WorkflowExecutionId, recipient.Material,
            WorkflowExecutionStatus.Completed, null, now, now, now, now, null, null, fixture.Reservation.Scope.TenantId,
            new Dictionary<string, string>()));
        await fixture.InvokeAsync();
        Assert.Equal(502, fixture.Http.Response.StatusCode);
        Assert.Equal("ResponseMissing", fixture.Response().GetProperty("code").GetString());
        Assert.Equal(fixture.Receipt.Item.OutboxItemId, fixture.Response().GetProperty("deliveryId").GetString());
        Assert.Equal(1, fixture.Port.Calls);
    }

    [Fact]
    public async Task Unavailable_transport_persistence_refuses_before_acceptance()
    {
        using var fixture = new Fixture(); fixture.Observer.Supported = false;
        await fixture.InvokeAsync();
        Assert.Equal(503, fixture.Http.Response.StatusCode);
        Assert.Equal(0, fixture.Port.Calls);
    }

    [Fact]
    public async Task Http_wait_input_is_detached_and_raw_replay_intent_excludes_route_resolution_and_secret_headers()
    {
        using var fixture = new Fixture();
        fixture.Http.Request.Headers["Authorization"] = "Bearer hidden";
        fixture.Http.Request.Headers["Cookie"] = "session=hidden";
        fixture.Http.Request.Headers["X-Api-Key"] = "hidden";
        fixture.Http.Request.Headers["X-Correlation-Id"] = "correlation";
        await fixture.InvokeAsync();
        var invocation = fixture.Port.Invocation!;
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.SourceContext.GetAsync().AsTask());
        Assert.NotNull(invocation.WaitCandidate);
        var input = invocation.WaitCandidate.Input!.Value.Deserialize<HttpRequestModel>()!;
        Assert.Equal("{\"amount\":18446744073709551615}", input.Body);
        Assert.False(input.Headers.ContainsKey("Authorization"));
        Assert.False(input.Headers.ContainsKey("Cookie"));
        Assert.False(input.Headers.ContainsKey("X-Api-Key"));
        Assert.Equal("correlation", Assert.Single(input.Headers["X-Correlation-Id"]));
        Assert.Equal(HttpEndpoint.ActivityType, invocation.WaitCandidate.ProviderId);
        Assert.Equal("/workflows/http/orders", invocation.SubmittedIntent!.Value.GetProperty("path").GetString());
        Assert.False(invocation.SubmittedIntent.Value.TryGetProperty("routeValues", out _));
        Assert.False(invocation.SubmittedIntent.Value.TryGetProperty("reservation", out _));

        // A renamed template parameter changes selected resume inputs without changing incoming request intent.
        var first = new HttpTriggerSourceContext(new HttpContextAccessor { HttpContext = fixture.Http }, new Access(fixture.Reservation.Scope.Persistence));
        first.Bind(fixture.Reservation); first.CaptureRequest(new(input.Path, input.Method, input.Headers, input.Query, input.Body,
            new Dictionary<string, string> { ["id"] = "42" }), fixture.Reservation.StimulusHash);
        var second = new HttpTriggerSourceContext(new HttpContextAccessor { HttpContext = fixture.Http }, new Access(fixture.Reservation.Scope.Persistence));
        second.Bind(fixture.Reservation); second.CaptureRequest(new(input.Path, input.Method, input.Headers, input.Query, input.Body,
            new Dictionary<string, string> { ["accountId"] = "42" }), fixture.Reservation.StimulusHash);
        var before = await first.GetAsync(); var after = await second.GetAsync();
        Assert.True(JsonElement.DeepEquals(before.SubmittedIntent!.Value, after.SubmittedIntent!.Value));
        Assert.NotEqual(before.WaitCandidate!.Fingerprint, after.WaitCandidate!.Fingerprint);
    }

    [Fact]
    public async Task Http_resource_policy_requires_the_actual_current_route_and_authorized_capture_and_is_cleared_after_transport()
    {
        using var fixture = new Fixture();
        var row = new StandaloneTrigger("trigger", "HTTP", "http", 1, JsonSerializer.SerializeToElement(new { }), 1, 1, 1,
            TriggerDesiredState.Paused, new(TriggerManagementKind.Studio), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "creator", tenantId: fixture.Reservation.Scope.TenantId);
        var signal = new StandaloneTriggerSignal(row.Id, "key", StandaloneHttpTriggerSource.Descriptor.PayloadSchema, JsonSerializer.SerializeToElement(new { }));
        Assert.False(await fixture.SourceContext.AuthorizeAsync(row, signal));
        fixture.SourceContext.Bind(fixture.Reservation);
        Assert.False(await fixture.SourceContext.AuthorizeAsync(row, signal));
        fixture.SourceContext.CaptureRequest(new("orders", "POST", new Dictionary<string, string[]>(), new Dictionary<string, string[]>(), "{}",
            new Dictionary<string, string>()), fixture.Reservation.StimulusHash, []);
        Assert.True(await fixture.SourceContext.AuthorizeAsync(row, signal)); // Pause alone does not remove access to an accepted receipt.
        Assert.False(await fixture.SourceContext.AuthorizeAsync(row, new("another-trigger", "key", signal.PayloadSchema, signal.Payload)));
        fixture.SourceContext.Clear();
        Assert.False(await fixture.SourceContext.AuthorizeAsync(row, signal));
        await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.SourceContext.GetAsync().AsTask());
    }

    [Fact]
    public async Task Anonymous_standalone_route_cannot_resume_a_protected_legacy_wait_or_read_the_request_body()
    {
        using var fixture = new Fixture();
        var now = DateTimeOffset.UtcNow;
        var owner = new WorkflowExecutionState("protected-owner", new("artifact", "definition", "version", "1", "hash"),
            WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, fixture.Reservation.Scope.TenantId,
            new Dictionary<string, string>()) { Partition = new(fixture.Reservation.Scope.Persistence.Value), RunKind = WorkflowRunKind.PublishedRun };
        await fixture.Executions.SaveAsync(owner);
        await fixture.Activities.SaveAsync(new(new("wait-activity", owner.WorkflowExecutionId, "node", "authored", "test", "1"),
            ActivityExecutionStatus.Suspended, null, 1, now, now, null, null, null, null, null, ActivitySchedulingProvenance.Empty,
            null, ["protected-wait"], [], 0, 0, new Dictionary<string, string>()));
        await fixture.Bookmarks.SaveAsync(new("protected-wait", owner.WorkflowExecutionId, "wait-activity", "node", "resume",
            HttpEndpointStimulus.StimulusType, fixture.Reservation.StimulusHash, null,
            new HttpEndpointStimulusOptions(Authorize: true, Policy: "ProtectedWaitPolicy").ToMetadata(), now, null));
        fixture.Http.Request.Body = new UnreadableBody();
        await fixture.InvokeAsync();
        Assert.Equal(401, fixture.Http.Response.StatusCode);
        Assert.Equal(0, fixture.Port.Calls);
        Assert.Empty(fixture.Observer.Items);
        Assert.NotNull(await fixture.Bookmarks.FindAsync(owner.WorkflowExecutionId, "protected-wait"));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
        internal readonly DefaultHttpContext Http = new();
        internal readonly Reader Reader;
        internal readonly Port Port;
        internal readonly Observer Observer = new();
        internal readonly RuntimeIngressReservation Reservation;
        internal readonly RuntimeIngressAdmissionReceipt Receipt;
        internal readonly InMemoryActivityExecutionStateStore Activities = new();
        internal readonly InMemoryBookmarkStateStore Bookmarks = new();
        internal readonly InMemoryWorkflowExecutionStateStore Executions = new();
        internal readonly string CapturedMode;
        internal readonly HttpTriggerSourceContext SourceContext;
        private readonly ServiceProvider services;
        private readonly StandaloneHttpTriggerMiddleware middleware;
        internal Fixture(bool partial = false, string mode = "AsyncFanOut", string body = "{\"amount\":18446744073709551615}", int maxBytes = 1024, TimeSpan? timeout = null)
        {
            CapturedMode = mode;
            var scope = new TriggerScope(new("physical-http-scope"), "actual-tenant");
            Reservation = new("reservation", scope, HttpEndpointRouting.StimulusType, HttpEndpointStimulus.Hash("orders", "POST"),
                RuntimeIngressReservation.StandaloneTriggerOwner, "trigger", 1, 1, new Dictionary<string, string> {
                    [HttpEndpointRouting.TemplateMetadataKey] = "orders", [HttpEndpointRouting.MethodMetadataKey] = "post",
                    [HttpEndpointRouting.AuthorizeMetadataKey] = bool.FalseString, ["http:standaloneMode"] = mode });
            Reader = new([Reservation]);
            var payload = JsonSerializer.SerializeToElement(new { payload = new { amount = 1 }, trigger = new { configuration = new { http = new { mode } } } });
            var actor = new WorkflowExecutionAuthoritySnapshot("standalone-trigger", "source");
            var producer = StandaloneHttpTriggerSource.Descriptor;
            var requestFingerprint = RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope.Persistence, scope.TenantId, "trigger", "Http", "key",
                producer.PayloadSchema, payload, actor);
            var source = new RuntimeStandaloneIngressSource(1, "admission", "dispatch", Now, scope.Persistence, scope.TenantId,
                new(scope.Persistence.Value), "trigger", 1, 1, "Http", "key", requestFingerprint, producer.PayloadSchema, payload,
                partial ? RuntimeIngressAdmissionPolicy.AcceptAvailable : RuntimeIngressAdmissionPolicy.RequireAll, actor);
            var recipient = new RuntimeSubscriptionRecipient("recipient", "private-subscription-a", 1, 1, "private-definition", "native", "slot", 1, "publication",
                new("artifact", "producer", "producer-version", "1", "hash"),
                new("reference", "Published", "published", "1", "private-definition", "version", "1", "publication", "slot"),
                new string('a', 64), 1, 1, new Dictionary<string, JsonElement>(), actor, scope.TenantId, source.Partition,
                new(1, scope.Persistence, source.OutboxItemId, "private-subscription-a"));
            var selection = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, scope.Persistence, [recipient],
                partial ? [new("private-subscription-b", 1, "Admission", "target-unavailable")] : [], [new("recipient", RuntimeRecipientPhase.Pending)]);
            Receipt = new(RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, selection, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1))), false);
            Port = new(Receipt);
            var collection = new ServiceCollection().AddLogging().AddPersistenceCore(scope.Persistence.Value);
            collection.AddSingleton<IActivityExecutionStateStore>(Activities);
            collection.AddSingleton<IWorkflowExecutionStateStore>(Executions);
            collection.AddScoped<HttpResponseInstructionDelivery>();
            services = collection.BuildServiceProvider();
            Http.RequestServices = services;
            Http.Request.Method = "POST"; Http.Request.Path = "/workflows/http/orders";
            Http.Request.ContentType = "application/json"; Http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            Http.Request.Headers["Idempotency-Key"] = "key"; Http.Response.Body = new MemoryStream();
            var accessor = new Access(scope.Persistence);
            SourceContext = new HttpTriggerSourceContext(new HttpContextAccessor { HttpContext = Http }, accessor);
            Port.ReadContext = () => SourceContext.GetAsync();
            middleware = new(new FakeRouteTable("orders"), new TestRouteMatcher(), accessor,
                Microsoft.Extensions.Options.Options.Create(new HttpEndpointOptions { MaxRequestBodyBytes = maxBytes, SyncRequestTimeout = timeout ?? TimeSpan.FromSeconds(3) }),
                SourceContext, NullLogger<StandaloneHttpTriggerMiddleware>.Instance, Reader, Port, Observer,
                bodyParser: new HttpRequestBodyParser(), scopes: services.GetRequiredService<IPersistenceOperationScopeFactory>(),
                waitIndex: Bookmarks, bookmarks: Bookmarks, executions: Executions, activities: Activities, deliveryStore: Port);
        }
        internal Task InvokeAsync() => middleware.InvokeAsync(Http, _ => throw new InvalidOperationException("The reserved source must own the request."));
        internal JsonElement Response() => JsonDocument.Parse(((MemoryStream)Http.Response.Body).ToArray()).RootElement.Clone();
        public void Dispose() => services.Dispose();
    }
    private sealed class UnreadableBody : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Authorization must run before body reads.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new InvalidOperationException("Authorization must run before body reads."));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class Access(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(scope); }
    private sealed class Reader(IReadOnlyList<RuntimeIngressReservation> rows) : IRuntimeIngressReservationReader
    {
        internal IReadOnlyList<RuntimeIngressReservation> Rows = rows;
        public bool SupportsReservations => true;
        public ValueTask<RuntimeIngressReservation?> FindAsync(TriggerScope scope, string type, string hash, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Rows.FirstOrDefault(row => row.Scope == scope && row.StimulusType == type && row.StimulusHash == hash));
        public ValueTask<RuntimeIngressReservationPage> ListAsync(TriggerScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new RuntimeIngressReservationPage(Rows.Where(row => row.Scope == scope).ToArray(), null));
        public ValueTask<RuntimeIngressReservationPage> ListInScopeAsync(PersistenceScope scope, RuntimeIngressReservationQuery query, CancellationToken cancellationToken = default) => ValueTask.FromResult(new RuntimeIngressReservationPage(Rows.Where(row => row.Scope.Persistence == scope && (query.StimulusHash is null || row.StimulusHash == query.StimulusHash)).ToArray(), null));
    }
    private sealed class Port(RuntimeIngressAdmissionReceipt receipt) : IStandaloneTriggerIngress<StandaloneHttpTriggerSource>, IRuntimeIngressDeliveryStore
    {
        internal int Calls; internal StandaloneTriggerSignal? Signal; internal StandaloneTriggerAdmissionRefusedException? Refusal; internal bool Replayed;
        internal Func<ValueTask<StandaloneTriggerInvocationContext>>? ReadContext;
        internal StandaloneTriggerInvocationContext? Invocation;
        public async ValueTask<StandaloneTriggerIngressResult> AdmitAsync(StandaloneTriggerSignal signal, CancellationToken cancellationToken = default)
        {
            Calls++; Signal = signal;
            if (ReadContext is not null) Invocation = await ReadContext();
            if (Refusal is not null) throw Refusal;
            return new StandaloneTriggerIngressResult(
                receipt.Item.StandaloneIngress!.AdmissionId, Replayed, receipt.Item.SubscriptionDelivery!.Recipients.Select(item => item.SubscriptionId).ToArray(),
                receipt.Item.SubscriptionDelivery.Refusals.Select(item => new StandaloneTriggerIngressRefusal(item.SubscriptionId, item.ReasonCode)).ToArray());
        }
        public ValueTask<RuntimePostCommitOutboxItem?> FindDeliveryAsync(TriggerScope scope, string sourceId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimePostCommitOutboxItem?>(receipt.Item.SourceScope == scope.Persistence && receipt.Item.StandaloneIngress!.TenantId == scope.TenantId &&
                receipt.Item.OutboxItemId == sourceId ? receipt.Item : null);
    }
    private sealed class Observer : IRuntimeIngressTransportObserver
    {
        internal bool Supported = true; internal readonly List<RuntimeIngressTransportObservation> Items = [];
        public bool SupportsObservations => Supported;
        public ValueTask RecordAsync(TriggerScope scope, RuntimeIngressTransportObservation observation, CancellationToken cancellationToken = default)
        { Items.Add(observation); return ValueTask.CompletedTask; }
    }
}
