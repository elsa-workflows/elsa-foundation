using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Sequence.Models;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Models;
using Elsa.Primitives.Persistence;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Exceptions;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Api.Tests.Support;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Requests;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using ArgumentValue = Elsa.Expressions.Core.Models.ArgumentValue;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublishWorkflowRequestHandlerTests
{
    private const string UnknownStructureKind = "test.opaque.structure";
    private const string UnknownStructureSchemaVersion = "1.0.0";
    private static readonly DateTimeOffset ReferenceEvaluationTime = DateTimeOffset.Parse("2026-07-19T12:00:00Z");

    private readonly InMemoryWorkflowExecutableStore _store = new();
    private readonly ActivityDefinitionVersion _writeLineActivity = ActivityVersion("activity-write-line", "Text", new TypeReference("String"));
    private readonly ActivityDefinitionVersion _sequenceActivity = ActivityVersion(
        "activity-sequence",
        typeof(SequenceActivity).FullName!,
        [],
        StructureFacet(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion));
    private readonly ActivityDefinitionVersion _flowchartActivity = ActivityVersion(
        "activity-flowchart",
        typeof(FlowchartActivity).FullName!,
        [],
        StructureFacet(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion));
    private readonly IActivityStructureService _activityStructureService = ActivityStructureService();
    private readonly InMemoryWorkflowTriggerBindingStore _bindingStore = new();
    private readonly InMemoryWorkflowActivationAuthority _activationAuthority = new();
    private readonly InMemoryPublicationRecordStore _publicationStore = new();
    private readonly InMemoryPublicationPolicyStore _policyStore = new();
    private readonly PublicationSnapshotReviewService _snapshotReviews = new(
        TimeProvider.System,
        new InMemoryPublicationSnapshotReviewStore());
    private IPublicationPreflightService _preflightService = new PublicationPreflightService();
    private IWorkflowActivationAuthority? _preflightAuthority;
    private Func<IPublicationActivator, IPublicationActivator>? _activatorWrapper;

    private static readonly WorkflowActivationSource ImportOwner = WorkflowActivationSource.ArtifactReconciliation("mounted-artifacts");
    private static readonly string DefaultSlotId = WorkflowActivationSlotIdentity.Create("definition-1", "default");

    [Fact]
    public async Task Publishes_reviewed_snapshot_when_candidate_and_authority_are_unchanged()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var layout = new WorkflowDefinitionVersionLayout
        {
            WorkflowDefinitionVersionId = workflowVersion.Id,
            Records = [new DesignMetadataRecord("write-one", 10, 20)]
        };
        var issued = await _snapshotReviews.IssueAsync(
            _snapshotReviews.ComputeCandidateHash(workflowVersion.State, layout.Records),
            SnapshotPlan());

        var result = await Handler(workflowVersion, layout, _writeLineActivity)
            .Handle(new PublishWorkflow(workflowVersion.Id, PreflightToken: issued.PreflightToken), CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.NotNull(await _store.FindAsync(result.ArtifactId));
    }

    [Theory]
    [InlineData("Literal")]
    [InlineData("Variable")]
    [InlineData("JavaScript")]
    public async Task A_literal_or_expression_on_a_credential_input_is_refused_and_nothing_is_published(string expressionType)
    {
        // T054 (spec 188, FR-008, FR-009): the credential rule refuses the binding, ahead of VF-ACT-011.
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("typed-in", expressionType), null, null, null, null)));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Handler(workflowVersion, CredentialWriteLineActivity()).Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        Assert.StartsWith("Inputs/CredentialLiteral: input 'Text' on activity 'write-one'", exception.Message, StringComparison.Ordinal);
        Assert.IsType<CredentialLiteralRefusedException>(exception.InnerException);
        Assert.Empty(await _store.ListAllAsync());
        Assert.Empty(await _publicationStore.ListBySlotAsync(DefaultSlotId));
    }

    [Fact]
    public async Task A_credential_literal_published_over_http_is_answered_with_400_naming_the_rule_node_and_input_and_never_the_value()
    {
        // T062 (spec 188, FR-008): the publish route's 400 for the refusal rests on the translator's ArgumentException arm.
        var literal = $"literal-value-{Guid.NewGuid():N}";
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue(literal, "Literal"), null, null, null, null)));
        var handler = Handler(workflowVersion, CredentialWriteLineActivity());
        await using var host = await PublishingMinimalApiHost.StartAsync(_ => new HandlerSender(handler));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/publishing/workflows/{workflowVersion.Id}/publish")
        {
            Content = new StringContent($"{{\"versionId\":\"{workflowVersion.Id}\"}}", System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(PublishingCompatibilityCases.IdentityHeader, "trusted-success");

        using var response = await host.Client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Inputs/CredentialLiteral: input 'Text' on activity 'write-one'", body, StringComparison.Ordinal);
        Assert.DoesNotContain(literal, body, StringComparison.Ordinal);
        Assert.Empty(await _store.ListAllAsync());
    }

    [Fact]
    public async Task An_activity_the_catalog_does_not_hold_is_refused_at_publish_and_once_installed_its_credential_literal_is_refused()
    {
        // T112 (spec 188, FR-008 edge case, research R7 residual): the version holds a literal on the input the activity
        // declares a credential once installed, stored as add-version stores it, where nothing could judge the node.
        var workflowVersion = WorkflowVersion(SequenceNode("sequence", [Node("send", new WorkflowArgumentState("Text", new ArgumentValue("typed-in", "Literal"), null, null, null, null))]));

        var uninstalled = await Record.ExceptionAsync(() =>
            Handler(workflowVersion, _sequenceActivity).Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        Assert.NotNull(uninstalled);
        Assert.Contains("activity-write-line", uninstalled.Message, StringComparison.Ordinal);
        Assert.Empty(await _store.ListAllAsync());
        Assert.Empty(await _publicationStore.ListBySlotAsync(DefaultSlotId));

        // The activity is installed with a catalog version of the same id that declares the input a credential (written
        // into the catalog directly: the Activities Design API refuses isCredential, T103).
        var installed = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            Handler(workflowVersion, _sequenceActivity, CredentialWriteLineActivity()).Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        Assert.StartsWith("Inputs/CredentialLiteral: input 'Text' on activity 'send'", installed.Message, StringComparison.Ordinal);
        Assert.Empty(await _store.ListAllAsync());
        Assert.Empty(await _publicationStore.ListBySlotAsync(DefaultSlotId));
    }

    [Fact]
    public async Task A_secret_reference_on_a_credential_input_is_published()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", SecretBindingCompilerFixture.Secret("Text")));

        var result = await Handler(workflowVersion, CredentialWriteLineActivity()).Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None);

        Assert.True(result.WasCreated);
        Assert.NotNull(await _store.FindAsync(result.ArtifactId));
    }

    [Theory]
    [InlineData(ExpressionDraftValidationState.Errors)]
    [InlineData(ExpressionDraftValidationState.Unavailable)]
    [InlineData(ExpressionDraftValidationState.Unauthorized)]
    [InlineData(ExpressionDraftValidationState.Incompatible)]
    [InlineData(ExpressionDraftValidationState.Stale)]
    [InlineData(ExpressionDraftValidationState.Canceled)]
    public async Task PublicationFailsClosedWhenFullDraftExpressionValidationIsNotValid(
        ExpressionDraftValidationState state)
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(
            layout: null,
            TestCompiler.Create(
                new FakeVersionStore(workflowVersion),
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            new FakeVersionStore(workflowVersion),
            new StubExpressionValidator(new(state, [])));

        var exception = await Assert.ThrowsAsync<ExpressionPublicationValidationException>(() =>
            handler.Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        Assert.Equal(state, exception.State);
        Assert.Equal($"expression-validation-{state.ToString().ToLowerInvariant()}", exception.Code);
        Assert.Empty(await _store.ListAllAsync());
    }

    [Fact]
    public async Task PublicationValidationFailurePreservesSafeDiagnosticsAndRedactsProviderOnlyMetadata()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var diagnostic = new Elsa.Expressions.Core.Models.ExpressionDiagnostic(
            "JavaScript/Syntax",
            Elsa.Expressions.Core.Models.ExpressionDiagnosticSeverity.Error,
            "Unexpected token.",
            "document-revision",
            new(new(2, 3), new(2, 4)),
            "write-one/inputs/Text",
            ["private-symbol"]);
        var handler = Handler(
            layout: null,
            TestCompiler.Create(
                new FakeVersionStore(workflowVersion),
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            new FakeVersionStore(workflowVersion),
            new StubExpressionValidator(new(ExpressionDraftValidationState.Errors, [diagnostic], "expression-syntax")));

        var exception = await Assert.ThrowsAsync<ExpressionPublicationValidationException>(() =>
            handler.Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        Assert.Equal(ExpressionDraftValidationState.Errors, exception.State);
        Assert.Equal("expression-syntax", exception.Code);
        var safe = Assert.Single(exception.Diagnostics);
        Assert.Equal(diagnostic.Code, safe.Code);
        Assert.Equal("Error", safe.Severity);
        Assert.Equal(diagnostic.Message, safe.Message);
        Assert.Equal(diagnostic.DocumentRevision, safe.DocumentRevision);
        Assert.Equal(diagnostic.Range, safe.Range);
        Assert.Equal(diagnostic.AuthoredPath, safe.AuthoredPath);
        Assert.DoesNotContain("private-symbol", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicationFailsClosedWhenExpressionValidationIsNotComposed()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var versionStore = new FakeVersionStore(workflowVersion);
        var handler = Handler(
            layout: null,
            TestCompiler.Create(
                versionStore,
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            versionStore,
            configureExpressionValidator: false);

        var exception = await Assert.ThrowsAsync<ExpressionPublicationValidationException>(() =>
            handler.Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        Assert.Equal(ExpressionDraftValidationState.Unavailable, exception.State);
        Assert.Equal("expression-validation-unavailable", exception.Code);
        Assert.Empty(exception.Diagnostics);
        Assert.Empty(await _store.ListAllAsync());
    }

    [Fact]
    public async Task PublicationMapsValidatorFaultToUnavailableAndFailsClosed()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var versionStore = new FakeVersionStore(workflowVersion);
        var handler = Handler(
            layout: null,
            TestCompiler.Create(
                versionStore,
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            versionStore,
            new ThrowingExpressionValidator());

        var exception = await Assert.ThrowsAsync<ExpressionPublicationValidationException>(() =>
            handler.Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        Assert.Equal(ExpressionDraftValidationState.Unavailable, exception.State);
        Assert.Equal("expression-validation-unavailable", exception.Code);
        Assert.Empty(await _store.ListAllAsync());
    }

    [Fact]
    public async Task PublicationLogsWhichDependencyIsMissingWhenExpressionValidationIsNotComposed()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var versionStore = new FakeVersionStore(workflowVersion);
        var logger = new RecordingLogger<PublishWorkflowRequestHandler>();
        var handler = Handler(
            layout: null,
            TestCompiler.Create(
                versionStore,
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            versionStore,
            configureExpressionValidator: false,
            logger: logger);

        await Assert.ThrowsAsync<ExpressionPublicationValidationException>(() =>
            handler.Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains(nameof(IExpressionDraftSemanticValidator), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IWorkflowDefinitionVersionStore), entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicationLogsTheValidatorFaultItReportsAsUnavailable()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var versionStore = new FakeVersionStore(workflowVersion);
        var logger = new RecordingLogger<PublishWorkflowRequestHandler>();
        var handler = Handler(
            layout: null,
            TestCompiler.Create(
                versionStore,
                new FakeActivityVersionStore([_writeLineActivity]),
                _activityStructureService,
                TestWellKnownTypeRegistry.Create()),
            versionStore,
            new ThrowingExpressionValidator(),
            logger: logger);

        await Assert.ThrowsAsync<ExpressionPublicationValidationException>(() =>
            handler.Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        var logged = Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Equal("Validation provider failed.", logged.Message);
    }

    [Fact]
    public async Task Stale_snapshot_token_fails_before_persisting_the_compiled_artifact()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var issued = await _snapshotReviews.IssueAsync(
            _snapshotReviews.ComputeCandidateHash(WorkflowDefinitionState.Empty, []),
            SnapshotPlan());

        await Assert.ThrowsAsync<PublicationSnapshotReviewException>(() =>
            Handler(workflowVersion)
                .Handle(new PublishWorkflow(workflowVersion.Id, PreflightToken: issued.PreflightToken), CancellationToken.None));

        Assert.Empty(await _store.ListAllAsync());
        Assert.Empty(await _referenceStore.ListAllAsync(WorkflowExecutableReferenceScope.Published));
    }

    [Fact]
    public async Task PublishesRootActivityIntoExecutableArtifact()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("definition-1", view.DefinitionId);
        Assert.Equal("version-1", view.DefinitionVersionId);
        Assert.Equal("write-one", view.RootActivityId);
        Assert.Equal(1, view.NodeCount);
        Assert.Equal("write-one", executable.RootActivity.ExecutableNodeId);
        Assert.Equal("one", executable.NodesById["write-one"].InputBindings["Text"].LiteralValue!.Value.GetString());
        var binding = executable.NodesById["write-one"].InputBindings["Text"];
        Assert.Equal("String", binding.TargetType.Alias);
        Assert.DoesNotContain("typeName", binding.Metadata);
    }

    [Fact]
    public async Task Publication_propagates_tenant_to_compilation_and_source_reference()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var (handler, compiler) = RecordingHandler(workflowVersion);

        var published = await handler.Handle(
            new PublishWorkflow(workflowVersion.Id, TenantId: "tenant-a"),
            CancellationToken.None);

        Assert.Equal("tenant-a", Assert.Single(compiler.Requests).TenantId);
        var reference = await _referenceStore.FindAsync(published.SourceReferenceId!);
        Assert.NotNull(reference);
        Assert.Equal("tenant-a", reference.TenantId);
    }

    [Fact]
    public async Task Publication_without_tenant_keeps_compilation_and_source_reference_unscoped()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var (handler, compiler) = RecordingHandler(workflowVersion);

        var published = await handler.Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None);

        Assert.Null(Assert.Single(compiler.Requests).TenantId);
        var reference = await _referenceStore.FindAsync(published.SourceReferenceId!);
        Assert.NotNull(reference);
        Assert.Null(reference.TenantId);
    }

    [Fact]
    public async Task Tenant_source_fact_does_not_change_artifact_identity()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var first = await handler.Handle(
            new PublishWorkflow(workflowVersion.Id, TenantId: "tenant-a"),
            CancellationToken.None);
        var second = await handler.Handle(
            new PublishWorkflow(
                workflowVersion.Id,
                PublicationAction.PublishSideBySide,
                SlotName: "blue",
                TenantId: "tenant-b"),
            CancellationToken.None);

        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.Equal(first.ArtifactHash, second.ArtifactHash);
        Assert.NotEqual(first.PublicationId, second.PublicationId);
        Assert.NotEqual(first.SourceReferenceId, second.SourceReferenceId);
        var references = await _referenceStore.ListAllByArtifactAsync(first.ArtifactId);
        Assert.Equal(["tenant-a", "tenant-b"], references.Select(x => x.TenantId).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task RepublishingIdenticalArtifactKeepsExistingPublicationAuthority()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("one"))));

        var first = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var second = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.False(second.WasCreated);
        Assert.Equal(first.PublicationId, second.PublicationId);
        Assert.Equal(first.SourceReferenceId, second.SourceReferenceId);
        Assert.Equal(PublicationStatusView.Active, second.Status);
        Assert.Single(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
    }

    [Fact]
    public async Task A_same_version_publish_that_read_the_slot_before_it_moved_is_answered_with_the_publication_the_slot_names()
    {
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var first = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        // The loser of a race preflighted before the winner's slot transition, so the handler's early return does not apply
        // and its candidate reaches a coordinator that finds the artifact already serving.
        _preflightAuthority = new SlotReadBeforeItMoved(_activationAuthority);

        var second = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.False(second.WasCreated);
        Assert.Equal(first.PublicationId, second.PublicationId);
        Assert.Equal(first.SourceReferenceId, second.SourceReferenceId);
        Assert.Equal(PublicationStatusView.Active, second.Status);
        var records = await _publicationStore.ListBySlotAsync(DefaultSlotId);
        Assert.Equal(first.PublicationId, Assert.Single(records, record => record.Status == PublicationStatus.Active).PublicationId);
        Assert.Equal(PublicationFailureCodes.ArtifactAlreadyServing, Assert.Single(records, record => record.Status == PublicationStatus.Failed).Failure?.Code);
        Assert.Equal(first.PublicationId, (await _activationAuthority.FindAsync("definition-1", "default"))!.ActiveActivationId);
    }

    [Fact]
    public async Task A_same_version_publish_whose_served_source_reference_was_retired_is_refused_not_reported_as_published()
    {
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var first = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        _preflightAuthority = new SlotReadBeforeItMoved(_activationAuthority);
        // The activation answers with the publication the slot already serves; its reference is retired right after.
        _activatorWrapper = activator => new RetiresServedReferenceAfterActivation(activator, _referenceStore, first.SourceReferenceId!);

        var failure = await Assert.ThrowsAsync<PublicationActivationException>(() =>
            Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("source reference is gone", failure.Message);
    }

    [Fact]
    public async Task Republishing_identical_artifact_to_same_slot_does_not_reuse_another_tenants_authority()
    {
        var handler = Handler(WorkflowVersion(Node("write-one", Text("one"))));

        var first = await handler.Handle(
            new PublishWorkflow("version-1", TenantId: "tenant-a"),
            CancellationToken.None);
        var second = await handler.Handle(
            new PublishWorkflow("version-1", TenantId: "tenant-b"),
            CancellationToken.None);

        Assert.True(first.WasCreated);
        Assert.True(second.WasCreated);
        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.NotEqual(first.PublicationId, second.PublicationId);
        Assert.NotEqual(first.SourceReferenceId, second.SourceReferenceId);
        var liveReference = Assert.Single(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
        Assert.Equal(second.SourceReferenceId, liveReference.SourceReferenceId);
        Assert.Equal("tenant-b", liveReference.TenantId);
    }

    [Fact]
    public async Task SameBehaviorFromTwoDefinitionsYieldsOneArtifactAndTwoIndependentReferences()
    {
        // Each definition owns an independent default slot even when both publications reuse one content-addressed
        // artifact. Neither definition's publication replaces the other definition's authority.
        var firstVersion = WorkflowVersion(Node("write-one", Text("one")), definitionId: "definition-A", versionId: "version-A", version: "1.0.0");
        var secondVersion = WorkflowVersion(Node("write-one", Text("one")), definitionId: "definition-B", versionId: "version-B", version: "3.7.0");

        var first = await Handler(firstVersion).Handle(new PublishWorkflow("version-A"), CancellationToken.None);
        var second = await Handler(secondVersion).Handle(new PublishWorkflow("version-B"), CancellationToken.None);

        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.Equal(first.ArtifactHash, second.ArtifactHash);
        Assert.Single(await _store.ListAllAsync());

        var references = await _referenceStore.ListAllByArtifactAsync(first.ArtifactId);
        Assert.Equal(2, references.Count);
        Assert.Equal(["definition-A", "definition-B"], references.Select(r => r.DefinitionId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(["version-A", "version-B"], references.Select(r => r.DefinitionVersionId).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(references, reference =>
        {
            Assert.Equal(first.ArtifactId, reference.ArtifactId);
            Assert.Equal(WorkflowExecutableReferenceScope.Published, reference.Scope);
            Assert.NotNull(reference.PublishedAt);
            Assert.Null(reference.ExpiresAt);
        });
    }

    [Fact]
    public async Task PublishingToOccupiedDefaultSlotReplacesItsLiveAuthority()
    {
        var firstVersion = WorkflowVersion(Node("write-one", Text("one")), "definition-1", "version-1", "1.0.0");
        var secondVersion = WorkflowVersion(Node("write-two", Text("two")), "definition-1", "version-2", "2.0.0");

        var first = await Handler(firstVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var storedFirst = await _store.FindAsync(first.ArtifactId);
        var second = await Handler(secondVersion).Handle(new PublishWorkflow("version-2"), CancellationToken.None);
        var storedSecond = await _store.FindAsync(second.ArtifactId);

        Assert.NotEqual(first.ArtifactId, second.ArtifactId);
        Assert.NotNull(storedFirst);
        Assert.NotNull(storedSecond);

        var liveReferences = await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime);
        var retiredReference = await _referenceStore.FindAsync(first.SourceReferenceId!);

        Assert.Collection(liveReferences, reference => Assert.Equal(second.SourceReferenceId, reference.SourceReferenceId));
        Assert.NotNull(retiredReference?.DeletedAt);
        Assert.NotEqual(first.SourceReferenceId, second.SourceReferenceId);
    }

    [Fact]
    public async Task ExplicitNamedSlotKeepsDefaultAndNamedAuthoritiesLiveSideBySide()
    {
        var defaultVersion = WorkflowVersion(Node("write-default", Text("default")), "definition-1", "version-1", "1.0.0");
        var namedVersion = WorkflowVersion(Node("write-blue", Text("blue")), "definition-1", "version-2", "2.0.0");

        var defaultPublication = await Handler(defaultVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var namedPublication = await Handler(namedVersion).Handle(NamedPublish("version-2", "blue"), CancellationToken.None);

        var liveReferences = await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime);

        Assert.Equal(2, liveReferences.Count);
        Assert.Contains(liveReferences, reference => reference.SourceReferenceId == defaultPublication.SourceReferenceId);
        Assert.Contains(liveReferences, reference => reference.SourceReferenceId == namedPublication.SourceReferenceId);
    }

    [Fact]
    public async Task Publishing_to_a_slot_owned_by_another_activation_source_is_refused_before_any_write()
    {
        var imported = await OccupyDefaultSlotAsync(ImportOwner);

        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() =>
            Handler(WorkflowVersion(Node("write-one", Text("one")))).Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        // The same failure activation raised, but now ahead of all three writes a late refusal used to leave behind.
        Assert.Equal("slot_owner_conflict", refusal.Code);
        Assert.Contains(ImportOwner.Describe(), refusal.Message, StringComparison.Ordinal);
        Assert.Equal(new PublishWrites(Executables: 0, SourceReferences: 0, PublicationRecords: 0), await CountPublishWritesAsync());
        Assert.Equal(imported, await _activationAuthority.FindAsync("definition-1", "default"));
    }

    [Fact]
    public async Task Publishing_the_artifact_a_foreign_owner_already_serves_is_refused_rather_than_journaled_as_active()
    {
        // The coordinator's same-artifact no-op answers before ownership is consulted, so without the preflight owner
        // check this publish succeeded and journaled an Active publication that the slot never pointed at.
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var imported = await HandOverDefaultSlotAsync(published, ImportOwner);
        var before = await CountPublishWritesAsync();

        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() =>
            Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Equal("slot_owner_conflict", refusal.Code);
        Assert.Equal(before, await CountPublishWritesAsync());
        Assert.Equal(imported, await _activationAuthority.FindAsync("definition-1", "default"));
    }

    [Fact]
    public async Task A_foreign_owner_outranks_trigger_conflicts_in_the_refusal()
    {
        await OccupyDefaultSlotAsync(ImportOwner);
        _preflightService = new ClashingPreflightService();

        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() =>
            Handler(WorkflowVersion(Node("write-one", Text("one")))).Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        // Resolving the trigger clash would still leave the slot to its owner, so the owner is what publish reports.
        Assert.Equal("slot_owner_conflict", refusal.Code);
        Assert.Equal(new PublishWrites(Executables: 0, SourceReferences: 0, PublicationRecords: 0), await CountPublishWritesAsync());
    }

    [Fact]
    public async Task Trigger_conflicts_without_a_foreign_owner_still_refuse_as_a_preflight_conflict_before_any_write()
    {
        _preflightService = new ClashingPreflightService();

        var refusal = await Assert.ThrowsAsync<PublicationPreflightConflictException>(() =>
            Handler(WorkflowVersion(Node("write-one", Text("one")))).Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Equal(ClashingPreflightService.Clash, Assert.Single(refusal.Conflicts));
        Assert.Equal(new PublishWrites(Executables: 0, SourceReferences: 0, PublicationRecords: 0), await CountPublishWritesAsync());
    }

    [Fact]
    public async Task PublishDoesNotWriteSourceReferenceWhileDeletionGuardOwnsArtifact()
    {
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(version);
        var first = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        await _referenceStore.RetireAsync(first.SourceReferenceId!, DateTimeOffset.UtcNow, "test-setup");
        var now = DateTimeOffset.UtcNow;
        var deletionGuard = await _store.TryBeginDeletionAsync(first.ArtifactId, "gc-test", now.AddMinutes(1), now);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() =>
            handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.NotNull(deletionGuard);
        Assert.Equal(first.ArtifactId, exception.ArtifactId);
        Assert.Single(await _referenceStore.ListAllByArtifactAsync(first.ArtifactId));
        Assert.Empty(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
    }

    [Fact]
    public async Task UnpublishingSlotRetiresAuthorityWithoutDeletingItsArtifact()
    {
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        await InvokeSlotLifecycleHandlerAsync("Unpublish", "definition-1", "default");

        var reference = await _referenceStore.FindAsync(published.SourceReferenceId!);
        Assert.NotNull(reference?.DeletedAt);
        Assert.Empty(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
        Assert.NotNull(await _store.FindAsync(published.ArtifactId));
    }

    [Fact]
    public async Task RestoringUnpublishedSlotReestablishesAuthorityAfterLifecycleValidation()
    {
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        await InvokeSlotLifecycleHandlerAsync("Unpublish", "definition-1", "default");

        await InvokeSlotLifecycleHandlerAsync("Restore", "definition-1", "default");

        var liveReference = Assert.Single(await _referenceStore.ListAllAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: ReferenceEvaluationTime));
        Assert.Equal(published.ArtifactId, liveReference.ArtifactId);
        Assert.NotNull(await _store.FindAsync(published.ArtifactId));
    }

    [Fact]
    public async Task PublishedReferenceCarriesLayoutRecordsCopiedFromDefinitionVersion()
    {
        // Acceptance 3 (ADR 0039): the publication reference embeds the layout sidecar copied verbatim from the
        // definition version's layout store.
        var version = WorkflowVersion(Node("write-one", Text("one")));
        var additional = JsonSerializer.SerializeToElement(new { color = "blue" });
        var layout = new WorkflowDefinitionVersionLayout
        {
            WorkflowDefinitionVersionId = "version-1",
            Records = [new DesignMetadataRecord("write-one", 12.5, 34.0, 200, 80, additional)]
        };

        var view = await Handler(version, layout, _writeLineActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var reference = Assert.Single(await _referenceStore.ListAllByArtifactAsync(view.ArtifactId));
        var record = Assert.Single(reference.Layout);
        Assert.Equal("write-one", record.NodeId);
        Assert.Equal(12.5, record.X);
        Assert.Equal(34.0, record.Y);
        Assert.Equal(200, record.Width);
        Assert.Equal(80, record.Height);
        Assert.Equal("blue", record.AdditionalProperties!.Value.GetProperty("color").GetString());
    }

    [Fact]
    public async Task PublishedReferenceCarriesVerbatimAuthoredInputSourceOutsideArtifactHash()
    {
        const string authoredJson = "{\"code\":\"return variables.orderId;\",\"options\":{\"strict\":true}}";
        using var document = JsonDocument.Parse(authoredJson);
        var input = new WorkflowArgumentState(
            "Text",
            new ArgumentValue(document.RootElement.Clone(), "JavaScript"),
            null,
            null,
            null,
            true);
        var version = WorkflowVersion(Node("write-one", input));

        var view = await Handler(version).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var reference = Assert.Single(await _referenceStore.ListAllByArtifactAsync(view.ArtifactId));
        var authored = Assert.Single(reference.AuthoredInputs);
        Assert.Equal("write-one", authored.ExecutableNodeId);
        Assert.Equal("Text", authored.InputKey);
        Assert.Equal("JavaScript", authored.ExpressionType);
        Assert.Equal(authoredJson, authored.Value.GetRawText());

        var executable = await _store.FindAsync(view.ArtifactId);
        Assert.Equal(view.ArtifactHash, executable!.Identity.ArtifactHash);
    }

    /// <summary>
    /// The authored-inputs view over a real publish of nested nodes (spec 188, T070): each authored input of a container's
    /// children is matched to its compiled node, so an ordinary literal is shown and a literal on an input the activity
    /// declares sensitive is redacted by its compiled policy, although its author did not mark it. Nothing else is
    /// redacted.
    /// </summary>
    [Fact]
    public async Task AuthoredInputSourcesOfNestedNodesAreRedactedOnlyWhereTheCompiledPolicyHidesThem()
    {
        var root = SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                new ActivityNode(
                    "note-one",
                    typeof(DeclaredInputsActivity).FullName!,
                    [new WorkflowArgumentState(nameof(DeclaredInputsActivity.Note), new ArgumentValue("classified", "Literal"), null, null, null, null)],
                    Outputs: [])
            ]);
        var published = await Handler(
                WorkflowVersion(root),
                _writeLineActivity,
                SecretBindingCompilerFixture.ClrActivityVersion(typeof(DeclaredInputsActivity)),
                _sequenceActivity)
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var sources = await new WorkflowExecutableInspector(_store, _referenceStore, new InMemoryWorkflowExecutionStateStore())
            .GetInputSourcesAsync(published.ArtifactId, published.SourceReferenceId);

        Assert.Collection(
            sources!.AuthoredInputs.OrderBy(input => input.ExecutableNodeId, StringComparer.Ordinal),
            note =>
            {
                Assert.Equal(("note-one", nameof(DeclaredInputsActivity.Note)), (note.ExecutableNodeId, note.InputKey));
                Assert.True(note.IsSensitive);
                Assert.Equal("redacted", note.AccessState);
                Assert.Null(note.Value);
            },
            write =>
            {
                Assert.Equal(("write-one", "Text"), (write.ExecutableNodeId, write.InputKey));
                Assert.False(write.IsSensitive);
                Assert.Equal("allowed", write.AccessState);
                Assert.Equal("one", write.Value!.Value.GetString());
            });
    }

    [Fact]
    public void ActivityPresentationSidecarMapsAuthoredIdsToExecutableIds()
    {
        var firstPlacement = new ExecutableNode(
            "exec-shared-1",
            "authored-shared",
            "Test.Activity",
            "1.0.0",
            new RuntimeActivityDescriptor(
                "test",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
        var secondPlacement = new ExecutableNode(
            "exec-shared-2",
            "authored-shared",
            "Test.Activity",
            "1.0.0",
            new RuntimeActivityDescriptor(
                "test",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());
        var root = new ExecutableNode(
            "exec-root",
            "authored-root",
            "Test.Activity",
            "1.0.0",
            new RuntimeActivityDescriptor(
                "test",
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot("Body", [firstPlacement, secondPlacement])]);
        var executable = new WorkflowExecutable(
            new("artifact-1", "definition-1", "version-1", "1.0.0", "hash-1"),
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch,
            new Dictionary<string, string>(),
            new IncidentStrategyReference("Fault", "1"));

        var presentation = WorkflowExecutableActivityPresentationSidecar.CopyFrom(
            [
                new ActivityPresentationRecord("authored-root", "Friendly root", "Historical copy."),
                new ActivityPresentationRecord("authored-shared", "Friendly placement", "Repeated copy.")
            ],
            executable);

        Assert.Collection(
            presentation.OrderBy(record => record.ExecutableNodeId, StringComparer.Ordinal),
            record =>
            {
                Assert.Equal("exec-root", record.ExecutableNodeId);
                Assert.Equal("Friendly root", record.DisplayName);
                Assert.Equal("Historical copy.", record.Description);
            },
            record =>
            {
                Assert.Equal("exec-shared-1", record.ExecutableNodeId);
                Assert.Equal("Friendly placement", record.DisplayName);
                Assert.Equal("Repeated copy.", record.Description);
            },
            record =>
            {
                Assert.Equal("exec-shared-2", record.ExecutableNodeId);
                Assert.Equal("Friendly placement", record.DisplayName);
                Assert.Equal("Repeated copy.", record.Description);
            });
    }

    [Fact]
    public async Task ActivityPresentationIsFrozenPerReferenceAndDoesNotChangeArtifactIdentity()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var firstLayout = new WorkflowDefinitionVersionLayout
        {
            WorkflowDefinitionVersionId = workflowVersion.Id,
            ActivityPresentation =
            [
                new ActivityPresentationRecord(
                    "write-one",
                    "Notify buyer",
                    "Send the confirmation after payment.")
            ]
        };
        var secondLayout = new WorkflowDefinitionVersionLayout
        {
            WorkflowDefinitionVersionId = workflowVersion.Id,
            ActivityPresentation =
            [
                new ActivityPresentationRecord(
                    "write-one",
                    "Notify warehouse",
                    "Prepare the parcel.")
            ]
        };

        var first = await Handler(workflowVersion, firstLayout, _writeLineActivity)
            .Handle(new PublishWorkflow(workflowVersion.Id), CancellationToken.None);
        var second = await Handler(workflowVersion, secondLayout, _writeLineActivity)
            .Handle(new PublishWorkflow(
                workflowVersion.Id,
                PublicationAction.PublishSideBySide,
                SlotName: "warehouse"), CancellationToken.None);

        Assert.Equal(first.ArtifactId, second.ArtifactId);
        Assert.Equal(first.ArtifactHash, second.ArtifactHash);
        var references = await _referenceStore.ListAllByArtifactAsync(first.ArtifactId);
        Assert.Equal(2, references.Count);
        Assert.Contains(
            references,
            reference => Assert.Single(reference.ActivityPresentation).DisplayName == "Notify buyer");
        Assert.Contains(
            references,
            reference => Assert.Single(reference.ActivityPresentation).DisplayName == "Notify warehouse");
    }

    [Fact]
    public async Task CompiledInputBindingCarriesStableInputKeyAndSensitivity()
    {
        var input = new WorkflowArgumentState(
            "Text",
            new ArgumentValue("classified", "Literal"),
            null,
            null,
            null,
            true);

        var view = await Handler(WorkflowVersion(Node("write-one", input)))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        var executable = await _store.FindAsync(view.ArtifactId);
        var binding = Assert.Single(executable!.RootActivity.InputBindings).Value;
        Assert.Equal("Text", binding.InputKey);
        Assert.True(binding.EffectivePolicy.IsSensitive);
    }

    [Fact]
    public async Task PublishedExecutableArtifactCanBeDispatchedForExecution()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var published = await Handler(workflowVersion).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var dispatcher = new WorkflowStartDispatcher(
            _store,
            _referenceStore,
            new InProcessWorkflowExecutionActorProvider(),
            new ShortRuntimeExecutionIdGenerator());

        // The publish above appended a live Published source reference the dispatch gates on (ADR 0040).
        var result = await dispatcher.DispatchAsync(new WorkflowExecutionStartDispatchRequest(published.ArtifactId, "test"));

        Assert.Equal(published.ArtifactId, result.PinnedExecutable.ArtifactId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.CommandDispatch.Status);
        Assert.NotEmpty(result.WorkflowExecutionId);
    }

    [Fact]
    public async Task PublishesSequenceAuthoredStructureIntoExecutableChildSlotAndStructure()
    {
        var root = SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ]);
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion, _writeLineActivity, _sequenceActivity);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Equal("sequence", view.RootActivityId);
        Assert.Equal(3, view.NodeCount);
        var childSlot = Assert.Single(executable.RootActivity.ChildSlots);
        Assert.Equal(SequenceActivity.ActivitiesSlotName, childSlot.Name);
        Assert.Equal(["write-one", "write-two"], childSlot.Activities.Select(activity => activity.ExecutableNodeId));

        Assert.NotNull(executable.RootActivity.Structure);
        Assert.Equal(SequenceActivity.StructureKind, executable.RootActivity.Structure.Kind);
        Assert.Equal(["write-one", "write-two"], executable.RootActivity.Structure.Payload.GetProperty("activities").EnumerateArray().Select(item => item.GetString()));
    }

    [Fact]
    public async Task PublishesFlowchartAuthoredStructureIntoExecutableChildSlotAndRuntimeStructure()
    {
        var root = FlowchartNode(
            "flowchart",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ],
            [new FlowchartConnection(new FlowchartEndpoint("write-one", "Done"), new FlowchartEndpoint("write-two", null))],
            "write-one");
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion, _writeLineActivity, _flowchartActivity);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.NotNull(executable.RootActivity.Structure);
        Assert.Equal(FlowchartActivity.StructureKind, executable.RootActivity.Structure.Kind);
        Assert.Equal(FlowchartActivity.StructureSchemaVersion, executable.RootActivity.Structure.SchemaVersion);
        Assert.Equal("write-one", executable.RootActivity.Structure.Payload.GetProperty("startNodeId").GetString());
        Assert.False(executable.RootActivity.Structure.Payload.TryGetProperty("activities", out _));
        var childSlot = Assert.Single(executable.RootActivity.ChildSlots);
        Assert.Equal(FlowchartActivity.ActivitiesSlotName, childSlot.Name);
        Assert.Equal(["write-one", "write-two"], childSlot.Activities.Select(activity => activity.ExecutableNodeId));
    }

    [Fact]
    public async Task PublishesUnknownOpaqueStructureWithoutProjectingChildren()
    {
        var root = Node(
            "opaque",
            structure: new ActivityNodeStructure(
                UnknownStructureKind,
                UnknownStructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { marker = "kept" })),
            Text("opaque"));
        var workflowVersion = WorkflowVersion(root);
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        Assert.Empty(executable.RootActivity.ChildSlots);
        Assert.Equal(UnknownStructureKind, executable.RootActivity.Structure?.Kind);
        Assert.Equal("kept", executable.RootActivity.Structure?.Payload.GetProperty("marker").GetString());
    }

    [Fact]
    public async Task RejectsDeclaredStructureWhoseHandlerIsMissing()
    {
        // The sequence activity's catalog row declares its structure contract (design facet), but this
        // shell has no structure handler registered — the composition mismatch that previously let a
        // composite publish opaquely and fault deep in runtime execution.
        var root = SequenceNode("seq", [Node("write-one", Text("one"))]);
        var workflowVersion = WorkflowVersion(root);
        var versionStore = new FakeVersionStore(workflowVersion);
        var compiler = TestCompiler.Create(
            versionStore,
            new FakeActivityVersionStore([_writeLineActivity, _sequenceActivity]),
            new DefaultActivityStructureService([]),
            TestWellKnownTypeRegistry.Create());
        var handler = Handler(layout: null, compiler, versionStore);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(
            () => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains(SequenceActivity.StructureKind, exception.Message);
        Assert.Contains("no structure handler", exception.Message);
    }

    [Fact]
    public async Task RejectsWorkflowWithoutRootActivity()
    {
        var workflowVersion = WorkflowVersion(rootActivity: null);
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("root activity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishesNonLiteralInputAsExpressionBinding()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("\"Hello \" + \"World\"", "JavaScript"), null, null, null, null)));
        var handler = Handler(workflowVersion);

        var view = await handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var executable = await _store.FindAsync(view.ArtifactId);

        Assert.NotNull(executable);
        var binding = executable.NodesById["write-one"].InputBindings["Text"];
        Assert.Equal(RuntimeInputBindingSource.Expression, binding.Source);
        Assert.Equal("JavaScript", binding.Expression!.Language);
        Assert.Equal("\"Hello \" + \"World\"", binding.Expression.Expression);
    }

    [Fact]
    public async Task RejectsExpressionInputWithoutExpressionText()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", new WorkflowArgumentState("Text", new ArgumentValue("", "JavaScript"), null, null, null, null)));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("no expression text", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RejectsUnknownActivityVersionId()
    {
        var workflowVersion = WorkflowVersion(new ActivityNode("missing", "missing-activity", [Text("one")], []));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("version-1"), CancellationToken.None));

        Assert.Contains("missing-activity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Activity definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Workflow definition version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PropagatesTypedCompilationExceptionForUnknownVersionId()
    {
        // #397: publishing a VersionId the store cannot resolve used to bubble a raw ArgumentException from
        // version-source resolution (which ran before the compiler's guarded region) and the handler then
        // rewrapped every compilation failure into a bare ArgumentException, erasing the type. The handler now
        // lets the typed WorkflowExecutableCompilationException propagate untouched.
        var workflowVersion = WorkflowVersion(Node("write-one", Text("one")));
        var handler = Handler(workflowVersion);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => handler.Handle(new PublishWorkflow("missing-version"), CancellationToken.None));

        Assert.Contains("missing-version", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComputesDifferentArtifactIdWhenSequenceOrderChanges()
    {
        var firstWorkflowVersion = WorkflowVersion(SequenceNode(
            "sequence",
            [
                Node("write-one", Text("one")),
                Node("write-two", Text("two")),
                Node("write-three", Text("three"))
            ]));
        var secondWorkflowVersion = WorkflowVersion(SequenceNode(
            "sequence",
            [
                Node("write-three", Text("three")),
                Node("write-one", Text("one")),
                Node("write-two", Text("two"))
            ]));
        var firstView = await Handler(firstWorkflowVersion, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var secondView = await Handler(secondWorkflowVersion, _writeLineActivity, _sequenceActivity).Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.NotEqual(firstView.ArtifactId, secondView.ArtifactId);
        Assert.NotEqual(firstView.ArtifactHash, secondView.ArtifactHash);
    }

    [Fact]
    public async Task ComputesDifferentArtifactIdWhenLiteralInputTypeMetadataChanges()
    {
        var workflowVersion = WorkflowVersion(Node("write-one", Text("1")));
        var stringView = await Handler(workflowVersion, ActivityVersion("activity-write-line", "Text", new TypeReference("String")))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);
        var integerView = await Handler(workflowVersion, ActivityVersion("activity-write-line", "Text", new TypeReference("Int32")))
            .Handle(new PublishWorkflow("version-1"), CancellationToken.None);

        Assert.NotEqual(stringView.ArtifactId, integerView.ArtifactId);
        Assert.NotEqual(stringView.ArtifactHash, integerView.ArtifactHash);
    }

    private readonly InMemoryWorkflowExecutableSourceReferenceStore _referenceStore = new();

    private static PublishWorkflow NamedPublish(string versionId, string slotName)
    {
        var request = new PublishWorkflow(versionId);
        var slotProperty = typeof(PublishWorkflow).GetProperty("SlotName");
        var actionProperty = typeof(PublishWorkflow).GetProperty("Action");

        Assert.NotNull(slotProperty);
        Assert.NotNull(actionProperty);

        slotProperty.SetValue(request, slotName);
        var actionType = Nullable.GetUnderlyingType(actionProperty.PropertyType) ?? actionProperty.PropertyType;
        actionProperty.SetValue(request, Enum.Parse(actionType, "PublishSideBySide"));
        return request;
    }

    private async Task InvokeSlotLifecycleHandlerAsync(string operation, string definitionId, string slotName)
    {
        // The slot-lifecycle handlers stay in the Api assembly (spec 145); anchor on an Api type, since
        // PublishWorkflowRequestHandler itself has moved to the engine assembly.
        var assembly = typeof(WorkflowsPublishingApiFeature).Assembly;
        var handlerType = assembly.GetType($"Elsa.Workflows.Publishing.Api.Handlers.{operation}PublicationSlotRequestHandler");
        var requestType = assembly.GetType($"Elsa.Workflows.Publishing.Api.Requests.{operation}PublicationSlot");

        Assert.NotNull(handlerType);
        Assert.NotNull(requestType);

        var request = Activator.CreateInstance(requestType, definitionId, slotName);
        Assert.NotNull(request);

        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowExecutableStore>(_store);
        services.AddSingleton<IWorkflowExecutableSourceReferenceStore>(_referenceStore);
        services.AddSingleton<IWorkflowTriggerBindingStore>(_bindingStore);
        services.AddSingleton<IWorkflowActivationAuthority>(_activationAuthority);
        services.AddSingleton<IPublicationRecordStore>(_publicationStore);
        services.AddSingleton<IPublicationPolicyStore>(_policyStore);
        services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(TestRootWriteLeases.Create(_store));
        var extractor = new WorkflowTriggerBindingExtractor([]);
        services.AddSingleton<IWorkflowTriggerBindingExtractor>(extractor);
        services.AddSingleton<IWorkflowTriggerIndexer>(new WorkflowTriggerIndexer(extractor, _bindingStore));
        // spec 145: the publish engine (compiler, stores, the PublishWorkflow handler) moved to the
        // WorkflowsPublishing engine feature; compose it explicitly alongside the transport feature.
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);

        await using var provider = services.BuildServiceProvider();
        var handler = ActivatorUtilities.CreateInstance(provider, handlerType);
        var handleMethod = handlerType.GetMethod("Handle", [requestType, typeof(CancellationToken)]);
        Assert.NotNull(handleMethod);

        var invocation = handleMethod.Invoke(handler, [request, CancellationToken.None]);
        var task = Assert.IsAssignableFrom<Task>(invocation);
        await task;
    }

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion) =>
        Handler(workflowVersion, _writeLineActivity);

    private PublishWorkflowRequestHandler Handler(WorkflowDefinitionVersion workflowVersion, params ActivityDefinitionVersion[] activityVersions) =>
        Handler(workflowVersion, layout: null, activityVersions);

    private PublishWorkflowRequestHandler Handler(
        WorkflowDefinitionVersion workflowVersion,
        WorkflowDefinitionVersionLayout? layout,
        params ActivityDefinitionVersion[] activityVersions)
    {
        var versionStore = new FakeVersionStore(workflowVersion);
        var compiler = TestCompiler.Create(
            versionStore,
            new FakeActivityVersionStore(activityVersions.ToList()),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create());
        return Handler(layout, compiler, versionStore);
    }

    private (PublishWorkflowRequestHandler Handler, RecordingWorkflowExecutableCompiler Compiler) RecordingHandler(
        WorkflowDefinitionVersion workflowVersion)
    {
        var versionStore = new FakeVersionStore(workflowVersion);
        var compiler = new RecordingWorkflowExecutableCompiler(TestCompiler.Create(
            versionStore,
            new FakeActivityVersionStore([_writeLineActivity]),
            _activityStructureService,
            TestWellKnownTypeRegistry.Create()));
        return (Handler(layout: null, compiler, versionStore), compiler);
    }

    private PublishWorkflowRequestHandler Handler(
        WorkflowDefinitionVersionLayout? layout,
        IWorkflowExecutableCompiler compiler,
        IWorkflowDefinitionVersionStore versionStore,
        IExpressionDraftSemanticValidator? expressionValidator = null,
        bool configureExpressionValidator = true,
        ILogger<PublishWorkflowRequestHandler>? logger = null)
    {
        var extractor = new WorkflowTriggerBindingExtractor([]);
        return new(
            compiler,
            _store,
            _referenceStore,
            extractor,
            _bindingStore,
            new FakeLayoutStore(layout),
            _preflightAuthority ?? _activationAuthority,
            _policyStore,
            new PublicationPolicyResolver(),
            _publicationStore,
            _preflightService,
            WrapActivator(new PublicationActivator(Coordinator(extractor), _publicationStore, _activationAuthority, _referenceStore, TimeProvider.System)),
            TimeProvider.System,
            workflowVersionStore: versionStore,
            snapshotReviews: _snapshotReviews,
            authoredInputsSidecar: new WorkflowExecutableAuthoredInputsSidecar(new ActivityTreeProjector(_activityStructureService)),
            logger: logger,
            expressionValidator: configureExpressionValidator
                ? expressionValidator ?? StubExpressionValidator.Valid
                : null);
    }

    private WorkflowActivationCoordinator Coordinator(IWorkflowTriggerBindingExtractor extractor) =>
        new(
            _activationAuthority,
            new InMemoryWorkflowActivationSwitch(_activationAuthority, _referenceStore, TimeProvider.System, _bindingStore),
            _referenceStore,
            TestRootWriteLeases.Create(_store),
            TimeProvider.System,
            new WorkflowTriggerIndexer(extractor, _bindingStore),
            _bindingStore);

    /// <summary>Puts an activation from <paramref name="owner"/> into the empty default slot, as an artifact mount does.</summary>
    private async Task<WorkflowActivationSlot> OccupyDefaultSlotAsync(WorkflowActivationSource owner)
    {
        var transition = await _activationAuthority.TryActivateAsync(new WorkflowActivationSlotRequest(
            "definition-1", "default", "import:artifact-1", owner, ExpectedRevision: 0, DateTimeOffset.UtcNow));
        Assert.True(transition.Succeeded, transition.Diagnostic);
        return transition.Slot;
    }

    /// <summary>Hands the default slot, still serving <paramref name="published"/>'s artifact, to <paramref name="owner"/> through an operator takeover.</summary>
    private async Task<WorkflowActivationSlot> HandOverDefaultSlotAsync(PublishedWorkflowView published, WorkflowActivationSource owner)
    {
        var slot = await _activationAuthority.FindAsync("definition-1", "default");
        var result = await Coordinator(new WorkflowTriggerBindingExtractor([])).ActivateAsync(new WorkflowActivationCommand(
            (await _store.FindAsync(published.ArtifactId))!,
            (await _referenceStore.FindAsync(published.SourceReferenceId))!,
            "default",
            "import:artifact-1",
            owner,
            slot!.Revision,
            WorkflowActivationOwnershipIntent.TakeOver));
        Assert.True(result.Succeeded, result.Diagnostic);
        return result.Slot;
    }

    /// <summary>Counts each store publish writes to, retired rows included, so a refusal that wrote anything is visible.</summary>
    private async Task<PublishWrites> CountPublishWritesAsync() => new(
        (await _store.ListAllAsync()).Count,
        (await _referenceStore.ListAllAsync()).Count,
        (await _publicationStore.ListBySlotAsync(DefaultSlotId)).Count);

    private IPublicationActivator WrapActivator(IPublicationActivator activator) => _activatorWrapper?.Invoke(activator) ?? activator;

    private sealed record PublishWrites(int Executables, int SourceReferences, int PublicationRecords);

    /// <summary>Retires a source reference once the activation has answered, as a retirement between activation and the answer would.</summary>
    private sealed class RetiresServedReferenceAfterActivation(
        IPublicationActivator inner,
        IWorkflowExecutableSourceReferenceStore referenceStore,
        string sourceReferenceId) : IPublicationActivator
    {
        public async ValueTask<PublicationActivationResult> ActivateAsync(PublicationActivationRequest request, CancellationToken cancellationToken = default)
        {
            var result = await inner.ActivateAsync(request, cancellationToken);
            await referenceStore.RetireAsync(sourceReferenceId, DateTimeOffset.UtcNow, "test-retire", cancellationToken);
            return result;
        }

        public ValueTask<PublicationCompletionResult> CompleteAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            inner.CompleteAsync(workflowDefinitionId, slotName, cancellationToken);
    }

    /// <summary>The authority as it stood before any slot moved: every slot is empty.</summary>
    private sealed class SlotReadBeforeItMoved(IWorkflowActivationAuthority inner) : IWorkflowActivationAuthority
    {
        public ValueTask<WorkflowActivationSlot?> FindAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowActivationSlot?>(null);

        public ValueTask<IReadOnlyCollection<WorkflowActivationSlot>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyCollection<WorkflowActivationSlot>>([]);

        public ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
            inner.TryActivateAsync(request, cancellationToken);

        public ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
            string workflowDefinitionId,
            string slotName,
            WorkflowActivationSource source,
            long expectedRevision,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default) =>
            inner.TryDeactivateAsync(workflowDefinitionId, slotName, source, expectedRevision, updatedAt, cancellationToken);
    }

    /// <summary>Reports one authoritative Exclusive clash in another slot, whatever the candidate claims.</summary>
    private sealed class ClashingPreflightService : IPublicationPreflightService
    {
        public static readonly PublicationTriggerConflict Clash = new(
            "publication-blue", "blue", "Http", "get:/orders", PublicationTriggerCardinality.Exclusive, "claim-blue");

        public PublicationPreflightResult Evaluate(
            IReadOnlyCollection<PublicationTriggerClaim> candidateClaims,
            IReadOnlyCollection<PublicationAuthoritativeClaimSet> authoritativeClaims) =>
            new(CanActivate: false, [], [Clash]);
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

    /// <summary>Captures formatted log entries so the diagnostics-only logging can be asserted directly.</summary>
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class ThrowingExpressionValidator : IExpressionDraftSemanticValidator
    {
        public ValueTask<ExpressionDraftValidationResult> ValidateAsync(
            WorkflowDefinitionState state,
            string documentScope,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Validation provider failed.");
    }

    private static WorkflowPublicationPreflightPlan SnapshotPlan() =>
        new(
            new ResolvedPublicationAction(
                "definition-1",
                "snapshot",
                PublicationAction.Replace,
                "default",
                PublicationPolicySource.Host,
                PolicyRevision: 0),
            Slot: null,
            new PublicationPreflightResult(true, [], []),
            CandidateClaims: []);

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity) =>
        WorkflowVersion(rootActivity, "definition-1", "version-1", "1.0.0");

    private static WorkflowDefinitionVersion WorkflowVersion(ActivityNode? rootActivity, string definitionId, string versionId, string version) =>
        new(definitionId, version)
        {
            Id = versionId,
            Definition = new WorkflowDefinition { Id = definitionId, Name = "Demo" },
            State = new WorkflowDefinitionState([], rootActivity, [], [], null)
        };

    private static ActivityNode Node(string nodeId, params WorkflowArgumentState[] inputs) =>
        Node(nodeId, structure: null, inputs);

    private static ActivityNode Node(
        string nodeId,
        ActivityNodeStructure? structure,
        params WorkflowArgumentState[] inputs) =>
        new(
            nodeId,
            "activity-write-line",
            inputs,
            Outputs: [],
            Structure: structure);

    private static ActivityNode SequenceNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities) =>
        new(
            nodeId,
            "activity-sequence",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(activities))));

    private static ActivityNode FlowchartNode(
        string nodeId,
        IReadOnlyCollection<ActivityNode> activities,
        IReadOnlyCollection<FlowchartConnection> connections,
        string? startNodeId) =>
        new(
            nodeId,
            "activity-flowchart",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure(activities, connections, startNodeId))));

    private static WorkflowArgumentState Text(string value) =>
        new("Text", new ArgumentValue(value, "Literal"), null, null, null, null);

    /// <summary>Sends every publish request of the HTTP host to <paramref name="handler"/>.</summary>
    private sealed class HandlerSender(PublishWorkflowRequestHandler handler) : IRequestSender
    {
        public async Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) where T : notnull =>
            (T)(object)await handler.Handle((PublishWorkflow)(object)request, cancellationToken);
    }

    /// <summary>The write-line activity with its <c>Text</c> input declared a credential in the catalog.</summary>
    private static ActivityDefinitionVersion CredentialWriteLineActivity() =>
        ActivityVersion("activity-write-line", "Test.WriteLine",
            [new InputDefinition("Text", "Text", new TypeReference("String"), null, "Text", null, false) with { IsSensitive = true, IsCredential = true }]);

    private static ActivityDefinitionVersion ActivityVersion(string id, string inputName, TypeReference inputType) =>
        ActivityVersion(id, "Test.WriteLine", [new InputDefinition(inputName, inputName, inputType, null, inputName, null, false)]);

    private static ActivityDefinitionVersion ActivityVersion(string id, string activityTypeKey) =>
        ActivityVersion(id, activityTypeKey, []);

    private static ActivityDefinitionVersion ActivityVersion(string id, string activityTypeKey, IReadOnlyCollection<InputDefinition> inputs, params ActivityDesignFacet[] designFacets) =>
        new("1.0.0", "activity-definition-1")
        {
            Id = id,
            Definition = new ActivityDefinition
            {
                Id = "activity-definition-1",
                ActivityTypeKey = activityTypeKey,
                Category = "Test"
            },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TestActivityAliases.ForActivityVersionId(id))),
            Inputs = inputs,
            DesignFacets = designFacets
        };

    // Mirrors the structure facet ClrAssemblyScanner projects from an [ActivityStructure] declaration.
    private static ActivityDesignFacet StructureFacet(string kind, string schemaVersion) =>
        new(
            kind,
            schemaVersion,
            JsonSerializer.SerializeToElement(new
            {
                mode = "generic",
                supportsScopedVariables = false,
                slots = Array.Empty<object>(),
                initialPayload = new { }
            }));

    private static IActivityStructureService ActivityStructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesSequenceFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);

        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    private sealed class FakeLayoutStore(WorkflowDefinitionVersionLayout? layout) : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(layout);
    }

    private sealed class RecordingWorkflowExecutableCompiler(IWorkflowExecutableCompiler inner) : IWorkflowExecutableCompiler
    {
        public List<WorkflowExecutableCompileRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutable> CompileAsync(
            WorkflowExecutableCompileRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.CompileAsync(request, cancellationToken);
        }
    }
}
