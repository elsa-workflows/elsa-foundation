using Elsa.Workflows.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

public sealed record StartWorkflowTestRun(string VersionId) : IRequest<WorkflowTestRunView>;

public sealed record StartWorkflowDraftTestRun(
    string DefinitionId,
    string SnapshotId,
    WorkflowDefinitionState State,
    string? ArtifactVersion = null) : IRequest<WorkflowTestRunView>;
