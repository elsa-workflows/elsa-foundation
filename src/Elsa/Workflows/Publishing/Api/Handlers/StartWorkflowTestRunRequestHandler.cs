using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class StartWorkflowTestRunRequestHandler(
    IWorkflowExecutableCompiler compiler,
    ITransientWorkflowExecutableStore transientExecutableStore,
    IWorkflowTestRunStore testRunStore,
    IWorkflowExecutionStartDispatcher startDispatcher,
    TimeProvider timeProvider)
    : IRequestHandler<StartWorkflowTestRun, WorkflowTestRunView>
{
    public const string RequestedBy = "workflow-designer-test-run";
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);
    private const string TestArtifactPrefix = "test-artifact-";

    public StartWorkflowTestRunRequestHandler(
        IWorkflowExecutableCompiler compiler,
        ITransientWorkflowExecutableStore transientExecutableStore,
        IWorkflowTestRunStore testRunStore,
        IWorkflowExecutionStartDispatcher startDispatcher)
        : this(compiler, transientExecutableStore, testRunStore, startDispatcher, TimeProvider.System)
    {
    }

    public async Task<WorkflowTestRunView> Handle(StartWorkflowTestRun request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(DefaultRetention);
        var testRunId = $"testrun-{Guid.NewGuid():N}";

        WorkflowExecutable executable;
        try
        {
            executable = await compiler.CompileAsync(
                new WorkflowExecutableCompileRequest(
                    request.VersionId,
                    WorkflowExecutableScope.TransientTestRun,
                    now,
                    PublishedAt: null,
                    expiresAt,
                    TestArtifactPrefix,
                    new Dictionary<string, string>
                    {
                        ["runtime.scope"] = "test-run",
                        ["runtime.testRunId"] = testRunId
                    }),
                cancellationToken);
        }
        catch (WorkflowExecutableCompilationException exception)
        {
            var rejected = new WorkflowTestRun(
                TestRunId: testRunId,
                DefinitionId: exception.DefinitionId ?? request.VersionId,
                DefinitionVersionId: exception.DefinitionVersionId ?? request.VersionId,
                ArtifactId: null,
                WorkflowExecutionId: null,
                Status: WorkflowTestRunStatus.Rejected,
                RequestedBy: RequestedBy,
                RequestedAt: now,
                ExpiresAt: null,
                Reason: exception.Message,
                Metadata: new Dictionary<string, string>());

            await testRunStore.SaveAsync(rejected, cancellationToken);
            return WorkflowTestRunView.From(rejected);
        }
        catch (ArgumentException exception)
        {
            var rejected = new WorkflowTestRun(
                TestRunId: testRunId,
                DefinitionId: request.VersionId,
                DefinitionVersionId: request.VersionId,
                ArtifactId: null,
                WorkflowExecutionId: null,
                Status: WorkflowTestRunStatus.Rejected,
                RequestedBy: RequestedBy,
                RequestedAt: now,
                ExpiresAt: null,
                Reason: exception.Message,
                Metadata: new Dictionary<string, string>());

            await testRunStore.SaveAsync(rejected, cancellationToken);
            return WorkflowTestRunView.From(rejected);
        }

        await transientExecutableStore.SaveAsync(executable, cancellationToken);

        var dispatch = await startDispatcher.DispatchTransientAsync(
            new WorkflowExecutionStartDispatchRequest(
                executable.Identity.ArtifactId,
                RequestedBy,
                metadata: new Dictionary<string, string>
                {
                    ["runtime.scope"] = "test-run",
                    ["runtime.testRunId"] = testRunId,
                    ["runtime.sourceDefinitionId"] = executable.Identity.DefinitionId,
                    ["runtime.sourceDefinitionVersionId"] = executable.Identity.DefinitionVersionId
                }),
            executable,
            cancellationToken);

        var status = MapStatus(dispatch.CommandDispatch.Status);
        var testRun = new WorkflowTestRun(
            TestRunId: testRunId,
            DefinitionId: executable.Identity.DefinitionId,
            DefinitionVersionId: executable.Identity.DefinitionVersionId,
            ArtifactId: executable.Identity.ArtifactId,
            WorkflowExecutionId: dispatch.WorkflowExecutionId,
            Status: status,
            RequestedBy: RequestedBy,
            RequestedAt: now,
            ExpiresAt: expiresAt,
            Reason: dispatch.CommandDispatch.Reason,
            Metadata: new Dictionary<string, string>
            {
                ["runtime.artifactHash"] = executable.Identity.ArtifactHash
            });

        await testRunStore.SaveAsync(testRun, cancellationToken);
        return WorkflowTestRunView.From(testRun, dispatch.CommandDispatch.Status);
    }

    private static WorkflowTestRunStatus MapStatus(WorkflowExecutionCommandDispatchStatus status) =>
        status switch
        {
            WorkflowExecutionCommandDispatchStatus.Accepted => WorkflowTestRunStatus.DispatchAccepted,
            WorkflowExecutionCommandDispatchStatus.Duplicate => WorkflowTestRunStatus.DispatchDuplicate,
            WorkflowExecutionCommandDispatchStatus.Rejected => WorkflowTestRunStatus.DispatchRejected,
            WorkflowExecutionCommandDispatchStatus.Deferred => WorkflowTestRunStatus.DispatchDeferred,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown workflow execution command dispatch status.")
        };
}
