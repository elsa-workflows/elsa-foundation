using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Persistence.Core.Entities;
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

    private static EndpointDefinition ConfiguredDefinition(Type endpointType)
    {
        var dependencies = endpointType
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => ResolveDependency(parameter.ParameterType))
            .ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create)
                              && method.IsGenericMethodDefinition
                              && method.GetParameters() is [var first, var rest]
                              && first.ParameterType == typeof(Action<DefaultHttpContext>)
                              && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var endpoint = (BaseEndpoint)create.Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), dependencies])!;
        endpoint.Configure();
        return endpoint.Definition;
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

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A configuration-only endpoint test must not invoke dependencies.");
    }
}
