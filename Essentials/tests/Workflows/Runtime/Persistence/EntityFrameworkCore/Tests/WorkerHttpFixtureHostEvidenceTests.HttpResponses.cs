using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CShells;
using CShells.Lifecycle;
using CShells.Features;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class WorkerHttpFixtureHostEvidenceTests
{
    /// <summary>Two independent in-process hosts share only SQLite. This is peer-host evidence, not a separate-process/restart witness.</summary>
    [Fact]
    public async Task Workflow_owned_response_observes_the_other_hosts_committed_instruction_and_replays_without_another_root()
    {
        await using var routing = await StartHostAsync(standaloneHttp: true, automaticResumption: false, suppressHttpPrompt: true);
        var shellA = await routing.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var fixture = await PrepareStandaloneHttpAsync(shellA.ServiceProvider, mode: "WorkflowResponse", targetExecutable: HttpResultExecutable(response: true));
        using var request = StandaloneRequest("peer-response", new { text = "detached-input" });
        var exchange = routing.Client.SendAsync(request);
        var accepted = await AwaitResponseSourceAsync(shellA.ServiceProvider, fixture.Scope);
        await AssertRoutingHostDeferredAsync(shellA.ServiceProvider, accepted.OutboxItemId);
        await using var worker = await StartHostAsync(standaloneHttp: true, automaticResumption: false);
        var shellB = await worker.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        Assert.NotSame(shellA.ServiceProvider, shellB.ServiceProvider);
        await using (var proof = shellB.ServiceProvider.CreateAsyncScope())
            Assert.IsType<RuntimePostCommitOutboxProcessor>(proof.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>());
        await AwaitHttpTargetAcceptanceAsync(shellB.ServiceProvider, accepted.OutboxItemId);
        using var response = await exchange;
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal("peer-response-body", await response.Content.ReadAsStringAsync());
        Assert.Equal("committed", Assert.Single(response.Headers.GetValues("X-Response-Evidence")));
        await using var operation = shellB.ServiceProvider.CreateAsyncScope(); var services = operation.ServiceProvider;
        Assert.Null(services.GetService<IHttpContextAccessor>()?.HttpContext);
        var durable = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(accepted.OutboxItemId))!;
        var recipient = Assert.Single(durable.SubscriptionDelivery!.Recipients);
        var execution = (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(recipient.StartIdentity.WorkflowExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, execution.Status);
        using var replayRequest = StandaloneRequest("peer-response", new { text = "detached-input" });
        using var replay = await routing.Client.SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode); Assert.Equal("peer-response-body", await replay.Content.ReadAsStringAsync());
        Assert.Single((await services.GetRequiredService<IRuntimeIngressDeliveryStore>().ListDeliveriesAsync(fixture.Scope, new(Limit: 100))).Items);
        Assert.Single(await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync());
    }

    [Fact]
    public async Task Missing_workflow_response_without_a_caller_key_returns_the_real_accepted_delivery_id()
    {
        await using var routing = await StartHostAsync(standaloneHttp: true, automaticResumption: false, suppressHttpPrompt: true);
        var shellA = await routing.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var fixture = await PrepareStandaloneHttpAsync(shellA.ServiceProvider, mode: "WorkflowResponse", targetExecutable: HttpResultExecutable(response: false));
        using var request = StandaloneRequest("removed", new { text = "missing-response" }); request.Headers.Remove("Idempotency-Key");
        var exchange = routing.Client.SendAsync(request);
        var accepted = await AwaitResponseSourceAsync(shellA.ServiceProvider, fixture.Scope);
        await AssertRoutingHostDeferredAsync(shellA.ServiceProvider, accepted.OutboxItemId);
        await using var worker = await StartHostAsync(standaloneHttp: true, automaticResumption: false);
        var shellB = await worker.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        Assert.NotSame(shellA.ServiceProvider, shellB.ServiceProvider);
        await using (var proof = shellB.ServiceProvider.CreateAsyncScope())
            Assert.IsType<RuntimePostCommitOutboxProcessor>(proof.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>());
        await AwaitHttpTargetAcceptanceAsync(shellB.ServiceProvider, accepted.OutboxItemId);
        using var response = await exchange; Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("ResponseMissing", problem.GetProperty("code").GetString());
        Assert.Equal(accepted.OutboxItemId, problem.GetProperty("deliveryId").GetString());
        await using var operation = shellB.ServiceProvider.CreateAsyncScope(); var services = operation.ServiceProvider;
        var recipient = Assert.Single(accepted.SubscriptionDelivery!.Recipients);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await services.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(recipient.StartIdentity.WorkflowExecutionId))!.Status);
    }

    [Fact]
    public async Task Response_timeout_without_a_caller_key_preserves_accepted_work_for_later_execution()
    {
        await using var routing = await StartHostAsync(standaloneHttp: true, automaticResumption: false, responseTimeoutSeconds: 0.2, suppressHttpPrompt: true);
        var shellA = await routing.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        var fixture = await PrepareStandaloneHttpAsync(shellA.ServiceProvider, mode: "WorkflowResponse");
        using var request = StandaloneRequest("removed", new { text = "timeout-input" }); request.Headers.Remove("Idempotency-Key");
        using var response = await routing.Client.SendAsync(request); Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(); var id = Assert.IsType<string>(problem.GetProperty("deliveryId").GetString());
        await AssertRoutingHostDeferredAsync(shellA.ServiceProvider, id);
        await using var worker = await StartHostAsync(standaloneHttp: true, automaticResumption: false);
        var shellB = await worker.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using (var proof = shellB.ServiceProvider.CreateAsyncScope())
            Assert.IsType<RuntimePostCommitOutboxProcessor>(proof.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>());
        await AwaitHttpTargetAcceptanceAsync(shellB.ServiceProvider, id);
        await using var operation = shellB.ServiceProvider.CreateAsyncScope(); var services = operation.ServiceProvider;
        var source = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(id))!;
        Assert.Contains(source.SubscriptionDelivery!.ControlFacts!.Transport, fact => fact.State == RuntimeIngressTransportState.TimedOut);
        var recipient = Assert.Single(source.SubscriptionDelivery.Recipients);
        var execution = (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(recipient.StartIdentity.WorkflowExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Running, execution.Status);
        Assert.Single(await services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(execution.WorkflowExecutionId));
    }

    // Fault injection only defers host A's prompt. Admission, schema checks, transport observation and all
    // stores remain real; host B uses the ordinary processor and physical claims to create the root.
    private static async Task AssertRoutingHostDeferredAsync(IServiceProvider shell, string sourceId)
    {
        await using var operation = shell.CreateAsyncScope(); var services = operation.ServiceProvider;
        await services.GetRequiredService<HttpPromptSuppressionEvidence>().Observed.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var source = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(sourceId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Pending, source.Status);
        Assert.DoesNotContain(source.SubscriptionDelivery!.Progress, item => item.Phase == RuntimeRecipientPhase.TargetAccepted);
        Assert.Empty(await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync());
    }

    [ShellFeature(Name, DependsOn = new object[] { "WorkflowsRuntimeApi", "WorkflowsRuntimeEntityFrameworkCore", "ActivitiesHttp" })]
    public sealed class HttpPromptSuppressionFeature : IShellFeature
    {
        public const string Name = "HttpResponseTestsDeferRoutingHostPrompt";
        public void ConfigureServices(IServiceCollection services)
        {
            var original = Assert.Single(services.Where(item => item.ServiceType == typeof(IRuntimePostCommitOutboxProcessor)));
            Assert.NotNull(original.ImplementationFactory);
            services.Remove(original);
            services.AddSingleton<HttpPromptSuppressionEvidence>();
            services.AddScoped<IRuntimePostCommitOutboxProcessor>(provider => new DeferredHttpPromptProcessor(
                (IRuntimePostCommitOutboxProcessor)original.ImplementationFactory!(provider),
                provider.GetRequiredService<HttpPromptSuppressionEvidence>()));
        }
    }

    private sealed class HttpPromptSuppressionEvidence
    {
        public TaskCompletionSource Observed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DeferredHttpPromptProcessor(IRuntimePostCommitOutboxProcessor inner, HttpPromptSuppressionEvidence evidence)
        : IRuntimePostCommitOutboxProcessor
    {
        public bool SupportsSourceKind(RuntimeDeliverySourceKind kind) => inner.SupportsSourceKind(kind);
        public bool SupportsSubscriptionDelivery(RuntimeDeliverySourceKind kind, string deliveryKind, int version) =>
            inner.SupportsSubscriptionDelivery(kind, deliveryKind, version);
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evidence.Observed.TrySetResult();
            return ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult([]));
        }
    }

    private static async Task<RuntimePostCommitOutboxItem> AwaitResponseSourceAsync(IServiceProvider shell, TriggerScope scope)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(60))
        {
            await using var operation = shell.CreateAsyncScope();
            var page = await operation.ServiceProvider.GetRequiredService<IRuntimeIngressDeliveryStore>().ListDeliveriesAsync(scope, new(Limit: 100));
            if (page.Items.Count > 0) return Assert.Single(page.Items);
            await Task.Delay(25);
        }
        throw new TimeoutException("The HTTP exchange did not durably accept its source.");
    }

    private static WorkflowExecutable HttpResultExecutable(bool response)
    {
        var type = response ? typeof(WriteHttpResponse) : typeof(WriteLine);
        var original = ClrActivityContractTestBuilder.BuildContract(type); var alias = TypeAliasConvention.CanonicalAlias(type);
        var contract = new ActivityContract(alias, original.ContractVersion, original.DescriptorKind, original.DescriptorPayload,
            original.Inputs.Values, original.Result, original.Outcomes, original.Activation);
        RuntimeInputBinding Literal(string key, object value)
        {
            var field = contract.Inputs[key]; var policy = ValueProtectionPolicy.InstanceInline;
            return new(key, field.Type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(field.Type, JsonSerializer.SerializeToElement(value), policy));
        }
        var values = response ? new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(WriteHttpResponse.StatusCode)] = Literal(nameof(WriteHttpResponse.StatusCode), 201),
            [nameof(WriteHttpResponse.Body)] = Literal(nameof(WriteHttpResponse.Body), "peer-response-body"),
            [nameof(WriteHttpResponse.Headers)] = Literal(nameof(WriteHttpResponse.Headers), new Dictionary<string, string[]> { ["X-Response-Evidence"] = ["committed"] })
        } : new Dictionary<string, RuntimeInputBinding> { ["text"] = Literal("text", "completed-without-response") };
        var node = new ExecutableNode("business-root", "business-root", alias, response ? "1.2.0" : "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, contract.DescriptorPayload,
            ClrActivityContractTestBuilder.CompleteInputBindings(contract, values), new Dictionary<string, string>(), activityContract: contract);
        return new(new("http-result-material", "business", "version", "1", response ? "reply" : "no-reply"), node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }
}
