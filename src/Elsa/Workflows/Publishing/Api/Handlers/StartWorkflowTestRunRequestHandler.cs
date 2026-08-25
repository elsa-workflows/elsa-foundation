using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Contracts;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>
/// Starts versioned and draft workflow test runs. The interface exists so the endpoints depend on
/// the operation rather than this orchestrator's construction, and so hosts can substitute a
/// deterministic seam.
/// </summary>
public interface IWorkflowTestRunStarter
{
    Task<WorkflowTestRunView> StartAsync(StartWorkflowTestRun request, CancellationToken cancellationToken);
    Task<WorkflowTestRunView> StartDraftAsync(StartWorkflowDraftTestRun request, CancellationToken cancellationToken);
}

/// <summary>
/// Test run (ADR 0040) = compile → idempotent save to the SINGLE content-addressed artifact store → append an
/// expiring <see cref="WorkflowExecutableReferenceScope.TestRun"/> Source Reference (source: the definition version
/// or draft snapshot; expiry = the retention window; layout copied from the definition-version layout store when a
/// version id exists, else empty) → dispatch gated on that reference. There is no transient store: a behaviorally
/// identical draft resolves to the SAME artifact id as the published version (the equivalence signal), which is why
/// the artifact prefix is unified with publish.
/// </summary>
public sealed class StartWorkflowTestRunRequestHandler(
    IWorkflowExecutableCompiler compiler,
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowDefinitionVersionLayoutStore layoutStore,
    IWorkflowTestRunStore testRunStore,
    IWorkflowStartDispatcher startDispatcher,
    IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
    TimeProvider timeProvider,
    IWorkflowDefinitionVersionStore? workflowVersionStore = null,
    WorkflowExecutableAuthoredInputsSidecar? authoredInputsSidecar = null,
    WorkflowExecutablePlacementSidecarContext? placementSidecars = null,
    IWorkflowTestScopeStore? testScopeStore = null,
    IWorkflowExecutionPartitionAccessor? partitionAccessor = null,
    IPersistenceAccessContextAccessor? accessContextAccessor = null,
    IExpressionDraftSemanticValidator? expressionValidator = null)
    : IWorkflowTestRunStarter
{
    public const string RequestedBy = "workflow-designer-test-run";
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);
    private static readonly RuntimeVariableDeclarationProjector VariableDeclarations = new();
    // Unified with publish (ADR 0040): scope lives on the reference, not the artifact id, so a test run of a draft
    // behaviorally identical to a published version resolves to the SAME artifact id — the free equivalence signal.
    private const string ArtifactPrefix = "artifact-";
    private const string DraftArtifactVersion = "draft";

    /// <summary>
    /// Preserves the original direct-construction surface with one shared process-local scope provider.
    /// Host composition resolves the primary constructor and its configured replacement provider.
    /// </summary>
    public StartWorkflowTestRunRequestHandler(
        IWorkflowExecutableCompiler compiler,
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowDefinitionVersionLayoutStore layoutStore,
        IWorkflowTestRunStore testRunStore,
        IWorkflowStartDispatcher startDispatcher,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager)
        : this(
            compiler,
            executableStore,
            sourceReferenceStore,
            layoutStore,
            testRunStore,
            startDispatcher,
            rootWriteLeaseManager,
            TimeProvider.System,
            testScopeStore: TestRunCompatibilityScope.Store)
    {
    }

    /// <summary>Preserves the injectable-time direct-construction surface with process-local scope state.</summary>
    public StartWorkflowTestRunRequestHandler(
        IWorkflowExecutableCompiler compiler,
        IWorkflowExecutableStore executableStore,
        IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
        IWorkflowDefinitionVersionLayoutStore layoutStore,
        IWorkflowTestRunStore testRunStore,
        IWorkflowStartDispatcher startDispatcher,
        IWorkflowExecutableRootWriteLeaseManager rootWriteLeaseManager,
        TimeProvider timeProvider)
        : this(
            compiler,
            executableStore,
            sourceReferenceStore,
            layoutStore,
            testRunStore,
            startDispatcher,
            rootWriteLeaseManager,
            timeProvider,
            testScopeStore: TestRunCompatibilityScope.Store)
    {
    }

    public Task<WorkflowTestRunView> StartAsync(StartWorkflowTestRun request, CancellationToken cancellationToken) =>
        Handle(request, cancellationToken);

    public Task<WorkflowTestRunView> StartDraftAsync(StartWorkflowDraftTestRun request, CancellationToken cancellationToken) =>
        Handle(request, cancellationToken);

    public async Task<WorkflowTestRunView> Handle(StartWorkflowTestRun request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(DefaultRetention);
        var testRunId = ShortIdentityGenerator.Generate(now);
        ExpressionDraftValidationResult? versionValidation = null;
        if (workflowVersionStore is null || expressionValidator is null)
            return request.AcknowledgeUnavailableExpressionValidation
                ? await StartVersionAsync()
                : await RejectExpressionValidationAsync(testRunId, request.VersionId, request.VersionId, now, expiresAt, "Expression validation is unavailable. Set acknowledgeUnavailableExpressionValidation to proceed with this test run.", null, request.AcknowledgeUnavailableExpressionValidation, cancellationToken);
        versionValidation = await ExpressionDraftSemanticValidation.ValidateSafelyAsync(
            expressionValidator,
            (await workflowVersionStore.GetWithDefinitionAsync(request.VersionId, cancellationToken)).State,
            request.VersionId,
            cancellationToken);
        if (!CanProceed(versionValidation, request.AcknowledgeUnavailableExpressionValidation))
            return await RejectExpressionValidationAsync(
                testRunId,
                request.VersionId,
                request.VersionId,
                now,
                expiresAt,
                RejectionReason(versionValidation.State),
                versionValidation,
                request.AcknowledgeUnavailableExpressionValidation,
                cancellationToken);
        return await StartVersionAsync();

        Task<WorkflowTestRunView> StartVersionAsync() => StartAsync(
            new WorkflowExecutableCompileRequest(
                request.VersionId,
                WorkflowExecutableReferenceScope.TestRun,
                now,
                PublishedAt: null,
                expiresAt,
                ArtifactPrefix,
                TestRunCompatibilityMetadata(testRunId)),
            testRunId,
            fallbackDefinitionId: request.VersionId,
            fallbackDefinitionVersionId: request.VersionId,
            now,
            expiresAt,
            ToInputValues(request.Inputs),
            draftSnapshot: null,
            versionValidation,
            request.AcknowledgeUnavailableExpressionValidation,
            cancellationToken);

    }

    public async Task<WorkflowTestRunView> Handle(StartWorkflowDraftTestRun request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(DefaultRetention);
        var testRunId = ShortIdentityGenerator.Generate(now);
        var sourceDefinitionVersionId = $"draft:{request.SnapshotId}";
        var artifactVersion = request.ArtifactVersion ?? DraftArtifactVersion;
        ExpressionDraftValidationResult? validation = null;
        if (expressionValidator is null)
        {
            if (!request.AcknowledgeUnavailableExpressionValidation)
                return await RejectExpressionValidationAsync(testRunId, request.DefinitionId, sourceDefinitionVersionId, now, expiresAt, "Expression validation is unavailable. Set acknowledgeUnavailableExpressionValidation to proceed with this test run.", null, request.AcknowledgeUnavailableExpressionValidation, cancellationToken);
        }
        else
        {
            validation = await ExpressionDraftSemanticValidation.ValidateSafelyAsync(
                expressionValidator,
                request.State,
                request.SnapshotId,
                cancellationToken);
            if (!CanProceed(validation, request.AcknowledgeUnavailableExpressionValidation))
                return await RejectExpressionValidationAsync(
                    testRunId,
                    request.DefinitionId,
                    sourceDefinitionVersionId,
                    now,
                    expiresAt,
                    RejectionReason(validation.State),
                    validation,
                    request.AcknowledgeUnavailableExpressionValidation,
                    cancellationToken);
        }

        return await StartAsync(
            new WorkflowExecutableCompileRequest(
                sourceDefinitionVersionId,
                WorkflowExecutableReferenceScope.TestRun,
                now,
                PublishedAt: null,
                expiresAt,
                ArtifactPrefix,
                TestRunCompatibilityMetadata(testRunId))
            {
                Source = new WorkflowExecutableCompileSource(
                    DefinitionId: request.DefinitionId,
                    DefinitionVersionId: sourceDefinitionVersionId,
                    ArtifactVersion: artifactVersion,
                    State: request.State,
                    SourceKind: WorkflowExecutableSourceKinds.WorkflowDraftSnapshot,
                    SourceId: request.SnapshotId,
                    SourceVersion: artifactVersion)
                {
                    ActivityPresentation = request.ActivityPresentation
                }
            },
            testRunId,
            fallbackDefinitionId: request.DefinitionId,
            fallbackDefinitionVersionId: sourceDefinitionVersionId,
            now,
            expiresAt,
            ToInputValues(request.Inputs),
            new WorkflowTestRunDraftSnapshot(
                DefinitionId: request.DefinitionId,
                DefinitionVersionId: sourceDefinitionVersionId,
                ArtifactVersion: artifactVersion,
                State: request.State,
                RequestedAt: now,
                ExpiresAt: expiresAt),
            validation,
            request.AcknowledgeUnavailableExpressionValidation,
            cancellationToken);
    }

    private static bool CanProceed(ExpressionDraftValidationResult validation, bool acknowledgeUnavailable) =>
        validation.State == ExpressionDraftValidationState.Valid ||
        validation.State == ExpressionDraftValidationState.Unavailable &&
        acknowledgeUnavailable &&
        validation.Diagnostics.All(diagnostic => diagnostic.Severity != ExpressionDiagnosticSeverity.Error);

    private static string RejectionReason(ExpressionDraftValidationState state) =>
        state == ExpressionDraftValidationState.Unavailable
            ? "Expression validation is unavailable. Set acknowledgeUnavailableExpressionValidation to proceed with this test run."
            : "Expression validation rejected this test run.";

    private async Task<WorkflowTestRunView> StartAsync(
        WorkflowExecutableCompileRequest compileRequest,
        string testRunId,
        string fallbackDefinitionId,
        string fallbackDefinitionVersionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        IReadOnlyDictionary<string, object?> inputs,
        WorkflowTestRunDraftSnapshot? draftSnapshot,
        ExpressionDraftValidationResult? expressionValidation,
        bool unavailableValidationAcknowledged,
        CancellationToken cancellationToken)
    {
        WorkflowExecutable executable;
        try
        {
            executable = await compiler.CompileAsync(compileRequest, cancellationToken);
        }
        catch (WorkflowExecutableCompilationException exception)
        {
            return await RejectAsync(
                testRunId,
                exception.DefinitionId ?? fallbackDefinitionId,
                exception.DefinitionVersionId ?? fallbackDefinitionVersionId,
                now,
                expiresAt,
                exception.Message,
                cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return await RejectAsync(testRunId, fallbackDefinitionId, fallbackDefinitionVersionId, now, expiresAt, exception.Message, cancellationToken);
        }

        // Idempotent save into the single content-addressed store (ADR 0038/0040): a behaviorally identical publish
        // or prior test run already put this artifact there and it is left untouched.
        await executableStore.SaveAsync(executable, cancellationToken);

        // Append the expiring TestRun Source Reference the dispatch gates on. Scope/expiry are reference facts now.
        var reference = await BuildTestRunReferenceAsync(executable, compileRequest.Source, now, expiresAt, cancellationToken);
        await rootWriteLeaseManager.ExecuteAsync(
            executable.Identity,
            $"test-run:{testRunId}",
            ct => sourceReferenceStore.SaveAsync(reference, ct),
            cancellationToken);

        if (draftSnapshot is not null)
            await testRunStore.SaveDraftSnapshotAsync(draftSnapshot, cancellationToken);

        // Seed authored workflow variable defaults from the compiled executable's workflow-scope declarations
        // so a test run resolves `variables.*` input expressions to their declared values (Seam C, #254),
        // matching the production runtime-API start path.
        var variables = VariableDeclarations.ProjectDeclaredVariableDefaultsByName(executable.WorkflowVariables);

        var partition = partitionAccessor?.Current ??
                        new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue);
        var tenantId = accessContextAccessor?.Current.Scope?.Value;
        var testScope = new WorkflowTestScope(testRunId, expiresAt, tenantId, partition);
        var runtimeScopeStore = testScopeStore ?? TestRunCompatibilityScope.Store;
        await runtimeScopeStore.CreateAsync(testScope, now, cancellationToken);

        WorkflowExecutionStartDispatchResult dispatch;
        try
        {
            dispatch = await startDispatcher.DispatchAsync(
                new WorkflowExecutionStartDispatchRequest(
                    artifactId: executable.Identity.ArtifactId,
                    requestedBy: RequestedBy,
                    workflowExecutionId: null,
                    idempotencyKey: null,
                    metadata: new Dictionary<string, string>
                    {
                        ["runtime.scope"] = "test-run",
                        ["runtime.testRunId"] = testRunId,
                        ["runtime.sourceReferenceId"] = reference.SourceReferenceId,
                        ["runtime.sourceDefinitionId"] = executable.Identity.DefinitionId,
                        ["runtime.sourceDefinitionVersionId"] = executable.Identity.DefinitionVersionId
                    },
                    variables: variables,
                    inputs: inputs,
                    stimulusInput: null,
                    triggerNodeId: null,
                    runKind: WorkflowRunKind.TestRun,
                    sourceSelection: new WorkflowExecutableSourceSelection(sourceReferenceId: reference.SourceReferenceId),
                    provenanceRequirement: WorkflowExecutableProvenanceRequirement.AllowReferenceLessLegacy,
                    parentWorkflowExecutionId: null,
                    correlationId: null,
                    tenantId: tenantId,
                    partition: partition,
                    authority: null,
                    startAuthority: null,
                    dispatchNestingDepth: 0,
                    testScope: testScope),
                WorkflowExecutableReferenceScope.TestRun,
                cancellationToken: cancellationToken);
        }
        catch
        {
            await runtimeScopeStore.CloseAsync(
                new WorkflowTestScopeCloseRequest(testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, timeProvider.GetUtcNow()),
                CancellationToken.None);
            throw;
        }

        var status = MapStatus(dispatch.CommandDispatch.Status);
        if (status == WorkflowTestRunStatus.DispatchRejected)
            await runtimeScopeStore.CloseAsync(
                new WorkflowTestScopeCloseRequest(testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, timeProvider.GetUtcNow()),
                cancellationToken);
        var metadata = ExpressionValidationMetadata(expressionValidation, unavailableValidationAcknowledged);
        metadata["runtime.artifactHash"] = executable.Identity.ArtifactHash;
        metadata["runtime.sourceReferenceId"] = reference.SourceReferenceId;
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
            Metadata: metadata);

        await testRunStore.SaveAsync(testRun, cancellationToken);
        return WorkflowTestRunView.From(testRun, dispatch.CommandDispatch.Status);
    }

    private async Task<WorkflowTestRunView> RejectAsync(
        string testRunId,
        string definitionId,
        string definitionVersionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        string reason,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var rejected = new WorkflowTestRun(
            TestRunId: testRunId,
            DefinitionId: definitionId,
            DefinitionVersionId: definitionVersionId,
            ArtifactId: null,
            WorkflowExecutionId: null,
            Status: WorkflowTestRunStatus.Rejected,
            RequestedBy: RequestedBy,
            RequestedAt: now,
            ExpiresAt: expiresAt,
            Reason: reason,
            Metadata: metadata ?? new Dictionary<string, string>());

        await testRunStore.SaveAsync(rejected, cancellationToken);
        return WorkflowTestRunView.From(rejected);
    }

    private Task<WorkflowTestRunView> RejectExpressionValidationAsync(
        string testRunId,
        string definitionId,
        string definitionVersionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        string reason,
        ExpressionDraftValidationResult? validation,
        bool unavailableAcknowledged,
        CancellationToken cancellationToken)
    {
        var metadata = ExpressionValidationMetadata(validation, unavailableAcknowledged);
        return RejectAsync(testRunId, definitionId, definitionVersionId, now, expiresAt, reason, cancellationToken, metadata);
    }

    private static Dictionary<string, string> ExpressionValidationMetadata(
        ExpressionDraftValidationResult? validation,
        bool unavailableAcknowledged)
    {
        var diagnostics = validation?.Diagnostics.Select(diagnostic => new
        {
            diagnostic.Code,
            Severity = diagnostic.Severity.ToString(),
            diagnostic.Message,
            diagnostic.AuthoredPath,
            diagnostic.Range
        }) ?? [];
        return new Dictionary<string, string>
        {
            ["expressionValidation.state"] = (validation?.State ?? ExpressionDraftValidationState.Unavailable).ToString(),
            ["expressionValidation.code"] = validation?.Code ??
                (validation is null ? "expression-validation-unavailable" : $"expression-validation-{validation.State.ToString().ToLowerInvariant()}"),
            ["expressionValidation.unavailableAcknowledged"] = unavailableAcknowledged.ToString(),
            ["expressionValidation.diagnostics"] = JsonSerializer.Serialize(diagnostics)
        };
    }

    // Builds the expiring TestRun reference. Source identity comes from the compile source when present (draft
    // snapshot), else from the compiled identity (durable definition version). The layout sidecar is copied verbatim
    // from the definition-version layout store when a version id resolves one, else empty (ADR 0039) — a draft
    // snapshot with no persisted layout carries an empty sidecar.
    private async Task<WorkflowExecutableSourceReference> BuildTestRunReferenceAsync(
        WorkflowExecutable executable,
        WorkflowExecutableCompileSource? source,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var identity = executable.Identity;
        var layout = await layoutStore.FindByVersionIdAsync(identity.DefinitionVersionId, cancellationToken);
        var sourceState = source?.State ?? (workflowVersionStore is null
            ? null
            : (await workflowVersionStore.GetWithDefinitionAsync(identity.DefinitionVersionId, cancellationToken)).State);
        var authoredInputs = sourceState is not null && authoredInputsSidecar is not null
            ? authoredInputsSidecar.CopyFrom(sourceState)
            : [];
        var activityPresentation = source?.ActivityPresentation.Count > 0
            ? source.ActivityPresentation
            : layout?.ActivityPresentation ?? [];

        return new WorkflowExecutableSourceReference(
            SourceReferenceId: ShortIdentityGenerator.Generate(now),
            ArtifactId: identity.ArtifactId,
            SourceKind: source?.SourceKind ?? WorkflowExecutableSourceKinds.WorkflowDefinitionVersion,
            SourceId: source?.SourceId ?? identity.DefinitionVersionId,
            SourceVersion: source?.SourceVersion ?? identity.ArtifactVersion,
            DefinitionId: identity.DefinitionId,
            DefinitionVersionId: identity.DefinitionVersionId,
            ArtifactVersion: identity.ArtifactVersion,
            CreatedAt: now,
            PublishedAt: null,
            Scope: WorkflowExecutableReferenceScope.TestRun,
            ExpiresAt: expiresAt,
            Layout: WorkflowExecutableLayoutSidecar.CopyFrom(layout),
            LayoutSidecar: placementSidecars?.Get(identity.DefinitionVersionId),
            AuthoredInputs: authoredInputs,
            ActivityPresentation:
                WorkflowExecutableActivityPresentationSidecar.CopyFrom(
                    activityPresentation,
                    executable));
    }

    // Caller-supplied workflow inputs (#286): unlike variables (which carry authored defaults projected off the
    // compiled executable), inputs have no artifact-side default, so an empty channel leaves `input.*` empty.
    private static IReadOnlyDictionary<string, object?> ToInputValues(IReadOnlyDictionary<string, JsonElement>? inputs) =>
        inputs is not { Count: > 0 }
            ? EmptyInputs
            : inputs.ToDictionary(item => item.Key, item => (object?)item.Value, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, object?> EmptyInputs = new Dictionary<string, object?>(StringComparer.Ordinal);

    private static WorkflowTestRunStatus MapStatus(WorkflowExecutionCommandDispatchStatus status) =>
        status switch
        {
            WorkflowExecutionCommandDispatchStatus.Accepted => WorkflowTestRunStatus.DispatchAccepted,
            // Dispatch was accepted; the workflow faulted during the ensuing drain. The fault is authoritatively surfaced
            // on the dispatch result and via workflow status queries. Treat as accepted here (a dedicated test-run faulted
            // status is a follow-up).
            WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted => WorkflowTestRunStatus.DispatchAccepted,
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
