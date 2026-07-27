using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Api.FastEndpoints.Constants;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests;

/// <summary>
/// RED contract for spec 092 FR-024 through FR-029. These tests intentionally describe the canonical
/// definition/draft/version lifecycle before T053-T056 add its endpoints and request models.
/// </summary>
public sealed class WorkflowDefinitionLifecycleContractTests
{
    private const string Root = "Elsa.Workflows.Design.Api.Endpoints";

    public static TheoryData<string, string, string> LifecycleEndpoints => new()
    {
        { "Definitions.List", "GET", "design/workflows/definitions" },
        { "Definitions.Add", "POST", "design/workflows/definitions" },
        { "Definitions.Get", "GET", "design/workflows/definitions/{definitionId}" },
        { "Definitions.UpdateMetadata", "PATCH", "design/workflows/definitions/{definitionId}" },
        { "Definitions.SoftDelete", "DELETE", "design/workflows/definitions/{definitionId}" },
        { "Definitions.Restore", "POST", "design/workflows/definitions/{definitionId}/restore" },
        { "Definitions.DeletePermanently", "DELETE", "design/workflows/definitions/{definitionId}/permanent" },
        { "Drafts.Get", "GET", "design/workflows/drafts/{draftId}" },
        { "Drafts.Replace", "PUT", "design/workflows/drafts/{draftId}" },
        { "Drafts.Discard", "DELETE", "design/workflows/drafts/{draftId}" },
        { "Drafts.PromotionPreflight", "POST", "design/workflows/drafts/{draftId}/promotion-preflight" },
        { "Drafts.Promote", "POST", "design/workflows/drafts/{draftId}/promote" },
        { "Versions.Get", "GET", "design/workflows/versions/{versionId}" }
    };

    [Theory]
    [MemberData(nameof(LifecycleEndpoints))]
    public void Canonical_lifecycle_operation_has_its_domain_route(
        string endpointName,
        string verb,
        string route)
    {
        var matches = typeof(AddDefinition).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && type.Namespace?.StartsWith(Root, StringComparison.Ordinal) == true
                           && typeof(BaseEndpoint).IsAssignableFrom(type))
            .Select(ConfiguredDefinition)
            .Where(definition => definition.Verbs.Contains(verb, StringComparer.OrdinalIgnoreCase)
                                 && definition.Routes.Contains(route, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            matches.Length == 1,
            $"Canonical operation '{endpointName}' requires exactly one {verb} {route} endpoint; found {matches.Length}.");
        Assert.Contains(
            verb == "GET" ? PermissionNames.WorkflowDesignRead : PermissionNames.WorkflowDesignManage,
            matches[0].AllowedPermissions!);
        Assert.Null(matches[0].AnonymousVerbs);
    }

    [Fact]
    public void Direct_version_ingestion_is_isolated_behind_explicit_manage_authorization()
    {
        var definition = typeof(AddDefinition).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                           && type.Namespace == $"{Root}.Versions"
                           && typeof(BaseEndpoint).IsAssignableFrom(type))
            .Select(ConfiguredDefinition)
            .Single(endpoint => endpoint.Verbs.Contains("POST", StringComparer.OrdinalIgnoreCase));

        Assert.Contains("design/workflows/versions/ingest", definition.Routes);
        Assert.Contains(PermissionNames.WorkflowDesignManage, definition.AllowedPermissions!);
        Assert.Null(definition.AnonymousVerbs);
    }

    [Fact]
    public void Definition_creation_accepts_authored_state_and_layout_without_a_concrete_root_kind()
    {
        var properties = typeof(AddDefinition).GetProperties().Select(property => property.Name).ToArray();

        Assert.Contains("InitialState", properties);
        Assert.Contains("Layout", properties);
        Assert.DoesNotContain("RootKind", properties);
        Assert.DoesNotContain("RootActivityVersionId", properties);
    }

    [Fact]
    public void Definition_details_expose_the_current_draft_as_a_first_class_resource()
    {
        var draftProperty = typeof(WorkflowDefinitionDetailsView).GetProperty(nameof(WorkflowDefinitionDetailsView.Draft));

        Assert.NotNull(draftProperty);
        Assert.Equal(typeof(WorkflowDraftView), draftProperty.PropertyType);
    }

    [Fact]
    public async Task Persisted_version_lookup_rejects_a_synthetic_draft_identifier_before_store_access()
    {
        var versions = new RecordingVersionStore();
        var handler = new GetVersionRequestHandler(versions, new NullVersionLayoutStore());

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            handler.Handle(new GetVersion("draft:editable-1"), CancellationToken.None));

        Assert.Equal(0, versions.ReadCount);
    }

    [Fact]
    public Task Promote_unknown_draft_maps_to_not_found() =>
        AssertCommandFailureStatusAsync(
            "Drafts.Promote",
            new PromoteDraft("promote-missing", "draft-missing"),
            EntityNotFoundException.ForEntity(typeof(WorkflowDefinitionDraft), "draft-missing"),
            StatusCodes.Status404NotFound);

    [Fact]
    public Task Promote_version_identity_conflict_maps_to_conflict() =>
        AssertCommandFailureStatusAsync(
            "Drafts.Promote",
            new PromoteDraft("promote-conflict", "draft-1", "2.0.0"),
            new WorkflowDefinitionVersionConflictException("definition-1", "2.0.0"),
            StatusCodes.Status409Conflict);

    [Fact]
    public async Task Promotion_preflight_reports_a_trimmed_forward_candidate_without_mutating_state()
    {
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-preflight",
            WorkflowDefinitionId = "definition-preflight",
            State = new Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState([], null, [], [], null)
        };
        var drafts = new PreflightDraftStore(draft);
        var versions = new PreflightVersionStore(new WorkflowDefinitionVersion(draft.WorkflowDefinitionId, "2.0.0"));
        var handler = new PreflightDraftPromotionRequestHandler(drafts, versions, new NoopInlineEventPublisher());

        var assessment = await handler.Handle(
            new PreflightDraftPromotion(draft.Id, " 2.1.0-rc.1 "),
            CancellationToken.None);

        Assert.True(assessment.IsReady);
        Assert.Equal("exact", assessment.AssignmentMode);
        Assert.Equal("2.1.0-rc.1", assessment.RequestedVersion);
        Assert.Equal("2.1.0-rc.1", assessment.ResolvedVersion);
        Assert.Equal(0, drafts.WriteCount);
        Assert.Equal(0, versions.WriteCount);
    }

    [Fact]
    public async Task Promotion_preflight_reports_an_existing_semantic_identity_without_reserving_it()
    {
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-preflight-conflict",
            WorkflowDefinitionId = "definition-preflight-conflict",
            State = new Elsa.Workflows.Design.Core.Models.WorkflowDefinitionState([], null, [], [], null)
        };
        var drafts = new PreflightDraftStore(draft);
        var versions = new PreflightVersionStore(
            new WorkflowDefinitionVersion(draft.WorkflowDefinitionId, "2.0.0"),
            identityExists: true);
        var handler = new PreflightDraftPromotionRequestHandler(drafts, versions, new NoopInlineEventPublisher());

        var assessment = await handler.Handle(
            new PreflightDraftPromotion(draft.Id, "2.0.0+build.7"),
            CancellationToken.None);

        Assert.False(assessment.IsReady);
        Assert.Equal("version-conflict", Assert.Single(assessment.Issues).Code);
        Assert.Equal(0, drafts.WriteCount);
        Assert.Equal(0, versions.WriteCount);
    }

    [Fact]
    public Task Permanent_delete_unknown_definition_maps_to_not_found() =>
        AssertCommandFailureStatusAsync(
            "Definitions.DeletePermanently",
            new DeleteDefinitionPermanently("delete-missing", "definition-missing"),
            EntityNotFoundException.ForEntity(typeof(WorkflowDefinition), "definition-missing"),
            StatusCodes.Status404NotFound);

    [Fact]
    public Task Permanent_delete_active_definition_maps_to_conflict() =>
        AssertCommandFailureStatusAsync(
            "Definitions.DeletePermanently",
            new DeleteDefinitionPermanently("delete-active", "definition-active"),
            new WorkflowDefinitionNotSoftDeletedException("definition-active"),
            StatusCodes.Status409Conflict);

    [Fact]
    public Task Unexpected_permanent_delete_failure_remains_an_internal_server_error() =>
        AssertCommandFailureStatusAsync(
            "Definitions.DeletePermanently",
            new DeleteDefinitionPermanently("delete-failed", "definition-active"),
            new InvalidOperationException("Unexpected provider failure."),
            StatusCodes.Status500InternalServerError);

    private static EndpointDefinition ConfiguredDefinition(Type endpointType)
    {
        var endpoint = CreateEndpoint(endpointType, ResolveDependency);
        endpoint.Configure();
        return endpoint.Definition;
    }

    private static async Task AssertCommandFailureStatusAsync(
        string relativeEndpointTypeName,
        object request,
        Exception failure,
        int expectedStatus)
    {
        var endpointType = typeof(AddDefinition).Assembly.GetType(
            $"{Root}.{relativeEndpointTypeName}",
            throwOnError: true)!;
        var sender = new ExceptionCommandSender(failure);
        var endpoint = CreateEndpoint(
            endpointType,
            type => type == typeof(ICommandSender) ? sender : ResolveDependency(type));
        var handle = endpoint.GetType().GetMethod(
            "HandleAsync",
            [request.GetType(), typeof(CancellationToken)])!;

        var exception = await Assert.ThrowsAsync<ValidationFailureException>(
            () => (Task)handle.Invoke(endpoint, [request, CancellationToken.None])!);

        Assert.Equal(expectedStatus, exception.StatusCode);
    }

    private static BaseEndpoint CreateEndpoint(Type endpointType, Func<Type, object> resolveDependency)
    {
        var dependencies = endpointType
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => resolveDependency(parameter.ParameterType))
            .ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(Action<DefaultHttpContext>)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        return (BaseEndpoint)create.Invoke(
            null,
            [(Action<DefaultHttpContext>)(context => context.Response.Body = new MemoryStream()), dependencies])!;
    }

    private static object ResolveDependency(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var nullLoggerType = typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments[0]);
            return (nullLoggerType.GetProperty(nameof(NullLogger<object>.Instance))?.GetValue(null)
                    ?? nullLoggerType.GetField(nameof(NullLogger<object>.Instance))?.GetValue(null))!;
        }
        if (type.IsInterface)
            return DispatchProxy.Create(type, typeof(NoopProxy));
        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private sealed class RecordingVersionStore : IWorkflowDefinitionVersionStore
    {
        public int ReadCount { get; private set; }

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => Read<WorkflowDefinitionVersion>();
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => Read<WorkflowDefinitionVersion?>();
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => Read<WorkflowDefinitionVersion>();
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => Read<WorkflowDefinitionVersion?>();
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Read<IReadOnlyList<WorkflowDefinitionVersion>>();
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) => Read<bool>();

        private Task<T> Read<T>()
        {
            ReadCount++;
            throw new InvalidOperationException("Synthetic draft ids must be rejected before persisted-version storage is queried.");
        }
    }

    private sealed class NullVersionLayoutStore : IWorkflowDefinitionVersionLayoutStore
    {
        public Task<WorkflowDefinitionVersionLayout?> FindByVersionIdAsync(string workflowDefinitionVersionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionVersionLayout?>(null);
    }

    private sealed class PreflightDraftStore(WorkflowDefinitionDraft draft) : IWorkflowDefinitionDraftStore
    {
        public int WriteCount { get; private set; }

        public Task<WorkflowDefinitionDraft?> FindByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(draftId == draft.Id ? draft : null);
        public Task<WorkflowDefinitionDraft?> FindByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkflowDefinitionDraft?>(workflowDefinitionId == draft.WorkflowDefinitionId ? draft : null);
        public Task<IReadOnlyList<WorkflowDefinitionDraft>> ListByWorkflowDefinitionIdAsync(string workflowDefinitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinitionDraft>>([]);
        public Task<IReadOnlyCollection<Elsa.Workflows.Design.Persistence.Core.Entities.DesignMetadataRecord>> FindLayoutByDraftIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Elsa.Workflows.Design.Persistence.Core.Entities.DesignMetadataRecord>>([]);
        public Task<Elsa.Workflows.Design.Persistence.Core.Models.DraftWithLayout?> FindWithLayoutByIdAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Elsa.Workflows.Design.Persistence.Core.Models.DraftWithLayout?>(null);
    }

    private sealed class PreflightVersionStore(
        WorkflowDefinitionVersion latest,
        bool identityExists = false) : IWorkflowDefinitionVersionStore
    {
        public int WriteCount { get; private set; }

        public Task<WorkflowDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) => Task.FromResult(latest);
        public Task<WorkflowDefinitionVersion?> FindByIdAsync(string versionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionVersion?>(latest);
        public Task<WorkflowDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) => Task.FromResult(latest);
        public Task<WorkflowDefinitionVersion?> FindLatestVersionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowDefinitionVersion?>(latest);
        public Task<IReadOnlyList<WorkflowDefinitionVersion>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowDefinitionVersion>>([latest]);
        public Task<bool> ExistsAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(identityExists);
    }

    private sealed class NoopInlineEventPublisher : Elsa.Events.Core.Contracts.IInlineEventPublisher
    {
        public Task Publish(Elsa.Events.Core.Contracts.IEvent @event, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ExceptionCommandSender(Exception exception) : ICommandSender
    {
        public Task<T> Send<T>(
            Elsa.Mediator.Core.Contracts.ICommand<T> command,
            CancellationToken cancellationToken = default)
            where T : notnull =>
            Task.FromException<T>(exception);

        public Task Send(
            Elsa.Mediator.Core.Contracts.ICommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromException(exception);
    }

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A configuration-only endpoint test must not invoke dependencies.");
    }
}
