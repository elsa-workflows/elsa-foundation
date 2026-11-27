using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
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
    StartDirectKernelTestRun start) : IWorkflowDirectTestRunDispatcher
{
    public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.TestRun,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (requiredScope != WorkflowExecutableReferenceScope.TestRun ||
            request is not { RunKind: WorkflowRunKind.TestRun, TestScope: { } test, SourceSelection.SourceReferenceId: { },
                TriggerNodeId: null, StimulusInput: null, DispatchNestingDepth: 0, ParentWorkflowExecutionId: null,
                StartAuthority: null, IdempotencyKey: null, WorkflowExecutionId: null } ||
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
        var subject = request.Authority.Metadata.GetValueOrDefault("elsa.identity.subject-kind") == "persisted-user"
            ? request.Authority.Metadata.GetValueOrDefault("elsa.identity.subject-id") : null;
        var selection = new WorkflowTestStart(test.ScopeId, test.ExpiresAt, test.TenantId, test.Partition.Value,
            source.SourceReferenceId, source.SourceKind, source.SourceId, source.SourceVersion, source.DefinitionVersionId,
            source.ArtifactVersion, KernelPublishedRunDispatcher.SourceDigest(source), subject);
        var spec = WorkflowActor.Start(NativeExecutionModel.Start(workflow, mode.ToString(), input), null, selection);
        var admission = await start(test.ScopeId, spec, cancellationToken);
        return KernelPublishedRunDispatcher.Result(test.ScopeId, executable.Identity, provenance, admission, clock.GetUtcNow());

        WorkflowExecutionStartDispatchResult Refused(string reason) => KernelPublishedRunDispatcher.Result(
            test.ScopeId, executable.Identity, provenance, new(AdmissionOutcome.Refused), clock.GetUtcNow(), reason);
    }
}
