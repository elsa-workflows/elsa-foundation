using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Runtime.CompilerServices;
using RouteAttribute = Elsa.Api.AspNetCore.RouteAttribute;

namespace Elsa.Api.Endpoints;

/// <summary>
/// Maps <see cref="ApiEndpointBase"/> classes onto a module's endpoint group.
/// </summary>
/// <remarks>
/// Scanning is module-local and happens inside the module's own explicit mapping call: the module
/// hands over its own assembly, the scan runs once per shell activation, and nothing found is stored
/// anywhere that outlives the endpoint generation. This is deliberately not process-global assembly
/// discovery, which is what ADR 0068 excludes.
/// </remarks>
public static class ApiEndpointMapper
{
    /// <summary>Maps every concrete <see cref="ApiEndpointBase"/> in the assembly onto the group.</summary>
    /// <param name="routePrefix">Prefix applied to attribute-declared routes, so attributes stay literal.</param>
    public static ModuleEndpointGroup MapEndpointsFrom(this ModuleEndpointGroup api, Assembly assembly, string? routePrefix = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var type in assembly.GetTypes()
                     .Where(type => !type.IsAbstract && typeof(ApiEndpointBase).IsAssignableFrom(type))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            MapEndpointCore(api, type, routePrefix);
        }

        return api;
    }

    /// <summary>Maps one endpoint class explicitly.</summary>
    public static IEndpointConventionBuilder MapEndpoint<TEndpoint>(this ModuleEndpointGroup api, string? routePrefix = null)
        where TEndpoint : ApiEndpointBase
    {
        ArgumentNullException.ThrowIfNull(api);
        return MapEndpointCore(api, typeof(TEndpoint), routePrefix);
    }

    private static IEndpointConventionBuilder MapEndpointCore(ModuleEndpointGroup api, Type type, string? routePrefix)
    {
        var options = new ApiEndpointOptions();
        var route = type.GetCustomAttribute<RouteAttribute>();
        if (route is not null)
        {
            options.Method = route.Method;
            options.Route = Prefix(routePrefix, route.Route);
        }

        // Configure runs on an uninitialized instance so an endpoint's constructor dependencies are
        // not needed at mapping time. It must therefore not touch instance state.
        var configureInstance = (ApiEndpointBase)RuntimeHelpers.GetUninitializedObject(type);
        configureInstance.Configure(options);

        if (options.Method is null || options.Route is null)
            throw new InvalidOperationException($"Endpoint '{type.FullName}' declares no route. Add a [Get]/[Post]/... attribute or set options.Method and options.Route in Configure.");
        if (string.IsNullOrWhiteSpace(options.Operation))
            throw new InvalidOperationException($"Endpoint '{type.FullName}' declares no operation identifier. Set options.Operation in Configure.");

        var (shape, request, response) = FindContract(type)
            ?? throw new InvalidOperationException($"Endpoint '{type.FullName}' does not derive from an ApiEndpoint base.");

        var builder = shape switch
        {
            EndpointShape.Unbound => (IEndpointConventionBuilder)MapUnboundMethod
                .MakeGenericMethod(response!)
                .Invoke(null, [api, type, options])!,
            EndpointShape.Writing => (IEndpointConventionBuilder)MapWritingMethod
                .MakeGenericMethod(request!, response!)
                .Invoke(null, [api, type, options])!,
            _ => (IEndpointConventionBuilder)MapMethod
                .MakeGenericMethod(request!, response ?? typeof(object))
                .Invoke(null, [api, type, options, response is null])!
        };

        foreach (var attribute in type.GetCustomAttributes().OfType<IEndpointConventionAttribute>())
            attribute.Apply(builder);
        foreach (var convention in options.Conventions)
            convention(builder);

        return builder;
    }

    private enum EndpointShape
    {
        Body,
        NoContent,
        Unbound,
        Writing
    }

    private static (EndpointShape shape, Type? request, Type? response)? FindContract(Type type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType)
                continue;
            var definition = current.GetGenericTypeDefinition();
            if (definition == typeof(ApiEndpoint<,>))
                return (EndpointShape.Body, current.GenericTypeArguments[0], current.GenericTypeArguments[1]);
            if (definition == typeof(ApiEndpoint<>))
                return (EndpointShape.NoContent, current.GenericTypeArguments[0], null);
            if (definition == typeof(ApiEndpointWithoutRequest<>))
                return (EndpointShape.Unbound, null, current.GenericTypeArguments[0]);
            if (definition == typeof(WritingApiEndpoint<,>))
                return (EndpointShape.Writing, current.GenericTypeArguments[0], current.GenericTypeArguments[1]);
        }

        return null;
    }

    private static readonly MethodInfo MapMethod =
        typeof(ApiEndpointMapper).GetMethod(nameof(MapTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo MapUnboundMethod =
        typeof(ApiEndpointMapper).GetMethod(nameof(MapUnboundTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo MapWritingMethod =
        typeof(ApiEndpointMapper).GetMethod(nameof(MapWritingTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static IEndpointConventionBuilder MapTyped<TRequest, TResponse>(
        ModuleEndpointGroup api, Type endpointType, ApiEndpointOptions options, bool noContent)
        where TResponse : notnull
    {
        // The factory is closure-captured, so it lives and dies with the endpoint generation.
        var factory = ActivatorUtilities.CreateFactory(endpointType, Type.EmptyTypes);

        if (noContent)
        {
            return api.MapNoContent<TRequest>(options, async (context, request, cancellationToken) =>
            {
                var endpoint = (ApiEndpoint<TRequest>)factory(context.RequestServices, null);
                endpoint.HttpContext = context;
                await endpoint.HandleAsync(request, cancellationToken);
            });
        }

        return api.MapBody<TRequest, TResponse>(options, async (context, request, cancellationToken) =>
        {
            var endpoint = (ApiEndpoint<TRequest, TResponse>)factory(context.RequestServices, null);
            endpoint.HttpContext = context;
            return await endpoint.HandleAsync(request, cancellationToken);
        });
    }

    private static IEndpointConventionBuilder MapUnboundTyped<TResponse>(
        ModuleEndpointGroup api, Type endpointType, ApiEndpointOptions options)
        where TResponse : notnull
    {
        var factory = ActivatorUtilities.CreateFactory(endpointType, Type.EmptyTypes);

        return api.MapUnboundBody<TResponse>(options, async (context, cancellationToken) =>
        {
            var endpoint = (ApiEndpointWithoutRequest<TResponse>)factory(context.RequestServices, null);
            endpoint.HttpContext = context;
            return await endpoint.HandleAsync(cancellationToken);
        });
    }

    private static IEndpointConventionBuilder MapWritingTyped<TRequest, TResponse>(
        ModuleEndpointGroup api, Type endpointType, ApiEndpointOptions options)
        where TResponse : notnull
    {
        var factory = ActivatorUtilities.CreateFactory(endpointType, Type.EmptyTypes);

        return api.MapWritingBody<TRequest>(options, typeof(TResponse), async (context, request, cancellationToken) =>
        {
            var endpoint = (WritingApiEndpoint<TRequest, TResponse>)factory(context.RequestServices, null);
            endpoint.HttpContext = context;
            endpoint.ResponseWriter = (response, statusCode) => api.WriteJsonAsync(context, response, statusCode);
            await endpoint.HandleAsync(request, cancellationToken);
        });
    }

    private static string Prefix(string? prefix, string route) =>
        string.IsNullOrEmpty(prefix) ? route : $"{prefix.TrimEnd('/')}/{route.TrimStart('/')}";
}
