using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Constants;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// Endpoint contracts for the authoring-schema surface consumed by headless clients:
/// the submit-body JSON Schema document and the composite-activity structure registry.
/// </summary>
public sealed class AuthoringSchemaEndpointContractTests
{
    [Theory]
    [InlineData("design/workflows/definitions/submit/schema")]
    [InlineData("design/workflows/structures")]
    public void Authoring_schema_route_is_a_read_only_design_endpoint(string route)
    {
        var endpoint = FindEndpoint(route);

        Assert.Equal(["GET"], endpoint.Definition.Verbs);
        Assert.Contains(
            ElsaEndpointPermissions.ComposePolicy([PermissionNames.WorkflowDesignRead]),
            endpoint.Definition.PreBuiltUserPolicies!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
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

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("A contract-only endpoint test must not invoke dependencies.");
    }
}
