using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Identity;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.Logging;

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

    Task<DraftTestRunOptionsView> GetDraftOptionsAsync(
        DraftTestRunOptionsRequest request,
        CancellationToken cancellationToken) =>
        Task.FromException<DraftTestRunOptionsView>(new NotSupportedException("Draft TestRun option discovery is not configured."));

    Task<WorkflowTestRunView?> GetAsync(string testRunId, CancellationToken cancellationToken) =>
        Task.FromException<WorkflowTestRunView?>(new NotSupportedException("Workflow TestRun inspection is not configured."));

    Task<WorkflowTestRunView?> StopAsync(string testRunId, CancellationToken cancellationToken) =>
        Task.FromException<WorkflowTestRunView?>(new NotSupportedException("Workflow TestRun stop is not configured."));
}

/// <summary>
/// Test run = compile → idempotent save to the SINGLE content-addressed artifact store → append an
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
    IExpressionDraftSemanticValidator? expressionValidator = null,
    ILogger<StartWorkflowTestRunRequestHandler>? logger = null,
    IWorkflowTriggerBindingExtractor? triggerBindingExtractor = null,
    IEnumerable<ITestRunTriggerDriver>? triggerDrivers = null,
    IWorkflowExecutableInputValidator? inputValidator = null,
    ITestRunTargetStore? testRunTargetStore = null,
    ITestRunTargetDispatcher? testRunTargetDispatcher = null,
    ITestRunTargetCleaner? testRunTargetCleaner = null,
    IWorkflowTestScopeCleaner? testScopeCleaner = null,
    IRuntimeExecutionIdGenerator? executionIdGenerator = null,
    IServiceProvider? ambientServices = null,
    ITestRunRootCanceller? rootCanceller = null,
    IWorkflowExecutionStateStore? executionStateStore = null)
    : IWorkflowTestRunStarter
{
    public const string RequestedBy = "workflow-designer-test-run";
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(30);
    private static readonly RuntimeVariableDeclarationProjector VariableDeclarations = new();
    // Unified with publish: scope lives on the reference, not the artifact id, so a test run of a draft
    // behaviorally identical to a published version resolves to the SAME artifact id — the free equivalence signal.
    private const string ArtifactPrefix = "artifact-";
    private const string DraftArtifactVersion = "draft";
    private readonly IReadOnlyCollection<ITestRunTriggerDriver> _triggerDrivers = (triggerDrivers ?? []).ToArray();

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

    public async Task<DraftTestRunOptionsView> GetDraftOptionsAsync(
        DraftTestRunOptionsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(DefaultRetention);
        var sourceDefinitionVersionId = $"draft:{request.SnapshotId}";
        var artifactVersion = request.ArtifactVersion ?? DraftArtifactVersion;
        var compileRequest = new WorkflowExecutableCompileRequest(
            sourceDefinitionVersionId,
            WorkflowExecutableReferenceScope.TestRun,
            now,
            PublishedAt: null,
            expiresAt,
            ArtifactPrefix,
            TestRunCompatibilityMetadata("test-run-options"))
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
        };

        WorkflowExecutable executable;
        try
        {
            executable = await compiler.CompileAsync(compileRequest, cancellationToken);
        }
        catch (Exception exception) when (exception is WorkflowExecutableCompilationException or ArgumentException)
        {
            return new DraftTestRunOptionsView(
                string.Empty,
                null,
                [],
                [new TestRunOptionsDiagnostic("compile.failed", exception.Message)]);
        }

        var diagnostics = new List<TestRunOptionsDiagnostic>();
        if (triggerBindingExtractor is null)
        {
            diagnostics.Add(new("trigger.discovery.unavailable", "Trigger discovery is not configured."));
            return new DraftTestRunOptionsView(executable.Identity.ArtifactHash, executable.InputContract, [], diagnostics);
        }

        WorkflowTriggerPreflightOutcome outcome;
        try
        {
            outcome = triggerBindingExtractor.Evaluate(executable);
        }
        catch (Exception exception) when (exception is WorkflowTriggerPreflightException or WorkflowTriggerExtractionException)
        {
            diagnostics.Add(new("trigger.discovery.rejected", exception.Message));
            return new DraftTestRunOptionsView(executable.Identity.ArtifactHash, executable.InputContract, [], diagnostics);
        }

        var options = new List<TestRunTriggerOptionView>();
        foreach (var nodeOutcome in outcome.NodeOutcomes)
        {
            if (!executable.NodesById.TryGetValue(nodeOutcome.ExecutableNodeId, out var node))
            {
                diagnostics.Add(new("trigger.node.missing", "A discovered trigger node is missing from the compiled workflow.", nodeOutcome.ExecutableNodeId));
                continue;
            }

            foreach (var binding in nodeOutcome.Bindings)
            {
                var context = new TestRunTriggerContext(executable, nodeOutcome.ProviderId, node, binding);
                var drivers = MatchingDrivers(context);
                if (drivers.Length == 0)
                {
                    options.Add(TriggerOption(binding, node, null, false, "No test-run driver is registered for this trigger.", []));
                    continue;
                }

                if (drivers.Length > 1)
                {
                    options.Add(TriggerOption(binding, node, null, false, "More than one test-run driver claims this trigger.", []));
                    diagnostics.Add(new("trigger.driver.ambiguous", "More than one test-run driver claims this trigger.", node.ExecutableNodeId));
                    continue;
                }

                try
                {
                    var modes = drivers[0].Describe(context);
                    var duplicateMode = modes.GroupBy(mode => mode.ModeId, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
                    if (duplicateMode is not null)
                        options.Add(TriggerOption(binding, node, drivers[0].DriverId, false, "The test-run driver returned duplicate mode identities.", []));
                    else if (modes.Count == 0)
                        options.Add(TriggerOption(binding, node, drivers[0].DriverId, false, "The test-run driver has no available modes.", []));
                    else
                        options.Add(TriggerOption(binding, node, drivers[0].DriverId, true, null, modes));
                }
                catch (Exception exception)
                {
                    options.Add(TriggerOption(binding, node, drivers[0].DriverId, false, "The test-run driver could not describe this trigger.", []));
                    diagnostics.Add(new("trigger.driver.describe-failed", exception.Message, node.ExecutableNodeId));
                }
            }
        }

        return new DraftTestRunOptionsView(executable.Identity.ArtifactHash, executable.InputContract, options, diagnostics);
    }

    public async Task<WorkflowTestRunView?> GetAsync(string testRunId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        var testRun = await testRunStore.FindAsync(testRunId, cancellationToken);
        if (testRun is null)
            return null;

        var scopeRecord = await FindCurrentTestScopeAsync(testRunId, cancellationToken);
        if (scopeRecord is null && (accessContextAccessor is not null || partitionAccessor is not null))
            return null;

        if (testRun.Status == WorkflowTestRunStatus.StopRequested && testRun.WorkflowExecutionId is { } stoppingExecutionId)
        {
            var execution = executionStateStore is null
                ? null
                : await executionStateStore.FindAsync(stoppingExecutionId, cancellationToken);
            if (execution?.Status == WorkflowExecutionStatus.Cancelled)
            {
                var stopped = testRun with { Status = WorkflowTestRunStatus.Stopped, Reason = null };
                if (scopeRecord?.State == WorkflowTestScopeState.Open)
                    await testRunStore.SaveAsync(stopped, cancellationToken);
                return WorkflowTestRunView.From(stopped);
            }

            return WorkflowTestRunView.From(testRun);
        }

        var target = testRunTargetStore is null
            ? null
            : await testRunTargetStore.FindAsync(testRunId, cancellationToken);
        if (target?.DispatchResult is not { } result)
            return WorkflowTestRunView.From(testRun);

        var command = result.Dispatch.CommandDispatch;
        var refreshed = testRun with
        {
            WorkflowExecutionId = result.Dispatch.WorkflowExecutionId,
            Status = MapStatus(command.Status),
            Reason = command.Reason,
            DriverAction = null
        };
        if (scopeRecord?.State == WorkflowTestScopeState.Open)
            await testRunStore.SaveAsync(refreshed, cancellationToken);
        return WorkflowTestRunView.From(refreshed, command.Status);
    }

    public async Task<WorkflowTestRunView?> StopAsync(string testRunId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        var testRun = await testRunStore.FindAsync(testRunId, cancellationToken);
        if (testRun is null)
            return null;

        var scopeRecord = await FindCurrentTestScopeAsync(testRunId, cancellationToken);
        if (scopeRecord is null && (accessContextAccessor is not null || partitionAccessor is not null))
            return null;

        var targetBeforeStop = testRunTargetStore is null
            ? null
            : await testRunTargetStore.FindAsync(testRunId, cancellationToken);

        var observedAt = timeProvider.GetUtcNow();
        if (testScopeCleaner is not null)
            await testScopeCleaner.CloseAsync(testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, observedAt, cancellationToken);
        else
        {
            await (testScopeStore ?? TestRunCompatibilityScope.Store).CloseAsync(
                new WorkflowTestScopeCloseRequest(testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, observedAt),
                cancellationToken);
            await StopTargetAfterScopeCloseAsync(testRunId, cancellationToken);
        }

        var workflowExecutionId = targetBeforeStop?.DispatchResult?.Dispatch.WorkflowExecutionId ??
                                  (targetBeforeStop?.State == TestRunTargetState.Dispatching ? targetBeforeStop.WorkflowExecutionId : null) ??
                                  testRun.WorkflowExecutionId;
        if (workflowExecutionId is not null && rootCanceller is not null && scopeRecord is not null)
            await rootCanceller.RequestCancelAsync(testRunId, workflowExecutionId, scopeRecord.Scope.Partition, cancellationToken);

        var executionState = workflowExecutionId is null || executionStateStore is null
            ? null
            : await executionStateStore.FindAsync(workflowExecutionId, cancellationToken);
        var rootWasCancelled = executionState?.Status == WorkflowExecutionStatus.Cancelled;
        var noRootWasStarted = workflowExecutionId is null ||
                               targetBeforeStop is { State: TestRunTargetState.Prepared, DispatchResult: null } && executionState is null;

        var stopped = testRun with
        {
            WorkflowExecutionId = workflowExecutionId,
            Status = rootWasCancelled || noRootWasStarted
                ? WorkflowTestRunStatus.Stopped
                : WorkflowTestRunStatus.StopRequested,
            Reason = rootWasCancelled || noRootWasStarted
                ? "Workflow test run was stopped."
                : "Workflow test-run scope was closed and root cancellation was requested.",
            DriverAction = null
        };
        await testRunStore.SaveAsync(stopped, cancellationToken);
        return WorkflowTestRunView.From(stopped);
    }

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
            cancellationToken,
            logger);
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
            request.Inputs,
            draftSnapshot: null,
            versionValidation,
            request.AcknowledgeUnavailableExpressionValidation,
            startSelection: null,
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
                cancellationToken,
                logger);
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
            request.Inputs,
            new WorkflowTestRunDraftSnapshot(
                DefinitionId: request.DefinitionId,
                DefinitionVersionId: sourceDefinitionVersionId,
                ArtifactVersion: artifactVersion,
                State: request.State,
                RequestedAt: now,
                ExpiresAt: expiresAt),
            validation,
            request.AcknowledgeUnavailableExpressionValidation,
            request.Start,
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

    private async Task<WorkflowTestRunView> StartSelectedTriggerAsync(
        WorkflowExecutable executable,
        WorkflowExecutableSourceReference reference,
        WorkflowTestScope testScope,
        IWorkflowTestScopeStore runtimeScopeStore,
        ResolvedTestRunTrigger selectedTrigger,
        IReadOnlyDictionary<string, JsonElement> inputs,
        IReadOnlyDictionary<string, object?> variables,
        string testRunId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        ExpressionDraftValidationResult? expressionValidation,
        bool unavailableValidationAcknowledged,
        CancellationToken cancellationToken)
    {
        var driver = selectedTrigger.Driver;
        var trigger = selectedTrigger.Context;
        var targetId = ShortIdentityGenerator.Generate(now);
        var prepareContext = new TestRunTriggerPrepareContext(
            trigger,
            testScope,
            testRunId,
            selectedTrigger.ModeId,
            selectedTrigger.Data,
            targetId);

        TestRunTriggerPreparation preparation;
        try
        {
            preparation = await driver.PrepareAsync(prepareContext, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await CloseTestScopeAsync(runtimeScopeStore, testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, timeProvider.GetUtcNow());
            await driver.StopAsync(
                new TestRunTriggerStopContext(trigger, testScope, testRunId, selectedTrigger.ModeId, null),
                CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Test-run trigger driver {DriverId} could not prepare test run {TestRunId}.", driver.DriverId, testRunId);
            await CloseTestScopeAsync(runtimeScopeStore, testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, timeProvider.GetUtcNow());
            await driver.StopAsync(
                new TestRunTriggerStopContext(trigger, testScope, testRunId, selectedTrigger.ModeId, null),
                CancellationToken.None);
            return await RejectAsync(
                testRunId,
                executable.Identity.DefinitionId,
                executable.Identity.DefinitionVersionId,
                now,
                expiresAt,
                "The selected test trigger could not be prepared.",
                CancellationToken.None,
                ExpressionValidationMetadata(expressionValidation, unavailableValidationAcknowledged));
        }

        var workflowExecutionId = executionIdGenerator?.NewWorkflowExecutionId() ?? ShortIdentityGenerator.Generate(now);
        var idempotencyKey = executionIdGenerator?.NewWorkflowExecutionCommandId() ?? ShortIdentityGenerator.Generate(now);
        var target = new TestRunTarget(
            testRunId,
            driver.DriverId,
            trigger.ProviderId,
            selectedTrigger.ModeId,
            executable.Identity,
            reference.SourceReferenceId,
            trigger.Binding,
            testScope,
            RequestedBy,
            workflowExecutionId,
            idempotencyKey,
            inputs,
            variables,
            preparation.DriverState,
            now);

        try
        {
            await testRunTargetStore!.SaveAsync(target, cancellationToken);
        }
        catch
        {
            await CloseTestScopeAsync(runtimeScopeStore, testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, timeProvider.GetUtcNow());
            await driver.StopAsync(
                new TestRunTriggerStopContext(trigger, testScope, testRunId, selectedTrigger.ModeId, preparation.DriverState),
                CancellationToken.None);
            throw;
        }

        var metadata = TestRunMetadata(executable, reference, expressionValidation, unavailableValidationAcknowledged);
        if (preparation.Kind == TestRunPreparationKind.ExternalAction)
        {
            var prepared = new WorkflowTestRun(
                TestRunId: testRunId,
                DefinitionId: executable.Identity.DefinitionId,
                DefinitionVersionId: executable.Identity.DefinitionVersionId,
                ArtifactId: executable.Identity.ArtifactId,
                WorkflowExecutionId: null,
                Status: WorkflowTestRunStatus.Prepared,
                RequestedBy: RequestedBy,
                RequestedAt: now,
                ExpiresAt: expiresAt,
                Reason: null,
                Metadata: metadata,
                DriverAction: preparation.Action,
                DriverDetails: preparation.Details);
            try
            {
                await testRunStore.SaveAsync(prepared, cancellationToken);
            }
            catch
            {
                await CloseTestScopeAsync(runtimeScopeStore, testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, timeProvider.GetUtcNow());
                throw;
            }
            return WorkflowTestRunView.From(prepared);
        }

        TestRunTargetDispatchResult dispatch;
        try
        {
            dispatch = await testRunTargetDispatcher!.DispatchAsync(
                testRunId,
                preparation.StimulusInput,
                new WorkflowExecutionCommandDispatchOptions(ambientServices),
                cancellationToken);
        }
        catch
        {
            await CloseTestScopeAsync(runtimeScopeStore, testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, timeProvider.GetUtcNow());
            throw;
        }

        var status = MapStatus(dispatch.Dispatch.CommandDispatch.Status);
        if (status == WorkflowTestRunStatus.DispatchRejected)
            await CloseTestScopeAsync(runtimeScopeStore, testRunId, WorkflowTestScopeCloseReason.ExplicitTeardown, timeProvider.GetUtcNow());

        var testRun = new WorkflowTestRun(
            TestRunId: testRunId,
            DefinitionId: executable.Identity.DefinitionId,
            DefinitionVersionId: executable.Identity.DefinitionVersionId,
            ArtifactId: executable.Identity.ArtifactId,
            WorkflowExecutionId: dispatch.Dispatch.WorkflowExecutionId,
            Status: status,
            RequestedBy: RequestedBy,
            RequestedAt: now,
            ExpiresAt: expiresAt,
            Reason: dispatch.Dispatch.CommandDispatch.Reason,
            Metadata: metadata,
            DriverDetails: preparation.Details);
        await testRunStore.SaveAsync(testRun, cancellationToken);
        return WorkflowTestRunView.From(testRun, dispatch.Dispatch.CommandDispatch.Status);
    }

    private async Task<WorkflowTestRunView> StartAsync(
        WorkflowExecutableCompileRequest compileRequest,
        string testRunId,
        string fallbackDefinitionId,
        string fallbackDefinitionVersionId,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        IReadOnlyDictionary<string, JsonElement>? inputs,
        WorkflowTestRunDraftSnapshot? draftSnapshot,
        ExpressionDraftValidationResult? expressionValidation,
        bool unavailableValidationAcknowledged,
        WorkflowTestRunStartSelection? startSelection,
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

        var triggerResolution = ResolveTriggerSelection(executable, startSelection);
        if (triggerResolution.Error is not null)
            return await RejectAsync(
                testRunId,
                executable.Identity.DefinitionId,
                executable.Identity.DefinitionVersionId,
                now,
                expiresAt,
                triggerResolution.Error,
                cancellationToken,
                ExpressionValidationMetadata(expressionValidation, unavailableValidationAcknowledged));

        if (triggerResolution.Trigger is not null &&
            (testRunTargetStore is null || testRunTargetDispatcher is null || testRunTargetCleaner is null))
            return await RejectAsync(
                testRunId,
                executable.Identity.DefinitionId,
                executable.Identity.DefinitionVersionId,
                now,
                expiresAt,
                "Test trigger target services are not configured.",
                cancellationToken,
                ExpressionValidationMetadata(expressionValidation, unavailableValidationAcknowledged));

        var normalizedInputs = inputs ?? EmptyJsonInputs;
        if (executable.InputContract is { } inputContract)
        {
            if (inputValidator is null)
                return await RejectAsync(
                    testRunId,
                    executable.Identity.DefinitionId,
                    executable.Identity.DefinitionVersionId,
                    now,
                    expiresAt,
                    "Workflow input validation is not configured for this executable.",
                    cancellationToken,
                    ExpressionValidationMetadata(expressionValidation, unavailableValidationAcknowledged));

            var validator = inputValidator;
            var inputValidation = validator.Validate(inputContract, normalizedInputs);
            if (!inputValidation.IsValid)
            {
                var inputMetadata = ExpressionValidationMetadata(expressionValidation, unavailableValidationAcknowledged);
                inputMetadata["runtime.inputValidation.findings"] = JsonSerializer.Serialize(inputValidation.Findings);
                return await RejectAsync(
                    testRunId,
                    executable.Identity.DefinitionId,
                    executable.Identity.DefinitionVersionId,
                    now,
                    expiresAt,
                    "Workflow input validation rejected this test run.",
                    cancellationToken,
                    inputMetadata);
            }

            normalizedInputs = inputValidation.NormalizedInputs;
        }

        // Idempotent save into the single content-addressed store: a behaviorally identical publish
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

        if (triggerResolution.Trigger is { } selectedTrigger)
            return await StartSelectedTriggerAsync(
                executable,
                reference,
                testScope,
                runtimeScopeStore,
                selectedTrigger,
                normalizedInputs,
                variables,
                testRunId,
                now,
                expiresAt,
                expressionValidation,
                unavailableValidationAcknowledged,
                cancellationToken);

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
                    inputs: ToInputValues(normalizedInputs),
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
                dispatchOptions: new WorkflowExecutionCommandDispatchOptions(ambientServices),
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
                (validation is null ? ExpressionDraftSemanticValidation.UnavailableCode : $"expression-validation-{validation.State.ToString().ToLowerInvariant()}"),
            ["expressionValidation.unavailableAcknowledged"] = unavailableAcknowledged.ToString(),
            ["expressionValidation.diagnostics"] = JsonSerializer.Serialize(diagnostics)
        };
    }

    private (ResolvedTestRunTrigger? Trigger, string? Error) ResolveTriggerSelection(
        WorkflowExecutable executable,
        WorkflowTestRunStartSelection? selection)
    {
        if (selection is null)
            return (null, null);

        if (StringComparer.Ordinal.Equals(selection.Kind, WorkflowTestRunStartSelection.Direct))
            return selection.OptionId is null && selection.ModeId is null && selection.Data is null
                ? (null, null)
                : (null, "A direct workflow start cannot include a trigger option, mode, or data.");

        if (!StringComparer.Ordinal.Equals(selection.Kind, WorkflowTestRunStartSelection.Trigger))
            return (null, "The selected workflow test-run start kind is not supported.");
        if (string.IsNullOrWhiteSpace(selection.OptionId) || string.IsNullOrWhiteSpace(selection.ModeId))
            return (null, "A trigger start requires an option and mode.");
        if (selection.Data is { ValueKind: JsonValueKind.Undefined })
            return (null, "Trigger data must be a defined JSON value when provided.");
        if (triggerBindingExtractor is null)
            return (null, "Trigger discovery is not configured.");

        WorkflowTriggerPreflightOutcome outcome;
        try
        {
            outcome = triggerBindingExtractor.Evaluate(executable);
        }
        catch (Exception exception) when (exception is WorkflowTriggerPreflightException or WorkflowTriggerExtractionException)
        {
            return (null, "The compiled workflow triggers could not be validated.");
        }

        if (!StringComparer.Ordinal.Equals(outcome.Identity.ArtifactHash, executable.Identity.ArtifactHash))
            return (null, "The selected trigger option does not belong to this compiled workflow.");

        var binding = outcome.Bindings.SingleOrDefault(item =>
            StringComparer.Ordinal.Equals(item.TriggerBindingId, selection.OptionId));
        if (binding is null)
            return (null, "The selected trigger option is not available for this workflow snapshot.");

        var nodeOutcome = outcome.NodeOutcomes.SingleOrDefault(item =>
            StringComparer.Ordinal.Equals(item.ExecutableNodeId, binding.ExecutableNodeId));
        if (nodeOutcome is null || !executable.NodesById.TryGetValue(binding.ExecutableNodeId, out var node))
            return (null, "The selected trigger node is missing from the compiled workflow.");

        var context = new TestRunTriggerContext(executable, nodeOutcome.ProviderId, node, binding);
        var drivers = MatchingDrivers(context);
        if (drivers.Length == 0)
            return (null, "No test-run driver can start the selected trigger.");
        if (drivers.Length > 1)
            return (null, "More than one test-run driver claims the selected trigger.");

        var driver = drivers[0];
        if (string.IsNullOrWhiteSpace(driver.DriverId))
            return (null, "The selected test-run driver has no stable identity.");

        IReadOnlyCollection<TestRunTriggerMode> modes;
        try
        {
            modes = driver.Describe(context);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Test-run trigger driver {DriverId} could not describe node {NodeId}.", driver.DriverId, node.ExecutableNodeId);
            return (null, "The selected test-run driver could not describe this trigger.");
        }

        var selectedModes = modes.Where(mode => StringComparer.Ordinal.Equals(mode.ModeId, selection.ModeId)).ToArray();
        if (selectedModes.Length != 1)
            return (null, "The selected trigger mode is not available.");

        TestRunTriggerValidation validation;
        try
        {
            validation = driver.Validate(context, selection.ModeId, selection.Data);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Test-run trigger driver {DriverId} could not validate node {NodeId}.", driver.DriverId, node.ExecutableNodeId);
            return (null, "The selected test-run trigger data could not be validated.");
        }

        if (validation is null)
            return (null, "The selected test-run driver returned no validation result.");
        if (!validation.IsValid)
            return (null, string.Join(" ", validation.Findings.Select(finding => finding.Message)));

        return (new ResolvedTestRunTrigger(context, driver, selection.ModeId, selection.Data?.Clone()), null);
    }

    private ITestRunTriggerDriver[] MatchingDrivers(TestRunTriggerContext context)
    {
        var matches = new List<ITestRunTriggerDriver>();
        foreach (var driver in _triggerDrivers)
        {
            try
            {
                if (driver.Supports(context))
                    matches.Add(driver);
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "Test-run trigger driver {DriverId} failed its support check for node {NodeId}.", driver.DriverId, context.Node.ExecutableNodeId);
            }
        }

        return matches.ToArray();
    }

    private static TestRunTriggerOptionView TriggerOption(
        WorkflowTriggerBinding binding,
        ExecutableNode node,
        string? driverId,
        bool available,
        string? reason,
        IReadOnlyCollection<TestRunTriggerMode> modes)
    {
        var label = node.Metadata.TryGetValue("name", out var authoredName) && !string.IsNullOrWhiteSpace(authoredName)
            ? authoredName
            : node.ActivityType;
        return new TestRunTriggerOptionView(
            binding.TriggerBindingId,
            driverId,
            binding.ExecutableNodeId,
            label,
            available,
            reason,
            Configuration: null,
            Modes: modes);
    }

    private static Dictionary<string, string> TestRunMetadata(
        WorkflowExecutable executable,
        WorkflowExecutableSourceReference reference,
        ExpressionDraftValidationResult? expressionValidation,
        bool unavailableValidationAcknowledged)
    {
        var metadata = ExpressionValidationMetadata(expressionValidation, unavailableValidationAcknowledged);
        metadata["runtime.artifactHash"] = executable.Identity.ArtifactHash;
        metadata["runtime.sourceReferenceId"] = reference.SourceReferenceId;
        return metadata;
    }

    private async ValueTask CloseTestScopeAsync(
        IWorkflowTestScopeStore runtimeScopeStore,
        string testRunId,
        WorkflowTestScopeCloseReason reason,
        DateTimeOffset observedAt)
    {
        if (testScopeCleaner is not null)
            await testScopeCleaner.CloseAsync(testRunId, reason, observedAt, CancellationToken.None);
        else
        {
            await runtimeScopeStore.CloseAsync(
                new WorkflowTestScopeCloseRequest(testRunId, reason, observedAt),
                CancellationToken.None);
            await StopTargetAfterScopeCloseAsync(testRunId, CancellationToken.None);
        }
    }

    private async ValueTask StopTargetAfterScopeCloseAsync(string testRunId, CancellationToken cancellationToken)
    {
        if (testRunTargetCleaner is not null)
            await testRunTargetCleaner.StopAsync(testRunId, cancellationToken);
        else if (testRunTargetStore is not null)
        {
            await testRunTargetStore.MarkStoppedAsync(testRunId, cancellationToken);
            await testRunTargetStore.RemoveAsync(testRunId, cancellationToken);
        }
    }

    private async Task<WorkflowTestScopeRecord?> FindCurrentTestScopeAsync(string testRunId, CancellationToken cancellationToken)
    {
        var scope = await (testScopeStore ?? TestRunCompatibilityScope.Store).FindAsync(testRunId, cancellationToken);
        if (scope is null)
            return null;

        var currentTenantId = accessContextAccessor?.Current.Scope?.Value;
        var currentPartition = partitionAccessor?.Current ??
                               new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue);
        if (accessContextAccessor is not null &&
            !StringComparer.Ordinal.Equals(currentTenantId, scope.Scope.TenantId))
            return null;
        if (partitionAccessor is not null && !Equals(currentPartition, scope.Scope.Partition))
            return null;
        return scope;
    }

    private sealed record ResolvedTestRunTrigger(
        TestRunTriggerContext Context,
        ITestRunTriggerDriver Driver,
        string ModeId,
        JsonElement? Data);

    // Builds the expiring TestRun reference. Source identity comes from the compile source when present (draft
    // snapshot), else from the compiled identity (durable definition version). The layout sidecar is copied verbatim
    // from the definition-version layout store when a version id resolves one, else empty — a draft
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

    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyJsonInputs =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

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
