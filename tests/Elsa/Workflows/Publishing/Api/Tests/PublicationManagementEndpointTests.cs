using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class PublicationManagementEndpointTests
{
    private const string Root = "Elsa.Workflows.Publishing.Api.Endpoints";

    public static TheoryData<string, string, string> ManagementEndpoints => new()
    {
        { "PreflightWorkflowPublicationEndpoint", "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/preflight", PermissionNames.WorkflowPublishingRead },
        { "PublishWorkflowEndpoint", "publishing/workflows/{versionId:regex(^(?!drafts$).+$)}/publish", PermissionNames.WorkflowPublishingManage },
        { "ListPublicationSlotsEndpoint", "publishing/workflows/{definitionId}/slots", PermissionNames.WorkflowPublishingRead },
        { "GetPublicationSlotEndpoint", "publishing/workflows/{definitionId}/slots/{slotName}", PermissionNames.WorkflowPublishingRead },
        { "UnpublishPublicationSlotEndpoint", "publishing/workflows/{definitionId}/slots/{slotName}", PermissionNames.WorkflowPublishingManage },
        { "RestorePublicationSlotEndpoint", "publishing/workflows/{definitionId}/slots/{slotName}/restore", PermissionNames.WorkflowPublishingManage },
        { "GetWorkflowPublicationPolicyEndpoint", "publishing/workflows/{definitionId}/policy", PermissionNames.WorkflowPublishingRead },
        { "SetWorkflowPublicationPolicyEndpoint", "publishing/workflows/{definitionId}/policy", PermissionNames.WorkflowPublishingManage }
    };

    [Theory]
    [MemberData(nameof(ManagementEndpoints))]
    public void Canonical_endpoint_has_pinned_route_and_action_scoped_permission(
        string endpointName,
        string route,
        string permission)
    {
        var definition = ConfiguredDefinition($"{Root}.{endpointName}");

        Assert.Contains(route, definition.Routes);
        Assert.Contains(PermissionNames.All, definition.AllowedPermissions!);
        Assert.Contains(permission, definition.AllowedPermissions!);
        Assert.Null(definition.AnonymousVerbs);
    }

    [Theory]
    [InlineData(PublicationPolicyDefaultActionView.Replace, "replace")]
    [InlineData(PublicationPolicyDefaultActionView.RequireExplicitSlot, "requireExplicitSlot")]
    public void Workflow_policy_uses_stable_public_action_names(
        PublicationPolicyDefaultActionView action,
        string expected)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new PublicationPolicyView("definition-1", action, "default", PublicationPolicySourceView.Workflow, 1, DateTimeOffset.UnixEpoch),
            options));

        Assert.Equal(expected, document.RootElement.GetProperty("defaultAction").GetString());
    }

    [Theory]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.Candidate, PublicationStatusView.Preparing)]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.PendingProjection, PublicationStatusView.Pending)]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.Active, PublicationStatusView.Active)]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.Retired, PublicationStatusView.Retired)]
    [InlineData(Elsa.Workflows.Publishing.Core.Models.PublicationStatus.Failed, PublicationStatusView.Failed)]
    public void Internal_publication_statuses_map_to_the_stable_management_contract(
        Elsa.Workflows.Publishing.Core.Models.PublicationStatus status,
        PublicationStatusView expected) =>
        Assert.Equal(expected, PublicationContract.ToView(status));

    [Fact]
    public void Trigger_preflight_maps_internal_claims_to_the_stable_client_shape()
    {
        var claim = new Elsa.Workflows.Publishing.Core.Models.PublicationTriggerClaim(
            "claim-1", "publication-1", "artifact-1", "node-1", "Http", "/bar",
            Elsa.Workflows.Publishing.Core.Models.PublicationTriggerCardinality.Exclusive,
            new Dictionary<string, string>());
        var change = PublicationTriggerChangeView.From(new Elsa.Workflows.Publishing.Core.Models.PublicationTriggerChange(
            "Http", "/bar", Elsa.Workflows.Publishing.Core.Models.PublicationTriggerChangeKind.Added, claim));

        Assert.Equal("http:/bar", change.Key);
        Assert.Equal(PublicationTriggerChangeKindView.Added, change.Change);
        Assert.Equal(PublicationTriggerCardinalityView.Exclusive, change.Cardinality);
    }

    [Theory]
    [InlineData(PublicationActionView.Replace, "replace")]
    [InlineData(PublicationActionView.SideBySide, "sideBySide")]
    public void Publication_intent_uses_stable_public_action_names(PublicationActionView action, string expected)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            new PublishWorkflowRequest("version-1", action),
            options));

        Assert.Equal(expected, document.RootElement.GetProperty("action").GetString());
    }

    private static EndpointDefinition ConfiguredDefinition(string endpointTypeName)
    {
        var endpointType = typeof(PublishWorkflow).Assembly.GetType(endpointTypeName, throwOnError: true)!;
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
        if (type == typeof(TimeProvider))
            return TimeProvider.System;
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

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A configuration-only endpoint test must not invoke dependencies.");
    }
}
