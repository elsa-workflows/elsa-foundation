using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class PublishWorkflowRequestHandler(
    IWorkflowExecutableCompiler compiler,
    IWorkflowExecutableStore executableStore,
    IWorkflowTriggerIndexer triggerIndexer)
    : IRequestHandler<PublishWorkflow, PublishedWorkflowView>
{
    private const string PublishedArtifactPrefix = "artifact-";

    public async Task<PublishedWorkflowView> Handle(PublishWorkflow request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        WorkflowExecutable executable;
        try
        {
            executable = await compiler.CompileAsync(
                new WorkflowExecutableCompileRequest(
                    request.VersionId,
                    WorkflowExecutableScope.Published,
                    now,
                    now,
                    ExpiresAt: null,
                    PublishedArtifactPrefix,
                    new Dictionary<string, string>
                    {
                        ["slice"] = "workflow-execution-vertical-slice"
                    }),
                cancellationToken);
        }
        catch (WorkflowExecutableCompilationException exception)
        {
            throw new ArgumentException(exception.Message, exception);
        }

        await executableStore.SaveAsync(executable, cancellationToken);

        // Index this artifact's start-triggers within the publish flow (W7, E3-1). A failure here propagates and
        // fails the publish by design: a silently unindexed published trigger — one that can never start a
        // workflow — is a worse outcome than a failed publish the caller can retry (indexing is idempotent).
        await triggerIndexer.IndexAsync(executable, cancellationToken);

        return PublishedWorkflowView.From(executable);
    }
}
