using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Elsa.Workflows.Runtime.Services.Executions;
using Kernel.Foundation.Contracts;
using Kernel.Foundation.Hosting;
using Kernel.Native;
using Kernel.WorkflowActor;
using Microsoft.Extensions.DependencyInjection;
using Admission = Kernel.Foundation.Contracts.Admission;

namespace Elsa.Workflows.Runtime.KernelHosting;

internal delegate ValueTask<Admission> StartPublishedKernelRun(string operationId, StartSpec start, CancellationToken token);
internal delegate ValueTask<InlineStartOutcome> StartPublishedInlineRun(string operationId, StartSpec start, Workflow executable,
    CancellationToken token);

/// <summary>Admits ordinary published Execute requests through the installed actor foundation.</summary>
internal sealed class KernelPublishedRunDispatcher(IServiceScopeFactory scopes, KernelServing serving, PersistenceScope persistence,
    StartPublishedKernelRun start, StartPublishedInlineRun? inline = null) : IWorkflowStartDispatcher
{
    public async ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
        WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requiredScope != WorkflowExecutableReferenceScope.Published || request.RunKind != WorkflowRunKind.PublishedRun ||
            (request.TenantId ?? PersistenceScope.DefaultValue) != persistence.Value)
            throw new InvalidOperationException("The kernel execute dispatcher requires a published request bound to this shell.");

        await using var scope = await scopes.CreateAsyncScopeAsync(persistence);
        var services = scope.ServiceProvider;
        var clock = services.GetRequiredService<TimeProvider>();
        var executable = await services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(request.ArtifactId, cancellationToken)
            ?? throw new WorkflowExecutableNotFoundException(request.ArtifactId);
        var sources = new WorkflowExecutableStartSourceResolver(services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>(), clock);
        var authority = services.GetRequiredService<IWorkflowActivationAuthority>();
        var source = await sources.ResolvePublishedAsync(request.ArtifactId, request.SourceSelection, persistence, cancellationToken);
        var published = await ReadServedAsync(source);

        var inputs = WorkflowStartDispatcher.ApplyInputContract(executable, request.Inputs,
            services.GetRequiredService<IWorkflowExecutableInputValidator>());
        var policies = services.GetServices<IWorkflowExecutableStartPolicy>().ToArray();
        if (policies is not [var policy])
            throw new InvalidOperationException($"Runtime composition requires exactly one {nameof(IWorkflowExecutableStartPolicy)} registration, but found {policies.Length}.");
        await WorkflowStartDispatcher.EnforceStartPolicyAsync(request, executable.Identity, new(persistence.Value), policy, cancellationToken);

        var input = new JsonObject();
        foreach (var (name, value) in inputs) input.Add(name, JsonNode.Parse(value.GetRawText()));
        var mode = services.GetRequiredService<IRuntimePersistenceModeResolver>().Resolve(executable);

        // Recheck after policy and input work, using the physical publication ledger as well as the served slot.
        var selected = await sources.ResolvePublishedAsync(request.ArtifactId, request.SourceSelection, persistence, cancellationToken);
        if (!WorkflowExecutableSourceReferenceComparer.SameSnapshot(source, selected) || await ReadServedAsync(selected) != published)
            throw Rejected();

        var subjectId = request.Authority.Metadata.GetValueOrDefault("elsa.identity.subject-kind") == "persisted-user"
            ? request.Authority.Metadata.GetValueOrDefault("elsa.identity.subject-id") : null;
        var publication = new WorkflowPublishedStart(source.SourceReferenceId, source.SourceKind, source.SourceId, source.SourceVersion,
            source.DefinitionVersionId, source.ArtifactVersion, published.ActivationId, published.SlotId, published.SlotName,
            published.Revision, SourceDigest(source), subjectId);
        var spec = WorkflowActor.Start(NativeExecutionModel.Start(published.Workflow, mode.ToString(), input), publication);

        var operationId = Guid.NewGuid().ToString("N");
        var provenance = WorkflowExecutableSourceProvenance.From(source);
        if (mode == PersistenceMode.Ephemeral)
        {
            if (inline is null) return Result(operationId, executable.Identity, provenance, new(AdmissionOutcome.Refused), clock.GetUtcNow(),
                "EphemeralPublishedStartUnavailable");
            var outcome = await inline(operationId, spec, published.Workflow, cancellationToken);
            return InlineResult(operationId, executable.Identity, provenance, outcome, clock.GetUtcNow());
        }
        var receipt = await start(operationId, spec, cancellationToken);
        return Result(operationId, executable.Identity, provenance, receipt, clock.GetUtcNow());

        async ValueTask<KernelServedSlot> ReadServedAsync(WorkflowExecutableSourceReference reference)
        {
            if (reference is not { SlotId: { } slotId, ActivationId: { } activation } ||
                reference.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(activation) ||
                serving.Find(slotId) is not { } served || served.DefinitionId != reference.DefinitionId ||
                served.Workflow.Definition != reference.DefinitionId || served.ActivationId != activation ||
                served.Workflow.Id != executable.Identity.ArtifactId ||
                NativeExecutableMapper.ReadMaterialSelection(served.Workflow.ExecutableSelection, request.ArtifactId) != executable.Identity)
                throw Rejected();

            var current = await authority.FindAsync(reference.DefinitionId, served.SlotName, cancellationToken);
            if (current is null || current.SlotId != slotId || current.ActiveActivationId != activation ||
                current.Revision != served.Revision || serving.Find(slotId) != served)
                throw Rejected();
            return served;
        }

        WorkflowExecutableReferenceRejectedException Rejected() => new(request.ArtifactId, requiredScope,
            WorkflowExecutableReferenceRejectionReason.NoLiveReference);
    }

    internal static string SourceDigest(WorkflowExecutableSourceReference source) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source)));

    internal static WorkflowExecutionStartDispatchResult Result(string operationId, WorkflowExecutableIdentity material,
        WorkflowExecutableSourceProvenance source, Admission admission, DateTimeOffset recordedAt, string? refusal = null,
        WorkflowTestRunHttpReply? httpReply = null)
    {
        var pressure = admission is { Outcome: AdmissionOutcome.Refused,
            Reason: RefusalReason.RateLimited or RefusalReason.NodeBusy or RefusalReason.InboxFull };
        var (status, id) = admission switch
        {
            { Outcome: AdmissionOutcome.Accepted, Actor: { } actor } => (WorkflowExecutionCommandDispatchStatus.Accepted, actor.Id),
            { Outcome: AdmissionOutcome.Duplicate, Actor: { } actor } => (WorkflowExecutionCommandDispatchStatus.Duplicate, actor.Id),
            { Outcome: AdmissionOutcome.Refused } => (pressure ? WorkflowExecutionCommandDispatchStatus.Deferred :
                WorkflowExecutionCommandDispatchStatus.Rejected, operationId),
            _ => throw new InvalidOperationException("The Actor Foundation returned an invalid start receipt.")
        };
        var reason = admission.Outcome == AdmissionOutcome.Refused ? refusal ?? admission.Reason?.ToString() ?? "StartRefused" : null;
        var metadata = pressure ? new Dictionary<string, string>
        {
            [RuntimeMetadataKeys.DispatchShed] = "true",
            [RuntimeMetadataKeys.DispatchRetryAfterSeconds] = RetrySeconds(admission.RetryAfter)
        } : null;
        return CreateResult(operationId, id, material, source, status, recordedAt, reason, metadata, httpReply);
    }

    private static WorkflowExecutionStartDispatchResult InlineResult(string operationId, WorkflowExecutableIdentity material,
        WorkflowExecutableSourceProvenance source, InlineStartOutcome outcome, DateTimeOffset recordedAt)
    {
        var (status, reason) = outcome.Disposition switch
        {
            InlineStartDisposition.Volatile or InlineStartDisposition.Committed when outcome.Status == ActorStatus.Faulted =>
                (WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, "WorkflowFaulted"),
            InlineStartDisposition.Volatile or InlineStartDisposition.Committed => (WorkflowExecutionCommandDispatchStatus.Accepted, null),
            InlineStartDisposition.Refused => (WorkflowExecutionCommandDispatchStatus.Rejected, outcome.RefusalReason?.ToString() ?? "InlineStartRefused"),
            InlineStartDisposition.Unavailable => (WorkflowExecutionCommandDispatchStatus.Deferred, outcome.RefusalReason?.ToString() ?? nameof(RefusalReason.NodeBusy)),
            InlineStartDisposition.Unknown => (WorkflowExecutionCommandDispatchStatus.Deferred, "InlineStartOutcomeUnknown"),
            _ => throw new InvalidOperationException("The Actor Foundation returned an invalid inline outcome.")
        };
        var hasRun = outcome.Disposition == InlineStartDisposition.Committed;
        if (hasRun != outcome.Actor.HasValue) throw new InvalidOperationException("Only a committed inline outcome has an actor.");
        var id = outcome.Actor?.Id ?? operationId;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [WorkflowExecutionStartDispatchView.PersistenceOutcomeKey] = outcome.Disposition.ToString()
        };
        if (outcome.Status is { } actorStatus) metadata[WorkflowExecutionStartDispatchView.StartActorStatusKey] = actorStatus.ToString();
        if (outcome.Disposition == InlineStartDisposition.Unavailable)
        {
            metadata[RuntimeMetadataKeys.DispatchShed] = "true";
            metadata[RuntimeMetadataKeys.DispatchRetryAfterSeconds] = RetrySeconds(outcome.RetryAfter);
        }
        return CreateResult(operationId, id, material, source, status, recordedAt, reason, metadata);
    }

    private static string RetrySeconds(TimeSpan? retryAfter) =>
        Math.Ceiling(Math.Clamp(retryAfter?.TotalSeconds ?? 1, 1, int.MaxValue)).ToString(CultureInfo.InvariantCulture);

    private static WorkflowExecutionStartDispatchResult CreateResult(string operationId, string id, WorkflowExecutableIdentity material,
        WorkflowExecutableSourceProvenance source, WorkflowExecutionCommandDispatchStatus status, DateTimeOffset recordedAt,
        string? reason, IReadOnlyDictionary<string, string>? metadata, WorkflowTestRunHttpReply? httpReply = null) =>
        new(id, material, new(operationId, id, status, recordedAt, reason, metadata),
            new(id, id, "actor-foundation", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.None, recordedAt), source)
            { TestRunHttpReply = httpReply };
}
