using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class PublishWorkflowRequestHandler(
    IWorkflowExecutableCompiler compiler,
    IWorkflowExecutableStore executableStore)
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

        return PublishedWorkflowView.From(executable);
    }
}
