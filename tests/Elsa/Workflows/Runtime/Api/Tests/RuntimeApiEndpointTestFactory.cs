using System.Reflection;
using System.Runtime.CompilerServices;
using Elsa.Workflows.Runtime.Api;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Api.Tests;

internal static class RuntimeApiEndpointTestFactory
{
    public static Type? FindType(string fullName) => typeof(WorkflowsRuntimeApiFeature).Assembly.GetType(fullName);

    public static BaseEndpoint FindByRoute(string route)
    {
        var endpoint = typeof(WorkflowsRuntimeApiFeature).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(BaseEndpoint).IsAssignableFrom(type))
            .Select(type => Create(type))
            .SingleOrDefault(candidate => candidate.Definition.Routes.Contains(route, StringComparer.Ordinal));
        Xunit.Assert.NotNull(endpoint);
        return endpoint;
    }

    public static (Type Request, Type Response) Contract(BaseEndpoint endpoint)
    {
        for (var current = endpoint.GetType().BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GenericTypeArguments.Length >= 2 &&
                current.GetGenericTypeDefinition().Namespace == "Elsa.Api.FastEndpoints.Abstractions")
                return (current.GenericTypeArguments[0], current.GenericTypeArguments[1]);
        }
        throw new InvalidOperationException($"Endpoint '{endpoint.GetType().FullName}' has no request/response contract.");
    }

    public static BaseEndpoint Create(Type endpointType, params object[] providedDependencies)
    {
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single().GetParameters().Select(parameter =>
                providedDependencies.SingleOrDefault(parameter.ParameterType.IsInstanceOfType)
                ?? Resolve(parameter.ParameterType)).ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var rest] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) && rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var endpoint = (BaseEndpoint)create.Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), dependencies])!;
        endpoint.Configure();
        return endpoint;
    }

    private static object Resolve(Type type)
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
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException();
    }
}
