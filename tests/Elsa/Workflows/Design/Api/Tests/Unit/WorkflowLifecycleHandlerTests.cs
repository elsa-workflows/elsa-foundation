using Elsa.Events.Core.Contracts;
using Elsa.Mediator.Core.Contracts;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Validations.Core.Events;
using Elsa.Workflows.Design.Validations.Core.Models;
using System.Reflection;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Drafts;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.SoftDelete;
using SoftDeleteHandler = Elsa.Workflows.Design.Api.Endpoints.Definitions.SoftDelete.Handler;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.DeletePermanently;
using DeletePermanentlyHandler = Elsa.Workflows.Design.Api.Endpoints.Definitions.DeletePermanently.Handler;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Restore;
using RestoreHandler = Elsa.Workflows.Design.Api.Endpoints.Definitions.Restore.Handler;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class WorkflowLifecycleHandlerTests
{
    [Fact]
    public void Workflow_draft_view_retains_its_public_factory_member()
    {
        var method = typeof(WorkflowDraftView).GetMethod(nameof(WorkflowDraftView.From), BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(typeof(WorkflowDraftView), method.ReturnType);
        Assert.Equal(
            [typeof(WorkflowDefinitionDraft), typeof(IEnumerable<DesignMetadataRecord>), typeof(IEnumerable<ActivityPresentationRecord>)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task Draft_get_and_replace_use_the_first_class_draft_id_and_preserve_omitted_layout()
    {
        var layout = new[] { new DesignMetadataRecord("node-1", 10, 20) };
        var drafts = new MutableDraftStore(new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "definition-1",
            State = WorkflowDefinitionState.Empty
        }, layout);
        var update = new RecordingUpdateDraftCommand(drafts);
        var desired = new WorkflowDefinitionStateView(
            RootActivity: new ActivityNode("root", "activity-version-1", [], []));

        var replaced = await new ReplaceDraftCommandHandler(drafts, update).Handle(
            new ReplaceDraft("replace-1", "draft-1", desired, Layout: null),
            CancellationToken.None);
        var read = await new GetDraftRequestHandler(drafts).Handle(new GetDraft("draft-1"), CancellationToken.None);

        Assert.Equal("draft-1", update.Request!.DraftId);
        Assert.Equal(layout, update.Request.Layout);
        Assert.Equal("root", replaced.State.RootActivity!.NodeId);
        Assert.Equal("root", read.State.RootActivity!.NodeId);
        Assert.Equal("node-1", Assert.Single(read.Layout).NodeId);
        Assert.Equal(new DesignOperationKey("replace-1"), update.OperationKey);
    }

    [Fact]
    public async Task Definition_must_pass_through_soft_delete_before_permanent_delete_and_restore_clears_lifecycle_facts()
    {
        var definition = new WorkflowDefinition { Id = "definition-1", Name = "Demo" };
        var definitions = new MutableDefinitionStore(definition);
        var save = new RecordingSaveDefinitionCommand();
        var permanent = new RecordingPermanentDeleteCommand(definitions);
        var permanentHandler = new DeletePermanentlyHandler(permanent);

        await Assert.ThrowsAsync<InvalidOperationException>(() => permanentHandler.Handle(
            new DeleteDefinitionPermanently("delete-before-soft-delete", definition.Id),
            CancellationToken.None));

        await new SoftDeleteHandler(definitions, save, new FixedTimeProvider()).Handle(
            new SoftDeleteDefinition("soft-delete-1", definition.Id, "cleanup"),
            CancellationToken.None);
        Assert.Equal(DateTimeOffset.UnixEpoch, definition.DeletedAt);
        Assert.Equal("cleanup", definition.DeletedReason);

        await new RestoreHandler(definitions, save).Handle(
            new RestoreDefinition("restore-1", definition.Id),
            CancellationToken.None);
        Assert.Null(definition.DeletedAt);
        Assert.Null(definition.DeletedReason);

        await new SoftDeleteHandler(definitions, save, new FixedTimeProvider()).Handle(
            new SoftDeleteDefinition("soft-delete-2", definition.Id),
            CancellationToken.None);
        await permanentHandler.Handle(
            new DeleteDefinitionPermanently("permanent-delete-1", definition.Id),
            CancellationToken.None);
        Assert.Equal(definition.Id, permanent.DefinitionId);
        Assert.Equal(new DesignOperationKey("permanent-delete-1"), permanent.OperationKey);
    }

    [Fact]
    public async Task Promotion_handler_preserves_direct_command_failures()
    {
        var failures = new Exception[]
        {
            EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), "draft-1"),
            new WorkflowDefinitionVersionConflictException("definition-1", "1.0.0"),
            new WorkflowPromotionOperationConflictException("promotion retry conflict")
        };

        foreach (var failure in failures)
        {
            var actual = await Record.ExceptionAsync(() => new PromoteDraftCommandHandler(
                    new ThrowingPromoteDraftCommand(failure),
                    new NeverRequestSender()).Handle(
                    new PromoteDraft("operation-1", "draft-1", "1.0.0"),
                    CancellationToken.None));

            Assert.Same(failure, actual);
        }
    }

    [Fact]
    public async Task Permanent_delete_handler_preserves_direct_command_failures()
    {
        var failures = new Exception[]
        {
            EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), "definition-1"),
            new WorkflowDefinitionNotSoftDeletedException("definition-1"),
            new PermanentDeletionUnavailableException("definition-1"),
            new InvalidOperationException("permanent-delete failed")
        };

        foreach (var failure in failures)
        {
            var actual = await Record.ExceptionAsync(() => new DeletePermanentlyHandler(
                    new ThrowingPermanentDeleteCommand(failure)).Handle(
                    new DeleteDefinitionPermanently("operation-1", "definition-1"),
                    CancellationToken.None));

            Assert.Same(failure, actual);
        }
    }

    [Fact]
    public async Task Promotion_preflight_reports_validation_and_identity_conflicts_without_mutating_the_draft_or_store()
    {
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "definition-1",
            State = WorkflowDefinitionState.Empty,
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var drafts = new MutableDraftStore(draft, []);
        var versions = new PreflightVersionStore(new WorkflowDefinitionVersion("definition-1", "2.0.0"), identityExists: true);
        var publisher = new RecordingInlineEventPublisher(new ValidationError("$workflow", "Draft/Invalid", "draft is invalid"));
        var result = await new PreflightDraftPromotionRequestHandler(drafts, versions, publisher).Handle(
            new PreflightDraftPromotion("draft-1", "2.0.0+build.7"),
            CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Contains(result.Issues, issue => issue.Code == "version-conflict");
        Assert.Contains(result.Issues, issue => issue.Code == "draft-validation" && issue.Path == "$workflow");
        Assert.Equal(1, publisher.PublishCalls);
        Assert.Equal(1, versions.LatestCalls);
        Assert.Equal(1, versions.IdentityCalls);
        Assert.Equal(WorkflowDefinitionState.Empty, draft.State);
        Assert.False(versions.WasMutated);
    }

    [Fact]
    public async Task Synthetic_draft_id_is_used_as_the_store_key_and_does_not_fall_back_to_definition_lookup()
    {
        var draft = new WorkflowDefinitionDraft
        {
            Id = "persisted-draft",
            WorkflowDefinitionId = "definition-1",
            State = WorkflowDefinitionState.Empty
        };
        var drafts = new MutableDraftStore(draft, []);
        var versions = new PreflightVersionStore(new WorkflowDefinitionVersion("definition-1", "1.0.0"), identityExists: false);
        var publisher = new RecordingInlineEventPublisher(null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => new PreflightDraftPromotionRequestHandler(drafts, versions, publisher).Handle(
            new PreflightDraftPromotion("synthetic-draft", "1.0.0"), CancellationToken.None));

        Assert.Equal("synthetic-draft", drafts.LastRequestedId);
        Assert.Equal(0, versions.LatestCalls);
        Assert.Equal(0, versions.IdentityCalls);
        Assert.Equal(0, publisher.PublishCalls);
    }

    [Fact]
    public async Task Promotion_preflight_reports_a_ready_exact_version_without_mutating_anything()
    {
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "definition-1",
            State = WorkflowDefinitionState.Empty,
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var drafts = new MutableDraftStore(draft, []);
        var versions = new PreflightVersionStore(new WorkflowDefinitionVersion("definition-1", "1.0.0"), identityExists: false);
        var publisher = new RecordingInlineEventPublisher(null);

        var result = await new PreflightDraftPromotionRequestHandler(drafts, versions, publisher).Handle(
            new PreflightDraftPromotion("draft-1", "2.0.0"),
            CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.Equal("exact", result.AssignmentMode);
        Assert.Equal("2.0.0", result.RequestedVersion);
        Assert.Equal("2.0.0", result.ResolvedVersion);
        Assert.Equal("1.0.0", result.LatestVersion);
        Assert.Empty(result.Issues);
        Assert.Equal(1, publisher.PublishCalls);
        Assert.Equal(1, versions.LatestCalls);
        Assert.Equal(1, versions.IdentityCalls);
        Assert.False(versions.WasMutated);
        Assert.Equal(WorkflowDefinitionState.Empty, draft.State);
    }

    [Fact]
    public async Task Promotion_preflight_trims_a_forward_exact_version_without_mutating_the_draft()
    {
        var (handler, draft, versions, publisher) = CreatePreflight("2.0.0", identityExists: false);

        var result = await handler.Handle(new PreflightDraftPromotion("draft-1", " 3.0.0 "), CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.Equal("exact", result.AssignmentMode);
        Assert.Equal("3.0.0", result.RequestedVersion);
        Assert.Equal("3.0.0", result.ResolvedVersion);
        Assert.Empty(result.Issues);
        Assert.Equal(WorkflowDefinitionState.Empty, draft.State);
        Assert.False(versions.WasMutated);
        Assert.Equal(1, publisher.PublishCalls);
    }

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("1.0.0")]
    public async Task Promotion_preflight_rejects_exact_latest_and_non_forward_versions_without_mutation(string requestedVersion)
    {
        var (handler, draft, versions, publisher) = CreatePreflight("2.0.0", identityExists: false);

        var result = await handler.Handle(new PreflightDraftPromotion("draft-1", requestedVersion), CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Contains(result.Issues, issue => issue.Code == "not-forward");
        Assert.Equal(WorkflowDefinitionState.Empty, draft.State);
        Assert.False(versions.WasMutated);
        Assert.Equal(1, publisher.PublishCalls);
    }

    private static (PreflightDraftPromotionRequestHandler Handler, WorkflowDefinitionDraft Draft, PreflightVersionStore Versions, RecordingInlineEventPublisher Publisher) CreatePreflight(string latestVersion, bool identityExists)
    {
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-1",
            WorkflowDefinitionId = "definition-1",
            State = WorkflowDefinitionState.Empty,
            LastModifiedAt = DateTimeOffset.UnixEpoch
        };
        var versions = new PreflightVersionStore(new WorkflowDefinitionVersion("definition-1", latestVersion), identityExists);
        var publisher = new RecordingInlineEventPublisher(null);
        return (new PreflightDraftPromotionRequestHandler(new MutableDraftStore(draft, []), versions, publisher), draft, versions, publisher);
    }

    private sealed class MutableDraftStore(WorkflowDefinitionDraft draft, IReadOnlyCollection<DesignMetadataRecord> layout)
        : IWorkflowDefinitionDraftStore
    {
        public WorkflowDefinitionDraft Draft { get; } = draft;
        public IReadOnlyCollection<DesignMetadataRecord> Layout { get; set; } = layout;
        public string? LastRequestedId { get; private set; }
        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default)
        {
            LastRequestedId = draftId;
            return Task.FromResult<WorkflowDefinitionDraft?>(draftId == Draft.Id ? Draft : null);
        }
        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionDraft?>(Draft);
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>([Draft]);
        public Task<IReadOnlyCollection<DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) => Task.FromResult(Layout);
        public Task<DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default)
        {
            LastRequestedId = draftId;
            return Task.FromResult<DraftWithLayout?>(draftId == Draft.Id ? new(Draft, Layout) : null);
        }
    }

    private sealed class RecordingUpdateDraftCommand(MutableDraftStore store) : IUpdateDraftCommand
    {
        public UpdateDraftRequest? Request { get; private set; }
        public DesignOperationKey? OperationKey { get; private set; }

        public Task Execute(
            DesignOperationKey operationKey,
            UpdateDraftRequest request,
            CancellationToken cancellationToken = default)
        {
            OperationKey = operationKey;
            Request = request;
            store.Draft.State = request.State;
            store.Layout = request.Layout;
            return Task.CompletedTask;
        }
    }

    private sealed class MutableDefinitionStore(WorkflowDefinition definition) : IWorkflowDefinitionStore
    {
        public Task<WorkflowDefinition> GetAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(definition);
        public Task<WorkflowDefinition?> FindByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinition?>(definition);
        public Task<IReadOnlyList<WorkflowDefinition>> ListAsync(WorkflowDefinitionFilter filter, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowDefinition>>([definition]);
    }

    private sealed class RecordingSaveDefinitionCommand : ISaveWorkflowDefinitionCommand
    {
        public Task Execute(
            DesignOperationKey operationKey,
            WorkflowDefinition definition,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingPermanentDeleteCommand(MutableDefinitionStore definitions)
        : IDeleteWorkflowDefinitionPermanentlyCommand
    {
        public string? DefinitionId { get; private set; }
        public DesignOperationKey? OperationKey { get; private set; }

        public async Task Execute(
            DesignOperationKey operationKey,
            string definitionId,
            CancellationToken cancellationToken = default)
        {
            var definition = await definitions.GetAsync(definitionId, cancellationToken);
            if (definition.DeletedAt is null)
                throw new InvalidOperationException(
                    "A workflow definition must be soft-deleted before permanent deletion.");

            OperationKey = operationKey;
            DefinitionId = definitionId;
        }
    }

    private sealed class ThrowingPromoteDraftCommand(Exception failure) : IPromoteDraftToVersionCommand
    {
        public Task<string> Execute(
            DesignOperationKey operationKey,
            string draftId,
            CancellationToken cancellationToken = default) => Task.FromException<string>(failure);

        public Task<string> Execute(
            DesignOperationKey operationKey,
            string draftId,
            string? requestedVersion,
            CancellationToken cancellationToken = default) => Task.FromException<string>(failure);
    }

    private sealed class ThrowingPermanentDeleteCommand(Exception failure) : IDeleteWorkflowDefinitionPermanentlyCommand
    {
        public Task Execute(
            DesignOperationKey operationKey,
            string definitionId,
            CancellationToken cancellationToken = default) => Task.FromException(failure);
    }

    private sealed class NeverRequestSender : IRequestSender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default)
            where T : notnull => throw new InvalidOperationException("The request sender must not be invoked after a command failure.");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch;
    }

    private sealed class PreflightVersionStore(WorkflowDefinitionVersion latest, bool identityExists) : IWorkflowDefinitionVersionStore
    {
        public int LatestCalls { get; private set; }
        public int IdentityCalls { get; private set; }
        public bool WasMutated { get; private set; }

        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default)
        {
            LatestCalls++;
            return Task.FromResult<WorkflowDefinitionVersion?>(latest);
        }

        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default)
        {
            IdentityCalls++;
            return Task.FromResult(identityExists);
        }

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingInlineEventPublisher(ValidationError? error) : IInlineEventPublisher
    {
        public int PublishCalls { get; private set; }

        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            PublishCalls++;
            if (error is not null && @event is DraftValidating validating)
                validating.Errors.Add(error);
            return Task.CompletedTask;
        }
    }
}
