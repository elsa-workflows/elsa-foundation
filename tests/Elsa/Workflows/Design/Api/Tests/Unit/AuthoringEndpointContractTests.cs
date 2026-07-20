using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Api.FastEndpoints.Constants;
using Elsa.Workflows.Design.Api;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// Canonical replacements for the route objectives still covered against Elsa.Server by the Modularity tests.
/// These remain RED until Workflow Design owns both context-sensitive authoring operations.
/// </summary>
public sealed class AuthoringEndpointContractTests
{
    [Theory]
    [InlineData("design/workflows/scoped-variables/analyze")]
    [InlineData("design/workflows/activities/{activityVersionId}/inputs/{inputName}/options")]
    public void Workflow_design_owns_the_canonical_authoring_route(string route)
    {
        var endpoint = FindEndpoint(route);

        Assert.Contains(PermissionNames.WorkflowDesignRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public void Scoped_variable_analysis_request_and_response_match_the_management_contract()
    {
        var (request, response) = EndpointContract("design/workflows/scoped-variables/analyze");

        AssertProperties(request, "State", "NodeId");
        AssertProperties(response, "VisibleVariables", "ShadowingWarnings");
    }

    [Fact]
    public void Contextual_input_options_bind_route_and_workflow_context_without_cacheable_output()
    {
        var endpoint = FindEndpoint("design/workflows/activities/{activityVersionId}/inputs/{inputName}/options");
        var (request, response) = EndpointContract(endpoint);

        AssertProperties(request, "ActivityVersionId", "InputName", "NodeId", "WorkflowState");
        AssertProperties(response, "Options");
    }

    private static (Type Request, Type Response) EndpointContract(string route) => EndpointContract(FindEndpoint(route));

    private static (Type Request, Type Response) EndpointContract(BaseEndpoint endpoint)
    {
        for (var current = endpoint.GetType().BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType || current.GenericTypeArguments.Length < 2)
                continue;
            var definition = current.GetGenericTypeDefinition();
            if (definition.Namespace == "Elsa.Api.FastEndpoints.Abstractions")
                return (current.GenericTypeArguments[0], current.GenericTypeArguments[1]);
        }

        throw new InvalidOperationException($"Endpoint '{endpoint.GetType().FullName}' does not declare a request/response contract.");
    }

    private static BaseEndpoint FindEndpoint(string route)
    {
        var endpoints = typeof(WorkflowsDesignApiFeature).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(BaseEndpoint).IsAssignableFrom(type))
            .Select(CreateEndpoint)
            .ToArray();
        var endpoint = endpoints.SingleOrDefault(candidate => candidate.Definition.Routes.Contains(route, StringComparer.Ordinal));
        Assert.NotNull(endpoint);
        return endpoint;
    }

    private static BaseEndpoint CreateEndpoint(Type endpointType)
    {
        var dependencies = endpointType
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single()
            .GetParameters()
            .Select(parameter => ResolveDependency(parameter.ParameterType))
            .ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var rest] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) &&
                              rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var endpoint = (BaseEndpoint)create.Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), dependencies])!;
        endpoint.Configure();
        return endpoint;
    }

    private static object ResolveDependency(Type type)
    {
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
        {
            var nullLogger = typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments[0]);
            return (nullLogger.GetProperty("Instance")?.GetValue(null) ?? nullLogger.GetField("Instance")?.GetValue(null))!;
        }
        if (type.IsInterface)
            return DispatchProxy.Create(type, typeof(NoopProxy));
        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static void AssertProperties(Type type, params string[] names) =>
        Assert.All(names, name => Assert.NotNull(type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)));

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A contract-only endpoint test must not invoke dependencies.");
    }
}
