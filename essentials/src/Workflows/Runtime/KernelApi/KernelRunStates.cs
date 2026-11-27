using Elsa.Workflows.Runtime.Core.Models;
using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.KernelApi;

/// <summary>A kernel run as the runtime API's reads see a workflow execution.</summary>
internal static class KernelRunStates
{
    /// <summary>The start of a run whose start is unknown.</summary>
    internal static readonly DateTimeOffset UnknownStart = DateTimeOffset.UnixEpoch;

    internal static bool CanReadTenant(PersistenceAccessContext? access, BoundScope tenant) =>
        access is { AccessPolicy: PersistenceAccessPolicy.Ordinary, AcrossScopes: false, Scope.Value: { } scope } &&
        string.Equals(scope, tenant.Tenant, StringComparison.Ordinal);

    /// <summary>
    /// The execution state of a kernel run: its actor reference as the execution id; the executable it pins, from the
    /// executable store when it holds the artifact (an artifact identity of the run's workflow and definition otherwise);
    /// its status, where Finished is Completed and a suspended run is Suspended; and its start and end.
    /// </summary>
    internal static WorkflowExecutionState From(ActorView view, WorkflowRunHeader run, WorkflowExecutableIdentity? identity, string tenant)
    {
        var header = view.Header;
        DateTimeOffset? ended = header.EndedAt is { } at ? DateTimeOffset.FromUnixTimeMilliseconds(at) : null;
        // A run whose header holds no start (one started before the actor kept it) has an unknown start: the epoch, which
        // is stable across reads, so the run keeps its place in pages and windows.
        var created = run.StartedAt ?? UnknownStart;
        var publication = run.PublishedStart;
        var test = run.TestStart;
        return new WorkflowExecutionState(
            header.Ref.Id,
            // An artifact the store does not hold has no version or hash to show.
            identity ?? new WorkflowExecutableIdentity(run.Workflow, run.Definition, "", "", ""),
            Status(header.Status, run.Suspended),
            run.SubStatus,
            created,
            run.StartedAt,
            ended,
            ended,
            CorrelationId: null,
            ParentWorkflowExecutionId: header.Parent?.Id,
            TenantId: tenant,
            SystemMetadata: new Dictionary<string, string>())
        {
            RunKind = test is null ? WorkflowRunKind.PublishedRun : WorkflowRunKind.TestRun,
            PinnedSource = test is not null ? new(test.SourceReferenceId, test.SourceKind, test.SourceId, test.SourceVersion,
                run.Definition, test.DefinitionVersionId, test.ArtifactVersion, PublicationId: null, SlotId: null)
                : publication is null ? null : new(publication.SourceReferenceId, publication.SourceKind, publication.SourceId,
                    publication.SourceVersion, run.Definition, publication.DefinitionVersionId, publication.ArtifactVersion,
                    publication.ActivationId, publication.SlotId),
            Partition = test is not null ? new(test.Partition) : publication is null ? null : new(tenant),
            Authority = test is not null ? Authority("workflow-designer-test-run", test.SubjectId)
                : publication is null ? null : Authority("runtime-api", publication.SubjectId),
            TestScope = test is not null ? new(test.ScopeId, test.ExpiresAt, test.Tenant, new(test.Partition)) : null
        };
    }

    private static WorkflowExecutionAuthoritySnapshot Authority(string requestedBy, string? subjectId) =>
        WorkflowExecutionAuthoritySnapshot.CreateRoot(requestedBy, subjectId is null ? null : new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["elsa.identity.subject-kind"] = "persisted-user",
            ["elsa.identity.subject-id"] = subjectId
        });

    internal static WorkflowExecutionStatus Status(ActorStatus status, bool suspended) => status switch
    {
        ActorStatus.Running => suspended ? WorkflowExecutionStatus.Suspended : WorkflowExecutionStatus.Running,
        ActorStatus.Finished => WorkflowExecutionStatus.Completed,
        ActorStatus.Faulted => WorkflowExecutionStatus.Faulted,
        ActorStatus.Cancelled => WorkflowExecutionStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "An actor status this list does not map.")
    };
}
