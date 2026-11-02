using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Services;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowTestRunRequestHandlerTests
{
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();
    private readonly InMemoryWorkflowExecutableStore _executableStore = new();
    private readonly InMemoryWorkflowExecutableSourceReferenceStore _sourceReferenceStore = new();
    private readonly InMemoryWorkflowTestScopeStore _testScopeStore = new();
    private readonly InMemoryWorkflowTestRunStore _testRunStore;

    public WorkflowTestRunRequestHandlerTests() =>
        _testRunStore = CreateTestRunStore(_testScopeStore);

    [Theory]
    [InlineData("options", null)]
    [InlineData("options", "tenant-a")]
    [InlineData("draft", null)]
    [InlineData("draft", "tenant-a")]
    [InlineData("version", null)]
    [InlineData("version", "tenant-a")]
    public async Task Test_run_compilation_and_source_reference_keep_the_current_tenant(string entryPoint, string? tenantId)
    {
        var access = new StubPersistenceAccessContextAccessor(tenantId is null
            ? PersistenceAccessContext.Global
            : PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));
        var requests = new List<WorkflowExecutableCompileRequest>();
        var handler = entryPoint == "version"
            ? Handler(WorkflowVersion(Node("write-one", Text("hello"))), accessContextAccessor: access, onCompile: requests.Add)
            : DraftSnapshotHandler(accessContextAccessor: access, onCompile: requests.Add);

        if (entryPoint == "options")
            await handler.GetDraftOptionsAsync(DraftOptionsRequest(), CancellationToken.None);
        else
        {
            var result = entryPoint == "version"
                ? await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None)
                : await handler.Handle(new StartWorkflowDraftTestRun("definition-1", "snapshot-tenant", DraftState()), CancellationToken.None);
            Assert.Equal("DispatchAccepted", result.Status);
            Assert.Equal(tenantId, Assert.Single(await _sourceReferenceStore.ListAllAsync()).TenantId);
        }

        Assert.Equal(tenantId, Assert.Single(requests).TenantId);
    }

    [Fact]
    public async Task StartsTransientWorkflowTestRunWithoutListingPublishedExecutable()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("DispatchAccepted", view.Status);
        Assert.Equal("Accepted", view.CommandDispatchStatus);
        Assert.NotNull(view.WorkflowExecutionId);
        Assert.StartsWith("artifact-", view.ArtifactId, StringComparison.Ordinal);
        // There is a single content-addressed store; the test-run artifact now lives in it (scope/
        // expiry are reference facts, no longer segregated by an artifact-level list filter).
        Assert.Equal(view.ArtifactId, Assert.Single(await _executableStore.ListAllAsync()).Identity.ArtifactId);
        Assert.NotNull(await _testRunStore.FindAsync(view.TestRunId));
    }

    [Fact]
    public async Task DraftTriggerTestRunPreparesExternalActionAndStopCleansItsDriverResource()
    {
        var driver = new ExternalTestRunDriver();
        using var runtime = CreateTestRunRuntime(driver);
        var extractor = new SingleBindingTriggerExtractor();
        var handler = DraftSnapshotHandler(
            triggerBindingExtractor: extractor,
            triggerDrivers: [driver],
            testRunTargetStore: runtime.Targets,
            testRunTargetDispatcher: runtime.Dispatcher,
            testRunTargetCleaner: runtime.Cleaner);
        var options = await handler.GetDraftOptionsAsync(DraftOptionsRequest(), CancellationToken.None);
        var option = Assert.Single(options.Options);
        Assert.True(option.IsAvailable);
        Assert.Equal("fire-now", Assert.Single(option.Modes).ModeId);

        var prepared = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-trigger",
            State: DraftState(),
            Start: new WorkflowTestRunStartSelection(
                WorkflowTestRunStartSelection.Trigger,
                option.OptionId,
                "fire-now")), CancellationToken.None);

        Assert.Equal("Prepared", prepared.Status);
        Assert.Null(prepared.WorkflowExecutionId);
        Assert.Equal("POST", prepared.DriverAction?.Method);
        Assert.Equal("test-request", prepared.DriverAction?.Kind);
        var target = Assert.IsType<TestRunTarget>(await runtime.Targets.FindAsync(prepared.TestRunId));
        Assert.Equal(TestRunTargetState.Prepared, target.State);
        Assert.NotNull(target.WorkflowExecutionId);
        Assert.NotNull(target.IdempotencyKey);

        var refreshed = await handler.GetAsync(prepared.TestRunId, CancellationToken.None);
        Assert.Equal("Prepared", refreshed?.Status);

        var stopped = await handler.StopAsync(prepared.TestRunId, CancellationToken.None);
        Assert.Equal("Stopped", stopped?.Status);
        Assert.Null(stopped?.DriverAction);
        Assert.Equal(1, driver.StopCalls);
        Assert.Null(await runtime.Targets.FindAsync(prepared.TestRunId));
    }

    [Fact]
    public async Task DraftTriggerTestRunRejectsUnknownOptionBeforeSavingExecutableOrOpeningScope()
    {
        var driver = new ExternalTestRunDriver();
        using var runtime = CreateTestRunRuntime(driver);
        var handler = DraftSnapshotHandler(
            triggerBindingExtractor: new SingleBindingTriggerExtractor(),
            triggerDrivers: [driver],
            testRunTargetStore: runtime.Targets);

        var rejected = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-invalid-trigger",
            State: DraftState(),
            Start: new WorkflowTestRunStartSelection(
                WorkflowTestRunStartSelection.Trigger,
                "stale-option",
                "fire-now")), CancellationToken.None);

        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("option", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAllAsync());
        Assert.Null(await runtime.Targets.FindAsync(rejected.TestRunId));
    }

    [Fact]
    public async Task TestRunInspectionAndStopAreHiddenOutsideTheOwningTenant()
    {
        var driver = new ExternalTestRunDriver();
        using var runtime = CreateTestRunRuntime(driver);
        var authorized = DraftSnapshotHandler(
            triggerBindingExtractor: new SingleBindingTriggerExtractor(),
            triggerDrivers: [driver],
            testRunTargetStore: runtime.Targets,
            testRunTargetDispatcher: runtime.Dispatcher,
            testRunTargetCleaner: runtime.Cleaner);
        var option = Assert.Single((await authorized.GetDraftOptionsAsync(DraftOptionsRequest(), CancellationToken.None)).Options);
        var prepared = await authorized.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-tenant-scope",
            State: DraftState(),
            Start: new WorkflowTestRunStartSelection(WorkflowTestRunStartSelection.Trigger, option.OptionId, "fire-now")),
            CancellationToken.None);
        var otherTenant = DraftSnapshotHandler(
            accessContextAccessor: new StubPersistenceAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("other-tenant"))),
            triggerBindingExtractor: new SingleBindingTriggerExtractor(),
            triggerDrivers: [driver],
            testRunTargetStore: runtime.Targets,
            testRunTargetCleaner: runtime.Cleaner);

        Assert.Null(await otherTenant.GetAsync(prepared.TestRunId, CancellationToken.None));
        Assert.Null(await otherTenant.StopAsync(prepared.TestRunId, CancellationToken.None));
        Assert.NotNull(await runtime.Targets.FindAsync(prepared.TestRunId));
        Assert.Equal(0, driver.StopCalls);
    }

    [Fact]
    public async Task StopKeepsTargetForRetryWhenDriverCleanupFails()
    {
        var driver = new ExternalTestRunDriver { FailStopCalls = 1 };
        using var runtime = CreateTestRunRuntime(driver);
        var handler = DraftSnapshotHandler(
            triggerBindingExtractor: new SingleBindingTriggerExtractor(),
            triggerDrivers: [driver],
            testRunTargetStore: runtime.Targets,
            testRunTargetDispatcher: runtime.Dispatcher,
            testRunTargetCleaner: runtime.Cleaner);
        var option = Assert.Single((await handler.GetDraftOptionsAsync(DraftOptionsRequest(), CancellationToken.None)).Options);
        var prepared = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-stop-retry",
            State: DraftState(),
            Start: new WorkflowTestRunStartSelection(WorkflowTestRunStartSelection.Trigger, option.OptionId, "fire-now")),
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.StopAsync(prepared.TestRunId, CancellationToken.None));
        var closing = Assert.IsType<WorkflowTestScopeRecord>(await _testScopeStore.FindAsync(prepared.TestRunId));
        Assert.Equal(WorkflowTestScopeState.Closing, closing.State);
        Assert.Equal(TestRunTargetState.Stopped, (await runtime.Targets.FindAsync(prepared.TestRunId))?.State);

        var stopped = await handler.StopAsync(prepared.TestRunId, CancellationToken.None);

        Assert.Equal("Stopped", stopped?.Status);
        Assert.Equal(2, driver.StopCalls);
        Assert.Null(await runtime.Targets.FindAsync(prepared.TestRunId));
    }

    [Fact]
    public async Task TriggerStartRejectsInputsWhenTheExecutableValidatorReportsFindings()
    {
        var driver = new ExternalTestRunDriver();
        using var runtime = CreateTestRunRuntime(driver);
        var state = new WorkflowDefinitionState(
            [],
            Node("write-one", Text("hello")),
            [WorkflowInput("customerId", new TypeReference("String"), isRequired: false)],
            [],
            null);
        var handler = DraftSnapshotHandler(
            triggerBindingExtractor: new SingleBindingTriggerExtractor(),
            triggerDrivers: [driver],
            testRunTargetStore: runtime.Targets,
            testRunTargetDispatcher: runtime.Dispatcher,
            testRunTargetCleaner: runtime.Cleaner,
            inputValidator: new RejectingInputValidator());
        var option = Assert.Single((await handler.GetDraftOptionsAsync(
            new DraftTestRunOptionsRequest("definition-1", "snapshot-input-invalid", state),
            CancellationToken.None)).Options);

        var rejected = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-input-invalid",
            State: state,
            Start: new WorkflowTestRunStartSelection(WorkflowTestRunStartSelection.Trigger, option.OptionId, "fire-now")),
            CancellationToken.None);

        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("input validation", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAllAsync());
        Assert.Null(await runtime.Targets.FindAsync(rejected.TestRunId));
        Assert.Null(await _testScopeStore.FindAsync(rejected.TestRunId));
    }

    [Fact]
    public async Task DirectDraftTestRunRejectsMissingRequiredInputsBeforeSavingExecutableOrOpeningScope()
    {
        var state = new WorkflowDefinitionState(
            [],
            Node("write-one", Text("hello")),
            [WorkflowInput("customerId", new TypeReference("String"), isRequired: true)],
            [],
            null);
        var handler = DraftSnapshotHandler();

        var rejected = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-direct-required-input",
            State: state,
            Start: new WorkflowTestRunStartSelection(WorkflowTestRunStartSelection.Direct)),
            CancellationToken.None);

        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("input validation", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("customerId", rejected.Metadata["runtime.inputValidation.findings"], StringComparison.Ordinal);
        Assert.Empty(await _executableStore.ListAllAsync());
        Assert.Empty(await _sourceReferenceStore.ListAllAsync());
        Assert.Null(await _testScopeStore.FindAsync(rejected.TestRunId));
    }

    [Fact]
    public async Task DraftTestRunWithInputContractFailsClosedWhenInputValidatorIsUnavailable()
    {
        var state = new WorkflowDefinitionState(
            [],
            Node("write-one", Text("hello")),
            [WorkflowInput("customerId", new TypeReference("String"), isRequired: true)],
            [],
            null);
        var handler = DraftSnapshotHandler(inputValidatorUnavailable: true);

        var rejected = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-input-validator-unavailable",
            State: state),
            CancellationToken.None);

        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("validation is not configured", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAllAsync());
        Assert.Null(await _testScopeStore.FindAsync(rejected.TestRunId));
    }

    [Fact]
    public async Task RejectsKnownExpressionErrorsEvenWhenUnavailableValidationWasAcknowledged()
    {
        var validator = new StubExpressionValidator(new(
            ExpressionDraftValidationState.Errors,
            [new("JavaScript/Syntax", Elsa.Expressions.Core.Models.ExpressionDiagnosticSeverity.Error, "Unclosed delimiter.", "revision")]));
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))), expressionValidator: validator);

        var view = await handler.Handle(new StartWorkflowTestRun(
            "version-1",
            AcknowledgeUnavailableExpressionValidation: true), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Contains("validation rejected", view.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAllAsync());
    }

    [Fact]
    public async Task RequiresExplicitAcknowledgementOnlyWhenExpressionValidationIsUnavailable()
    {
        var validator = new StubExpressionValidator(new(ExpressionDraftValidationState.Unavailable, []));
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))), expressionValidator: validator);

        var rejected = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);
        var accepted = await handler.Handle(new StartWorkflowTestRun(
            "version-1",
            AcknowledgeUnavailableExpressionValidation: true), CancellationToken.None);

        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("validation is unavailable", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Unavailable", rejected.Metadata["expressionValidation.state"]);
        Assert.Equal("False", rejected.Metadata["expressionValidation.unavailableAcknowledged"]);
        Assert.Equal("DispatchAccepted", accepted.Status);
        Assert.Equal("Unavailable", accepted.Metadata["expressionValidation.state"]);
        Assert.Equal("True", accepted.Metadata["expressionValidation.unavailableAcknowledged"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableValidationAcknowledgementDoesNotOverrideKnownErrorDiagnostics(bool draft)
    {
        var validator = new StubExpressionValidator(new(
            ExpressionDraftValidationState.Unavailable,
            [new("JavaScript/Syntax", Elsa.Expressions.Core.Models.ExpressionDiagnosticSeverity.Error, "Unclosed delimiter.", "revision")]));

        var view = draft
            ? await DraftSnapshotHandler(expressionValidator: validator).Handle(new StartWorkflowDraftTestRun(
                DefinitionId: "definition-1",
                SnapshotId: "snapshot-1",
                State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null),
                AcknowledgeUnavailableExpressionValidation: true), CancellationToken.None)
            : await Handler(
                    WorkflowVersion(Node("write-one", Text("hello"))),
                    expressionValidator: validator)
                .Handle(new StartWorkflowTestRun(
                    "version-1",
                    AcknowledgeUnavailableExpressionValidation: true), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Empty(await _executableStore.ListAllAsync());
    }

    [Theory]
    [InlineData(ExpressionDraftValidationState.Errors)]
    [InlineData(ExpressionDraftValidationState.Unauthorized)]
    [InlineData(ExpressionDraftValidationState.Incompatible)]
    [InlineData(ExpressionDraftValidationState.Stale)]
    [InlineData(ExpressionDraftValidationState.Canceled)]
    public async Task AcknowledgementDoesNotOverrideNonUnavailableValidationFailures(ExpressionDraftValidationState state)
    {
        var handler = Handler(
            WorkflowVersion(Node("write-one", Text("hello"))),
            expressionValidator: new StubExpressionValidator(new(state, [])));

        var view = await handler.Handle(new StartWorkflowTestRun(
            "version-1",
            AcknowledgeUnavailableExpressionValidation: true), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Empty(await _executableStore.ListAllAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ValidatorFaultRequiresUnavailableAcknowledgementForVersionAndDraftTestRuns(bool draft)
    {
        var validator = new ThrowingExpressionValidator();

        var rejected = draft
            ? await DraftSnapshotHandler(expressionValidator: validator).Handle(new StartWorkflowDraftTestRun(
                DefinitionId: "definition-1",
                SnapshotId: "snapshot-1",
                State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null)), CancellationToken.None)
            : await Handler(
                    WorkflowVersion(Node("write-one", Text("hello"))),
                    expressionValidator: validator)
                .Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", rejected.Status);
        Assert.Contains("validation is unavailable", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Unavailable", rejected.Metadata["expressionValidation.state"]);
        Assert.Equal("expression-validation-unavailable", rejected.Metadata["expressionValidation.code"]);
        Assert.Empty(await _executableStore.ListAllAsync());
    }

    [Fact]
    public async Task ValidatorPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = Handler(
            WorkflowVersion(Node("write-one", Text("hello"))),
            expressionValidator: new CancelingExpressionValidator());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(new StartWorkflowTestRun("version-1"), cancellation.Token));

        Assert.Empty(await _executableStore.ListAllAsync());
    }

    [Fact]
    public async Task ClassifiesVersionAndDraftSnapshotDispatchesAsTestRuns()
    {
        var versionDispatcher = new CapturingStartDispatcher(Dispatcher());
        var draftDispatcher = new CapturingStartDispatcher(Dispatcher());

        await Handler(WorkflowVersion(Node("write-one", Text("hello"))), versionDispatcher)
            .Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);
        await DraftSnapshotHandler(draftDispatcher).Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-1",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null)), CancellationToken.None);

        var versionRequest = Assert.Single(versionDispatcher.Requests);
        var draftRequest = Assert.Single(draftDispatcher.Requests);
        Assert.Equal(WorkflowRunKind.TestRun, versionRequest.RunKind);
        Assert.Equal(WorkflowRunKind.TestRun, draftRequest.RunKind);
        await AssertExactTestRunReferenceAsync(versionRequest);
        await AssertExactTestRunReferenceAsync(draftRequest);
        await AssertRuntimeTestScopeAsync(versionRequest);
        await AssertRuntimeTestScopeAsync(draftRequest);
    }

    private async Task AssertExactTestRunReferenceAsync(WorkflowExecutionStartDispatchRequest request)
    {
        var sourceReferenceId = Assert.IsType<string>(request.SourceSelection?.SourceReferenceId);
        var reference = await _sourceReferenceStore.FindAsync(sourceReferenceId);
        Assert.NotNull(reference);
        Assert.Equal(request.ArtifactId, reference.ArtifactId);
        Assert.Equal(WorkflowExecutableReferenceScope.TestRun, reference.Scope);
    }

    private async Task AssertRuntimeTestScopeAsync(WorkflowExecutionStartDispatchRequest request)
    {
        var scope = Assert.IsType<WorkflowTestScope>(request.TestScope);
        Assert.Equal(WorkflowExecutionPartition.DefaultValue, scope.Partition.Value);
        Assert.Null(scope.TenantId);
        var record = await _testScopeStore.FindAsync(scope.ScopeId);
        Assert.NotNull(record);
        Assert.Equal(WorkflowTestScopeState.Open, record.State);
        Assert.True(WorkflowTestScope.ContextEquals(scope, record.Scope));
    }

    [Fact]
    public async Task StartsDraftSnapshotTransientWorkflowTestRunWithoutDurableDefinitionVersion()
    {
        var dispatcher = Dispatcher();
        var handler = DraftSnapshotHandler(dispatcher);

        var view = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-1",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null)), CancellationToken.None);

        Assert.Equal("DispatchAccepted", view.Status);
        Assert.Equal("Accepted", view.CommandDispatchStatus);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("draft:snapshot-1", view.DefinitionVersionId);
        Assert.NotNull(view.WorkflowExecutionId);
        Assert.StartsWith("artifact-", view.ArtifactId, StringComparison.Ordinal);
        // Single content-addressed store: the draft test-run artifact is present. Reference-driven
        // scope gating for dispatch (rejecting a test-run artifact from a normal runtime dispatch) is worker W3's
        // slice; the artifact-level dispatcher no longer rejects by scope.
        Assert.Equal(view.ArtifactId, Assert.Single(await _executableStore.ListAllAsync()).Identity.ArtifactId);
        var snapshot = await _testRunStore.FindDraftSnapshotAsync(view.DefinitionVersionId);
        Assert.NotNull(snapshot);
        Assert.Equal("write-one", snapshot.State.RootActivity!.NodeId);
    }

    [Fact]
    public async Task DraftTestRunCopiesAuthoredInputSourceOntoItsExpiringReference()
    {
        const string authoredJson = "{\"script\":\"return input.customerId;\"}";
        using var document = JsonDocument.Parse(authoredJson);
        var state = new WorkflowDefinitionState(
            [],
            Node("write-one", new WorkflowArgumentState(
                "Text",
                new ArgumentValue(document.RootElement.Clone(), "JavaScript"),
                null,
                null,
                null,
                false)),
            [],
            [],
            null);

        var view = await DraftSnapshotHandler().Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-authored-input",
            State: state), CancellationToken.None);

        var reference = Assert.Single(await _sourceReferenceStore.ListAllByArtifactAsync(view.ArtifactId!));
        var authored = Assert.Single(reference.AuthoredInputs);
        Assert.Equal("write-one", authored.ExecutableNodeId);
        Assert.Equal("Text", authored.InputKey);
        Assert.Equal("JavaScript", authored.ExpressionType);
        Assert.Equal(authoredJson, authored.Value.GetRawText());
    }

    [Fact]
    public async Task DraftTestRunFreezesActivityPresentationOntoItsExpiringReference()
    {
        var view = await DraftSnapshotHandler().Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-presentation",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null),
            ActivityPresentation:
            [
                new ActivityPresentationRecord(
                    "write-one",
                    "Notify buyer",
                    "Send the confirmation after payment.")
            ]), CancellationToken.None);

        var reference = Assert.Single(await _sourceReferenceStore.ListAllByArtifactAsync(view.ArtifactId!));
        var presentation = Assert.Single(reference.ActivityPresentation);
        Assert.Equal("write-one", presentation.ExecutableNodeId);
        Assert.Equal("Notify buyer", presentation.DisplayName);
        Assert.Equal("Send the confirmation after payment.", presentation.Description);
    }

    [Fact]
    public async Task TestRunDoesNotWriteSourceReferenceWhileDeletionGuardOwnsArtifact()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))));
        var first = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);
        var existingReference = Assert.Single(await _sourceReferenceStore.ListAllByArtifactAsync(first.ArtifactId!));
        await _sourceReferenceStore.RetireAsync(existingReference.SourceReferenceId, DateTimeOffset.UtcNow, "test-setup");
        var now = DateTimeOffset.UtcNow;
        var deletionGuard = await _executableStore.TryBeginDeletionAsync(first.ArtifactId!, "gc-test", now.AddMinutes(1), now);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None));

        Assert.NotNull(deletionGuard);
        Assert.Equal(first.ArtifactId, exception.ArtifactId);
        Assert.Single(await _sourceReferenceStore.ListAllByArtifactAsync(first.ArtifactId!));
        Assert.Empty(await _sourceReferenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.TestRun,
            liveOnly: true,
            now: now));
    }

    [Fact]
    public async Task RejectsDraftSnapshotCompileFailureWithSnapshotIdentity()
    {
        var handler = DraftSnapshotHandler();

        var view = await handler.Handle(new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: "snapshot-1",
            State: new WorkflowDefinitionState([], RootActivity: null, [], [], null)), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("draft:snapshot-1", view.DefinitionVersionId);
        Assert.Null(view.ArtifactId);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Contains("root activity", view.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAllAsync());
        Assert.NotNull(await _testRunStore.FindAsync(view.TestRunId));
        Assert.Null(await _testRunStore.FindDraftSnapshotAsync(view.DefinitionVersionId));
    }

    [Fact]
    public void RejectsBlankDraftSnapshotIdBeforeCreatingTestRunRequest()
    {
        var exception = Assert.Throws<ArgumentException>(() => new StartWorkflowDraftTestRun(
            DefinitionId: "definition-1",
            SnapshotId: " ",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null)));

        Assert.Equal("SnapshotId", exception.ParamName);
    }

    [Fact]
    public async Task RejectsMissingRootWithoutDispatchingExecution()
    {
        var handler = Handler(WorkflowVersion(rootActivity: null));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Null(view.ArtifactId);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("version-1", view.DefinitionVersionId);
        Assert.Contains("root activity", view.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _executableStore.ListAllAsync());
    }

    [Fact]
    public async Task RejectsUnknownActivityWithoutDispatchingExecution()
    {
        var handler = Handler(WorkflowVersion(new ActivityNode("missing", "missing-activity", [Text("hello")], [])));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Contains("missing-activity", view.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DispatchesNonLiteralExpressionInput()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("\"Hello \" + \"World\"", "JavaScript"), null, null, null, null))));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("DispatchAccepted", view.Status);
        Assert.Equal("Accepted", view.CommandDispatchStatus);
        Assert.NotNull(view.WorkflowExecutionId);
        Assert.StartsWith("artifact-", view.ArtifactId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsExpressionInputWithoutExpressionTextWithoutDispatchingExecution()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("", "JavaScript"), null, null, null, null))));

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Contains("no expression text", view.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsInvalidLiteralConversionWithoutDispatchingExecution()
    {
        var handler = Handler(
            WorkflowVersion(Node("write-one", Text("not-an-int"))),
            activityVersions: [ActivityVersion("activity-write-line", "Text", new TypeReference("Int32"))]);

        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.Equal("Rejected", view.Status);
        Assert.Null(view.WorkflowExecutionId);
        Assert.Null(view.ArtifactId);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Contains("cannot be converted", view.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestRunAppendsAnExpiringTestRunReferenceIntoTheSingleStore()
    {
        // The test run saves the artifact into the one content-addressed store and appends an expiring
        // TestRun-scope source reference (scope/expiry are reference facts). The reference is what a dispatch gates on.
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))));
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        Assert.NotNull(await _executableStore.FindAsync(view.ArtifactId!));
        var reference = Assert.Single(await _sourceReferenceStore.ListAllByArtifactAsync(view.ArtifactId!));
        Assert.Equal(WorkflowExecutableReferenceScope.TestRun, reference.Scope);
        Assert.Equal(view.ExpiresAt, reference.ExpiresAt);
        Assert.Null(reference.PublishedAt);
    }

    [Fact]
    public async Task PublishedDispatchIsRejectedForArtifactWithOnlyTestRunReferences()
    {
        // Test (d): a NORMAL published dispatch of an artifact that only carries a TestRun reference is refused by the
        // reference gate — a test-run artifact is not dispatchable as published even though it lives in the same store.
        var dispatcher = Dispatcher();
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))), dispatcher);
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(view.ArtifactId!, "normal-runtime")).AsTask());

        Assert.Equal(view.ArtifactId, exception.ArtifactId);
        Assert.Equal(WorkflowExecutableReferenceScope.Published, exception.RequiredScope);
        Assert.Equal(WorkflowExecutableReferenceRejectionReason.NoLiveReference, exception.Reason);
    }

    [Fact]
    public async Task DraftTestRunResolvesToSameArtifactIdAsBehaviorallyIdenticalVersion()
    {
        // Test (a) — the equivalence signal. A draft snapshot and a durable version with identical behavior
        // compile to the SAME artifact id because the hash is purely behavioral and the test-run flow now
        // shares the published artifact prefix (scope lives on the reference, not the id). Same id ⇒ Studio can report
        // "this draft is behaviorally identical to published vN" with no diffing.
        var identicalRoot = Node("write-one", Text("hello"));

        var versionView = await Handler(WorkflowVersion(identicalRoot))
            .Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);
        var draftView = await DraftSnapshotHandler()
            .Handle(new StartWorkflowDraftTestRun(
                DefinitionId: "definition-1",
                SnapshotId: "snapshot-1",
                State: new WorkflowDefinitionState([], identicalRoot, [], [], null)), CancellationToken.None);

        Assert.NotNull(versionView.ArtifactId);
        Assert.Equal(versionView.ArtifactId, draftView.ArtifactId);
        // Both references point at the single shared artifact.
        Assert.Single(await _executableStore.ListAllAsync());
        Assert.Equal(2, (await _sourceReferenceStore.ListAllByArtifactAsync(versionView.ArtifactId!)).Count);
    }

    [Fact]
    public async Task DispatchIsRejectedWithExpiryReasonWhenTheOnlyReferenceExpired()
    {
        // Test (b): once the test run's only (TestRun) reference has passed its ExpiresAt, dispatching the artifact is
        // refused with the distinguishing Expired reason — the artifact still exists, but nothing lets it run.
        var dispatcher = Dispatcher();
        var handler = Handler(WorkflowVersion(Node("write-one", Text("hello"))), dispatcher);
        var view = await handler.Handle(new StartWorkflowTestRun("version-1"), CancellationToken.None);

        // Drive the dispatcher's clock past the reference expiry so the same-scope reference is present but lapsed.
        var expiredDispatcher = new WorkflowStartDispatcher(
            _executableStore,
            _sourceReferenceStore,
            new InProcessWorkflowExecutionActorProvider(),
            new ShortRuntimeExecutionIdGenerator(),
            new FakeTimeProvider(view.ExpiresAt!.Value.AddSeconds(1)));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableReferenceRejectedException>(() =>
            expiredDispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(view.ArtifactId!, "test-run"), WorkflowExecutableReferenceScope.TestRun).AsTask());

        Assert.Equal(WorkflowExecutableReferenceRejectionReason.Expired, exception.Reason);
        Assert.Equal(WorkflowExecutableReferenceScope.TestRun, exception.RequiredScope);
    }

    [Fact]
    public async Task TestRunStoreDropsExpiredRunOnRead()
    {
        // #398: a test run past its ExpiresAt must never be observed, even before the periodic sweep runs.
        var store = CreateTestRunStore();
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(TestRun("testrun-expired", expiresAt: now.AddMinutes(-1)));

        Assert.Null(await store.FindAsync("testrun-expired"));
    }

    [Fact]
    public async Task TestRunStoreKeepsUnexpiredAndNeverExpiringRuns()
    {
        var store = CreateTestRunStore();
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(TestRun("testrun-live", expiresAt: now.AddMinutes(30)));
        await store.SaveAsync(TestRun("testrun-eternal", expiresAt: null));

        Assert.NotNull(await store.FindAsync("testrun-live"));
        Assert.NotNull(await store.FindAsync("testrun-eternal"));
    }

    [Fact]
    public async Task TestRunStoreKeepsUnexpiredDraftSnapshot()
    {
        var store = CreateTestRunStore();
        var snapshot = DraftSnapshot("draft:snapshot-live", expiresAt: DateTimeOffset.UtcNow.AddMinutes(30));
        await store.SaveDraftSnapshotAsync(snapshot);

        var stored = await store.FindDraftSnapshotAsync("draft:snapshot-live");

        Assert.NotNull(stored);
        Assert.Equal("write-one", stored.State.RootActivity!.NodeId);
    }

    [Fact]
    public async Task TestRunStoreDropsExpiredDraftSnapshotOnRead()
    {
        var store = CreateTestRunStore();
        await store.SaveDraftSnapshotAsync(DraftSnapshot("draft:snapshot-expired", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Null(await store.FindDraftSnapshotAsync("draft:snapshot-expired"));
    }

    [Fact]
    public async Task CleanupExpiredTestRunsRemovesOnlyExpiredRuns()
    {
        // #398: the store used to grow without bound; CleanupExpiredAsync now evicts every run whose retention
        // window has elapsed while leaving live and never-expiring runs in place.
        var store = CreateTestRunStore();
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(TestRun("testrun-old", expiresAt: now.AddMinutes(-5)));
        await store.SaveAsync(TestRun("testrun-live", expiresAt: now.AddMinutes(30)));
        await store.SaveAsync(TestRun("testrun-eternal", expiresAt: null));

        var deleted = await store.CleanupExpiredAsync(now);

        Assert.Equal(1, deleted);
        Assert.Null(await store.FindAsync("testrun-old"));
        Assert.NotNull(await store.FindAsync("testrun-live"));
        Assert.NotNull(await store.FindAsync("testrun-eternal"));
    }

    [Fact]
    public async Task CleanupExpiredTestRuns_ClosesRuntimeScopeBeforeDeletingProjection()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowTestScopeStore, RejectingWorkflowTestScopeStore>();
        await using var provider = services.BuildServiceProvider();
        var store = new InMemoryWorkflowTestRunStore(provider.GetRequiredService<IServiceScopeFactory>());
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        await store.SaveAsync(TestRun("testrun-close-first", expiresAt));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CleanupExpiredAsync(expiresAt.AddTicks(1)).AsTask());

        Assert.NotNull(await store.FindAsync("testrun-close-first"));
    }

    [Fact]
    public async Task CleanupExpiredTestRunsRemovesExpiredDraftSnapshots()
    {
        var store = CreateTestRunStore();
        var now = DateTimeOffset.UtcNow;
        await store.SaveDraftSnapshotAsync(DraftSnapshot("draft:snapshot-old", expiresAt: now.AddMinutes(-5)));
        await store.SaveDraftSnapshotAsync(DraftSnapshot("draft:snapshot-live", expiresAt: now.AddMinutes(30)));

        var deleted = await store.CleanupExpiredAsync(now);

        Assert.Equal(1, deleted);
        Assert.Null(await store.FindDraftSnapshotAsync("draft:snapshot-old"));
        Assert.NotNull(await store.FindDraftSnapshotAsync("draft:snapshot-live"));
    }

    private static WorkflowTestRun TestRun(string testRunId, DateTimeOffset? expiresAt) =>
        new(
            TestRunId: testRunId,
            DefinitionId: "definition-1",
            DefinitionVersionId: "version-1",
            ArtifactId: null,
            WorkflowExecutionId: null,
            Status: WorkflowTestRunStatus.Rejected,
            RequestedBy: "test",
            RequestedAt: expiresAt?.AddMinutes(-30) ?? DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt,
            Reason: null,
            Metadata: new Dictionary<string, string>());

    private static InMemoryWorkflowTestRunStore CreateTestRunStore(IWorkflowTestScopeStore? scopeStore = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowTestScopeStore>(scopeStore ?? new InMemoryWorkflowTestScopeStore());
        var provider = services.BuildServiceProvider();
        return new InMemoryWorkflowTestRunStore(provider.GetRequiredService<IServiceScopeFactory>());
    }

    private TestRunRuntime CreateTestRunRuntime(ITestRunTriggerDriver driver)
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.RemoveAll<IWorkflowTestScopeStore>();
        services.AddSingleton<IWorkflowTestScopeStore>(_testScopeStore);
        services.RemoveAll<IWorkflowExecutableStore>();
        services.AddSingleton<IWorkflowExecutableStore>(_executableStore);
        services.RemoveAll<IWorkflowStartDispatcher>();
        services.AddSingleton<IWorkflowStartDispatcher>(Dispatcher());
        services.AddSingleton<ITestRunTriggerDriver>(driver);
        return new(services.BuildServiceProvider());
    }

    private static WorkflowTestRunDraftSnapshot DraftSnapshot(string definitionVersionId, DateTimeOffset? expiresAt) =>
        new(
            DefinitionId: "definition-1",
            DefinitionVersionId: definitionVersionId,
            ArtifactVersion: "draft",
            State: new WorkflowDefinitionState([], Node("write-one", Text("hello")), [], [], null),
            RequestedAt: expiresAt?.AddMinutes(-30) ?? DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt);

    private StartWorkflowTestRunRequestHandler Handler(
        WorkflowDefinitionVersion workflowVersion,
        IWorkflowStartDispatcher? dispatcher = null,
        IReadOnlyCollection<ActivityDefinitionVersion>? activityVersions = null,
        IExpressionDraftSemanticValidator? expressionValidator = null,
        IPersistenceAccessContextAccessor? accessContextAccessor = null,
        Action<WorkflowExecutableCompileRequest>? onCompile = null)
    {
        var versionStore = new FakeVersionStore(workflowVersion);
        return new(
            ObserveCompiler(TestCompiler.Create(
                versionStore,
                new FakeActivityVersionStore((activityVersions ?? [_writeLineActivity]).ToList()),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()), onCompile),
            _executableStore,
            _sourceReferenceStore,
            new EmptyWorkflowDefinitionVersionLayoutStore(),
            _testRunStore,
            dispatcher ?? Dispatcher(),
            TestRootWriteLeases.Create(_executableStore),
            TimeProvider.System,
            versionStore,
            new WorkflowExecutableAuthoredInputsSidecar(new ActivityTreeProjector(_activityStructureService)),
        inputValidator: new WorkflowExecutableInputValidator(TestWellKnownTypeRegistry.Create()),
            testScopeStore: _testScopeStore,
            expressionValidator: expressionValidator ?? StubExpressionValidator.Valid,
            accessContextAccessor: accessContextAccessor);
    }

    private StartWorkflowTestRunRequestHandler DraftSnapshotHandler(
        IWorkflowStartDispatcher? dispatcher = null,
        IExpressionDraftSemanticValidator? expressionValidator = null,
        IWorkflowTriggerBindingExtractor? triggerBindingExtractor = null,
        IEnumerable<ITestRunTriggerDriver>? triggerDrivers = null,
        ITestRunTargetStore? testRunTargetStore = null,
        ITestRunTargetDispatcher? testRunTargetDispatcher = null,
        ITestRunTargetCleaner? testRunTargetCleaner = null,
        IWorkflowExecutableInputValidator? inputValidator = null,
        IPersistenceAccessContextAccessor? accessContextAccessor = null,
        IWorkflowExecutionPartitionAccessor? partitionAccessor = null,
        bool inputValidatorUnavailable = false,
        Action<WorkflowExecutableCompileRequest>? onCompile = null)
    {
        return new(
            ObserveCompiler(TestCompiler.Create(
                new ThrowingVersionStore(),
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()), onCompile),
            _executableStore,
            _sourceReferenceStore,
            new EmptyWorkflowDefinitionVersionLayoutStore(),
            _testRunStore,
            dispatcher ?? Dispatcher(),
            TestRootWriteLeases.Create(_executableStore),
            TimeProvider.System,
            authoredInputsSidecar: new WorkflowExecutableAuthoredInputsSidecar(new ActivityTreeProjector(_activityStructureService)),
            testScopeStore: _testScopeStore,
            expressionValidator: expressionValidator ?? StubExpressionValidator.Valid,
            triggerBindingExtractor: triggerBindingExtractor,
            triggerDrivers: triggerDrivers,
            testRunTargetStore: testRunTargetStore,
            testRunTargetDispatcher: testRunTargetDispatcher,
            testRunTargetCleaner: testRunTargetCleaner,
            inputValidator: inputValidatorUnavailable
                ? null
                : inputValidator ?? new WorkflowExecutableInputValidator(TestWellKnownTypeRegistry.Create()),
            accessContextAccessor: accessContextAccessor,
            partitionAccessor: partitionAccessor);
    }

    private static IWorkflowExecutableCompiler ObserveCompiler(IWorkflowExecutableCompiler compiler, Action<WorkflowExecutableCompileRequest>? observer) =>
        observer is null ? compiler : new ObservingCompiler(compiler, observer);

    private sealed class ObservingCompiler(IWorkflowExecutableCompiler inner, Action<WorkflowExecutableCompileRequest> observer) : IWorkflowExecutableCompiler
    {
        public ValueTask<WorkflowExecutable> CompileAsync(WorkflowExecutableCompileRequest request, CancellationToken cancellationToken = default)
        {
            observer(request);
            return inner.CompileAsync(request, cancellationToken);
        }
    }

    private static WorkflowDefinitionState DraftState() =>
        new([], Node("write-one", Text("hello")), [], [], null);

    private static DraftTestRunOptionsRequest DraftOptionsRequest() =>
        new("definition-1", "snapshot-trigger", DraftState());

    private static InputDefinition WorkflowInput(string name, TypeReference type, bool isRequired) =>
        new(
            ReferenceKey: $"input-{name}",
            Name: name,
            Type: type,
            StorageDriverType: null,
            DisplayName: name,
            Category: null,
            IsNullable: !isRequired,
            IsRequired: isRequired,
            DefaultValue: null,
            DefaultSyntax: null);

    private sealed class SingleBindingTriggerExtractor : IWorkflowTriggerBindingExtractor
    {
        public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable) =>
            Evaluate(executable).Bindings;

        public WorkflowTriggerPreflightOutcome Evaluate(WorkflowExecutable executable)
        {
            var node = executable.RootActivity ?? throw new InvalidOperationException("The test executable has no root node.");
            var binding = new WorkflowTriggerBinding(
                WorkflowTriggerBinding.BuildId(executable.Identity.ArtifactId, node.ExecutableNodeId, "test-stimulus"),
                executable.Identity.ArtifactId,
                executable.Identity.DefinitionId,
                executable.Identity.ArtifactVersion,
                executable.Identity.ArtifactHash,
                node.ExecutableNodeId,
                "test.trigger",
                "test-stimulus",
                null,
                new Dictionary<string, string>(),
                DateTimeOffset.UtcNow,
                Cardinality: TriggerCardinality.Exclusive);
            return new WorkflowTriggerPreflightOutcome(
                executable.Identity,
                [new WorkflowTriggerNodePreflightOutcome(
                    node.ExecutableNodeId,
                    node.ActivityType,
                    "test.provider",
                    WorkflowTriggerPreflightStatus.Registered,
                    [binding])]);
        }
    }

    private sealed class ExternalTestRunDriver : ITestRunTriggerDriver
    {
        public string DriverId => "test-driver";
        public int StopCalls { get; private set; }
        public int FailStopCalls { get; set; }

        public bool Supports(TestRunTriggerContext context) =>
            StringComparer.Ordinal.Equals(context.ProviderId, "test.provider");

        public IReadOnlyCollection<TestRunTriggerMode> Describe(TestRunTriggerContext context) =>
        [
            new TestRunTriggerMode("fire-now", "Fire now", "Send one test request.", isDefault: true)
        ];

        public TestRunTriggerValidation Validate(
            TestRunTriggerContext context,
            string modeId,
            JsonElement? data) =>
            modeId == "fire-now"
                ? TestRunTriggerValidation.Success()
                : TestRunTriggerValidation.Failure([new("mode.invalid", "The selected mode is unavailable.")]);

        public ValueTask<TestRunTriggerPreparation> PrepareAsync(
            TestRunTriggerPrepareContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TestRunTriggerPreparation.External(
                new TestRunDriverAction("test-request", $"/test-runs/{context.TestRunId}/fire", "POST")));

        public ValueTask StopAsync(
            TestRunTriggerStopContext context,
            CancellationToken cancellationToken = default)
        {
            StopCalls++;
            if (FailStopCalls > 0)
            {
                FailStopCalls--;
                return ValueTask.FromException(new InvalidOperationException("Injected driver cleanup failure."));
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubPersistenceAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class RejectingInputValidator : IWorkflowExecutableInputValidator
    {
        public WorkflowExecutableInputValidation Validate(
            WorkflowExecutableInputContract contract,
            IEnumerable<KeyValuePair<string, JsonElement>> inputs) =>
            WorkflowExecutableInputValidation.Failure([
                new WorkflowExecutableInputValidationFinding("runtime.input.invalid", "An input is invalid.", "customerId")
            ]);

        public WorkflowExecutableInputValidation ValidateKnownInputs(
            WorkflowExecutableInputContract contract,
            IEnumerable<KeyValuePair<string, JsonElement>> inputs) =>
            Validate(contract, inputs);
    }

    private WorkflowStartDispatcher Dispatcher() =>
        new(
            _executableStore,
            _sourceReferenceStore,
            new InProcessWorkflowExecutionActorProvider(),
            new ShortRuntimeExecutionIdGenerator());

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        new("definition-1", "1.0.0")
        {
            Id = "version-1",
            Definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        new(nodeId, "activity-write-line", inputs, Outputs: []);

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeReference inputType) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = "Test.WriteLine",
                Category = "Test"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TestActivityAliases.ForActivityVersionId(id))),
            Inputs = [new InputDefinition(inputName, inputName, inputType, null, inputName, null, false)]
        };

    private static IActivityStructureService ActivityStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private sealed class StubExpressionValidator(ExpressionDraftValidationResult result) : IExpressionDraftSemanticValidator
    {
        public static readonly StubExpressionValidator Valid = new(new(ExpressionDraftValidationState.Valid, []));

        public ValueTask<ExpressionDraftValidationResult> ValidateAsync(
            WorkflowDefinitionState state,
            string documentScope,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }

    private sealed class ThrowingExpressionValidator : IExpressionDraftSemanticValidator
    {
        public ValueTask<ExpressionDraftValidationResult> ValidateAsync(
            WorkflowDefinitionState state,
            string documentScope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Validation provider failed.");
    }

    private sealed class CancelingExpressionValidator : IExpressionDraftSemanticValidator
    {
        public ValueTask<ExpressionDraftValidationResult> ValidateAsync(
            WorkflowDefinitionState state,
            string documentScope,
            CancellationToken cancellationToken) =>
            ValueTask.FromCanceled<ExpressionDraftValidationResult>(cancellationToken);
    }

    private sealed class ThrowingVersionStore : IWorkflowDefinitionVersionStore
    {
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Draft snapshot test runs must not read durable workflow definition versions.");

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class CapturingStartDispatcher(IWorkflowStartDispatcher inner) : IWorkflowStartDispatcher
    {
        public List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.DispatchAsync(request, requiredScope, dispatchOptions, cancellationToken);
        }
    }

    private sealed class TestRunRuntime : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;

        public TestRunRuntime(ServiceProvider provider)
        {
            _provider = provider;
            _scope = provider.CreateScope();
            Targets = _scope.ServiceProvider.GetRequiredService<ITestRunTargetStore>();
            Dispatcher = _scope.ServiceProvider.GetRequiredService<ITestRunTargetDispatcher>();
            Cleaner = _scope.ServiceProvider.GetRequiredService<ITestRunTargetCleaner>();
        }

        public ITestRunTargetStore Targets { get; }
        public ITestRunTargetDispatcher Dispatcher { get; }
        public ITestRunTargetCleaner Cleaner { get; }

        public void Dispose()
        {
            _scope.Dispose();
            _provider.Dispose();
        }
    }

    private sealed class RejectingWorkflowTestScopeStore : IWorkflowTestScopeStore
    {
        public ValueTask<WorkflowTestScopeRecord> CreateAsync(
            WorkflowTestScope scope,
            DateTimeOffset createdAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkflowTestScopeRecord?> FindAsync(
            string scopeId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkflowTestScopeCloseResult> CloseAsync(
            WorkflowTestScopeCloseRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected runtime scope-close failure.");

        public ValueTask<WorkflowTestScopeRecord> CompleteAsync(
            string scopeId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<WorkflowTestScopePage> QueryAsync(
            WorkflowTestScopePageQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
