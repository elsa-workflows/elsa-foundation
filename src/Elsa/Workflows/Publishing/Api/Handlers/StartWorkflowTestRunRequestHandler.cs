using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Publishing.Api.Handlers;

public sealed class StartWorkflowTestRunRequestHandler(
    IWorkflowExecutableCompiler compiler,
    ITransientWorkflowExecutableStore transientExecutableStore,
    IWorkflowTestRunStore testRunStore,
    IWorkflowExecutionStartDispatcher startDispatcher,
    TimeProvider timeProvider)
    : IRequestHandler<StartWorkflowTestRun, WorkflowTestRunView>,
      IRequestHandler<StartWorkflowDraftTestRun, WorkflowTestRunView>
{
    public const string RequestedBy = "workflow-designer-test-run";
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);
    private static readonly RuntimeVariableScopeFactory ScopeFactory = new();
    private const string TestArtifactPrefix = "test-artifact-";
    private const string DraftSnapshotSourceKind = "WorkflowDraftSnapshot";
    private const string DraftArtifactVersion = "draft";

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

        return await StartAsync(
            new WorkflowExecutableCompileRequest(
                request.VersionId,
                WorkflowExecutableScope.TransientTestRun,
                now,
                PublishedAt: null,
                expiresAt,
                TestArtifactPrefix,
                TestRunCompatibilityMetadata(testRunId)),
            testRunId,
            fallbackDefinitionId: request.VersionId,
            fallbackDefinitionVersionId: request.VersionId,
            now,
            expiresAt,
            cancellationToken);
    }

    public async Task<WorkflowTestRunView> Handle(StartWorkflowDraftTestRun request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(DefaultRetention);
        var testRunId = $"testrun-{Guid.NewGuid():N}";
        var sourceDefinitionVersionId = $"draft:{request.SnapshotId}";
        var artifactVersion = request.ArtifactVersion ?? DraftArtifactVersion;

        return await StartAsync(
            new WorkflowExecutableCompileRequest(
                sourceDefinitionVersionId,
                WorkflowExecutableScope.TransientTestRun,
                now,
                PublishedAt: null,
                expiresAt,
                TestArtifactPrefix,
                TestRunCompatibilityMetadata(testRunId))
            {
                Source = new WorkflowExecutableCompileSource(
                    DefinitionId: request.DefinitionId,
                    DefinitionVersionId: sourceDefinitionVersionId,
                    ArtifactVersion: artifactVersion,
                    State: request.State,
                    SourceReference: new WorkflowExecutableSourceReference(DraftSnapshotSourceKind, request.SnapshotId, artifactVersion))
            },
            testRunId,
            fallbackDefinitionId: request.DefinitionId,
            fallbackDefinitionVersionId: sourceDefinitionVersionId,
            now,
            expiresAt,
            cancellationToken);
    }

    private async Task<WorkflowTestRunView> StartAsync(
        WorkflowExecutableCompileRequest compileRequest,
        string testRunId,
        string fallbackDefinitionId,
        string fallbackDefinitionVersionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        WorkflowExecutable executable;
        try
        {
            executable = await compiler.CompileAsync(compileRequest, cancellationToken);
        }
        catch (WorkflowExecutableCompilationException exception)
        {
            var rejected = new WorkflowTestRun(
                TestRunId: testRunId,
                DefinitionId: exception.DefinitionId ?? fallbackDefinitionId,
                DefinitionVersionId: exception.DefinitionVersionId ?? fallbackDefinitionVersionId,
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
                DefinitionId: fallbackDefinitionId,
                DefinitionVersionId: fallbackDefinitionVersionId,
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

        // Seed authored workflow variable defaults from the compiled executable's root structure so a test
        // run resolves `variables.*` input expressions to their declared values (Seam C, #254), matching the
        // production runtime-API start path.
        var variables = ScopeFactory.ProjectDeclaredVariableDefaultsByName(executable.RootActivity);

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
                },
                variables: variables),
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

    private static IReadOnlyDictionary<string, string> TestRunCompatibilityMetadata(string testRunId) =>
        new Dictionary<string, string>
        {
            ["runtime.scope"] = "test-run",
            ["runtime.testRunId"] = testRunId
        };
}
