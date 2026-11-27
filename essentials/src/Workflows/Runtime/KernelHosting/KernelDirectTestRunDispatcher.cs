using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelApi;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.Services.Executions;
using Kernel.Foundation.Contracts;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Admission = Kernel.Foundation.Contracts.Admission;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal delegate ValueTask<Admission> StartDirectKernelTestRun(string operationId, StartSpec start, CancellationToken token);

/// <summary>Admits the exact material and Runtime scope of an ordinary direct authoring Test Run.</summary>
internal sealed class KernelDirectTestRunDispatcher(IServiceScopeFactory scopes, KernelServing serving, PersistenceScope persistence,
    StartDirectKernelTestRun start, KernelTestRuns testRuns, KernelOperatorIngress operators, TimeProvider time) : IWorkflowDirectTestRunDispatcher
{
    private static readonly TimeSpan StopBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    public bool SupportsTrigger(string driverId) => driverId == "Elsa.Event";

    public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.TestRun,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var triggered = request.TriggerNodeId is not null;
        var ordinary = request is { TriggerNodeId: null, StimulusInput: null, IdempotencyKey: null, WorkflowExecutionId: null };
        var prepared = request is { TriggerNodeId: not null, IdempotencyKey: not null, WorkflowExecutionId: not null };
        if (requiredScope != WorkflowExecutableReferenceScope.TestRun || (!ordinary && !prepared) ||
            request is not { RunKind: WorkflowRunKind.TestRun, TestScope: { } test, SourceSelection.SourceReferenceId: { },
                DispatchNestingDepth: 0, ParentWorkflowExecutionId: null, StartAuthority: null } ||
            (test.TenantId ?? PersistenceScope.DefaultValue) != persistence.Value)
            throw new InvalidOperationException("The direct Test Run must select an exact source and the owning Runtime scope.");

        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var services = scope.ServiceProvider;
        var clock = services.GetRequiredService<TimeProvider>();
        var executable = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(request.ArtifactId, cancellationToken)
            ?? throw new WorkflowExecutableNotFoundException(request.ArtifactId);
        var sources = new WorkflowExecutableStartSourceResolver(services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), clock);
        var source = await sources.ResolveDirectTestAsync(request.ArtifactId, request.SourceSelection, persistence, cancellationToken);
        var stored = await services.GetRequiredService<IWorkflowTestScopeStore>().FindAsync(test.ScopeId, cancellationToken);
        if (stored is not { State: WorkflowTestScopeState.Open } || !WorkflowTestScope.ContextEquals(stored.Scope, test) ||
            source.SourceReferenceId != test.ScopeId || source.TenantId != test.TenantId ||
            source.ExpiresAt != test.ExpiresAt || test.IsExpired(clock.GetUtcNow()))
            throw new WorkflowExecutableReferenceRejectedException(request.ArtifactId, requiredScope,
                WorkflowExecutableReferenceRejectionReason.NoLiveReference);

        var inputs = WorkflowStartDispatcher.ApplyInputContract(executable, request.Inputs,
            services.GetRequiredService<IWorkflowExecutableInputValidator>());
        var policies = services.GetServices<IWorkflowExecutableStartPolicy>().ToArray();
        if (policies is not [var policy])
            throw new InvalidOperationException($"Runtime composition requires exactly one {nameof(IWorkflowExecutableStartPolicy)} registration, but found {policies.Length}.");
        await WorkflowStartDispatcher.EnforceStartPolicyAsync(request, executable.Identity, test.Partition, policy, cancellationToken);

        var provenance = WorkflowExecutableSourceProvenance.From(source);
        var mode = services.GetRequiredService<IRuntimePersistenceModeResolver>().Resolve(executable);
        if (mode == PersistenceMode.Ephemeral) return Refused("EphemeralTestRunUnavailable");

        Workflow workflow;
        try
        {
            workflow = NativeExecutableMapper.Map(executable, serving.Capabilities with { Children = false }, source.DefinitionId);
        }
        catch (NativeMappingRefusedException error)
        {
            return Refused(error.Message);
        }

        var input = new JsonObject();
        foreach (var (name, value) in inputs) input.Add(name, JsonNode.Parse(value.GetRawText()));
        if (triggered && !NativeStartTriggers.StartEvents(workflow).Any(node => node.Id == request.TriggerNodeId))
            return Refused("The selected node is not a startable named-event trigger in this executable.");
        var subject = request.Authority.Metadata.GetValueOrDefault("elsa.identity.subject-kind") == "persisted-user"
            ? request.Authority.Metadata.GetValueOrDefault("elsa.identity.subject-id") : null;
        var selection = new WorkflowTestStart(test.ScopeId, test.ExpiresAt, test.TenantId, test.Partition.Value,
            source.SourceReferenceId, source.SourceKind, source.SourceId, source.SourceVersion, source.DefinitionVersionId,
            source.ArtifactVersion, KernelPublishedRunDispatcher.SourceDigest(source), subject);
        var stimulus = request.StimulusInput is { } payload ? JsonNode.Parse(payload.GetRawText()) : null;
        var native = NativeExecutionModel.Start(workflow.Id, mode.ToString(), input, request.TriggerNodeId, stimulus, workflow.Definition)
            with { ExecutableSelection = workflow.ExecutableSelection };
        var spec = WorkflowActor.Start(native, null, selection);
        var admission = await start(test.ScopeId, spec, cancellationToken);
        return KernelPublishedRunDispatcher.Result(test.ScopeId, executable.Identity, provenance, admission, clock.GetUtcNow());

        WorkflowExecutionStartDispatchResult Refused(string reason) => KernelPublishedRunDispatcher.Result(
            test.ScopeId, executable.Identity, provenance, new(AdmissionOutcome.Refused), clock.GetUtcNow(), reason);
    }

    public async ValueTask RequestStopAsync(WorkflowTestScope scope, string workflowExecutionId,
        CancellationToken cancellationToken = default)
    {
        await using var runtime = await scopes.CreateAsyncScopeAsync(persistence);
        var current = await runtime.ServiceProvider.GetRequiredService<IWorkflowTestScopeStore>()
            .FindAsync(scope.ScopeId, cancellationToken);
        if (current is not { State: WorkflowTestScopeState.Closing or WorkflowTestScopeState.Closed } ||
            !WorkflowTestScope.ContextEquals(current.Scope, scope))
            throw new TestScopeAdmissionException("Only the exact closed Test Run scope can stop its test actor.");

        var actor = new ActorRef(workflowExecutionId);
        var tenant = testRuns.Reads.Tenant;
        using var budget = new CancellationTokenSource(StopBudget, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var token = linked.Token;
        try
        {
            var ingress = await operators.Open((binding, target, kind) =>
                binding.Tenant == tenant.Tenant && target == actor && kind == Kinds.Terminate, token);
            while (true)
            {
                var view = await FindActorAsync(scope, actor, token);
                if (IsStopped(view)) return;
                if (view?.Selection is { } selection)
                {
                    var deadline = checked(await operators.ReadClock(token) + (long)StopBudget.TotalMilliseconds);
                    var command = new OperatorCommand(selection, ExpectedTarget: actor);
                    var reply = await ingress.RequestAsync(tenant, new Target.Ref(actor), Guid.NewGuid().ToString("N"),
                        Kinds.Terminate, JsonSerializer.Serialize(command), null, new(null, deadline), token);
                    if (reply is { Error: null, Item: "terminated" or "terminated and kept" }) return;
                }
                await Task.Delay(RetryDelay, time, token);
            }
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("The Test Run stop did not settle. Retry Stop for the selected test run.");
        }
    }

    public async ValueTask<bool> IsStoppedAsync(WorkflowTestScope scope, string workflowExecutionId,
        CancellationToken cancellationToken = default) =>
        IsStopped(await FindActorAsync(scope, new(workflowExecutionId), cancellationToken));

    private async ValueTask<ActorView?> FindActorAsync(WorkflowTestScope scope, ActorRef actor, CancellationToken token)
    {
        var run = await testRuns.Reads.GetAsync(actor, token);
        return run.Run is { Run.TestStart: { } selection } test && selection.ScopeId == scope.ScopeId &&
            selection.Tenant == scope.TenantId && selection.Partition == scope.Partition.Value &&
            selection.ExpiresAt == scope.ExpiresAt ? test.View : null;
    }

    private static bool IsStopped(ActorView? view) =>
        view is { Header.Status: not ActorStatus.Running, Park: null };
}
