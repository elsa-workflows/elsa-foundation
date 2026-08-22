using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Elsa.Api.AspNetCore;

/// <summary>How an endpoint treats a JSON request body.</summary>
public enum EndpointBodyMode
{
    /// <summary>No body is read. Values come from route and query only.</summary>
    None,

    /// <summary>
    /// A JSON body is required. A content type that is present but not JSON is unsupported media;
    /// an absent content type still attempts the body, so an empty or malformed payload is reported
    /// as a bad request rather than as unsupported media.
    /// </summary>
    Required,

    /// <summary>
    /// As <see cref="Required"/>, but an absent content type is also unsupported media, and the
    /// rejection is a bare 415 status with no response body.
    /// </summary>
    /// <remarks>
    /// This reproduces a published contract in which the media-type check runs before any body is
    /// read and writes only a status code. It is not the default because every other operation
    /// reports an unsupported media type through the owner's problem shape.
    /// </remarks>
    RequiredWithContentType,

    /// <summary>A JSON body is read when present, and its absence binds from route and query instead.</summary>
    Optional
}

/// <summary>The reason a request could not be bound, mapped by the caller to a status code.</summary>
public enum EndpointBindingFailure
{
    UnsupportedMediaType,
    MissingBody,
    MalformedBody
}

/// <summary>The outcome of binding a request, either a value or a failure with a message.</summary>
public readonly record struct EndpointBindingResult<T>(T? Value, EndpointBindingFailure? Failure, string? Message)
{
    public bool Succeeded => Failure is null;
}

/// <summary>
/// Binds a request record from route values, the query string, and a JSON body.
/// </summary>
/// <remarks>
/// This is deliberately small. It exists because module endpoints publish bare
/// <see cref="RequestDelegate"/> handlers rather than typed lambdas: a typed lambda makes
/// RequestDelegateFactory publish the handler's own <see cref="MethodInfo"/> into endpoint metadata,
/// which API Explorer then retains for the host service-provider lifetime and which the endpoint
/// ownership guards reject. Binding therefore cannot be delegated to the framework here, and this
/// type covers exactly the shapes first-party endpoints use.
/// <para>
/// Precedence is route, then body, then query, then the parameter's own default. Route wins over the
/// body so a route-addressed resource identifier cannot be contradicted by the payload.
/// </para>
/// </remarks>
public static class EndpointRequestBinder
{
    private static readonly ConcurrentDictionary<Type, ConstructorInfo> Constructors = new();

    public static async ValueTask<EndpointBindingResult<T>> BindAsync<T>(
        HttpContext context,
        JsonSerializerOptions jsonOptions,
        EndpointBodyMode bodyMode)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(jsonOptions);

        object? body = null;
        if (bodyMode is not EndpointBodyMode.None)
        {
            var declared = !string.IsNullOrWhiteSpace(context.Request.ContentType);
            var isJson = declared && IsJsonContentType(context.Request.ContentType);
            var unsupported = bodyMode switch
            {
                // An optional body simply is not there when it is not JSON.
                EndpointBodyMode.Optional => false,
                EndpointBodyMode.RequiredWithContentType => !isJson,
                _ => declared && !isJson
            };

            if (unsupported)
            {
                return new(default, EndpointBindingFailure.UnsupportedMediaType,
                    "The request content type must be application/json.");
            }

            if (bodyMode is EndpointBodyMode.Optional && !isJson)
            {
                // Falls through to route and query binding.
            }
            else
            {
                try
                {
                    body = await JsonSerializer.DeserializeAsync(
                        context.Request.Body, typeof(T), jsonOptions, context.RequestAborted);
                }
                catch (JsonException exception)
                {
                    var message = exception.Message.Replace(" Path: $ |", "", StringComparison.Ordinal);
                    return new(default, EndpointBindingFailure.MalformedBody, message);
                }

                if (body is null && bodyMode is EndpointBodyMode.Required)
                    return new(default, EndpointBindingFailure.MissingBody, "A request body is required.");
            }
        }

        var constructor = Constructors.GetOrAdd(typeof(T), SelectConstructor);
        var parameters = constructor.GetParameters();

        // A contract declared with init-only properties rather than positional parameters is bound by
        // assignment: the deserialized body is kept and route values are applied over it.
        if (parameters.Length == 0)
            return new(BindProperties<T>(body, context), null, null);
        var arguments = new object?[parameters.Length];
        var routeValues = context.Request.RouteValues;
        var query = context.Request.Query;

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index];
            var name = parameter.Name!;

            if (TryGetRouteValue(routeValues, name, out var routeValue))
            {
                arguments[index] = Convert(routeValue, parameter.ParameterType, name);
                continue;
            }

            if (body is not null)
            {
                arguments[index] = ReadProperty(body, name, parameter.ParameterType);
                continue;
            }

            if (TryGetQueryValue(query, name, out var queryValue))
            {
                arguments[index] = Convert(queryValue, parameter.ParameterType, name);
                continue;
            }

            arguments[index] = parameter.HasDefaultValue
                ? parameter.DefaultValue
                : DefaultOf(parameter.ParameterType);
        }

        return new((T)constructor.Invoke(arguments), null, null);
    }

    private static T BindProperties<T>(object? body, HttpContext context)
    {
        var instance = body ?? Activator.CreateInstance(typeof(T))!;
        var routeValues = context.Request.RouteValues;
        var query = context.Request.Query;

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite)
                continue;

            if (TryGetRouteValue(routeValues, property.Name, out var routeValue))
                property.SetValue(instance, Convert(routeValue, property.PropertyType, property.Name));
            else if (body is null && TryGetQueryValue(query, property.Name, out var queryValue))
                property.SetValue(instance, Convert(queryValue, property.PropertyType, property.Name));
        }

        return (T)instance;
    }

    private static ConstructorInfo SelectConstructor(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length != 1)
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' must declare exactly one public constructor to be bound from a request; found {constructors.Length}.");
        }

        return constructors[0];
    }

    private static object? ReadProperty(object source, string name, Type targetType)
    {
        var property = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property is null ? DefaultOf(targetType) : property.GetValue(source);
    }

    private static bool TryGetRouteValue(RouteValueDictionary routeValues, string name, out string? value)
    {
        foreach (var entry in routeValues)
        {
            if (!string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = entry.Value?.ToString();
            return value is not null;
        }

        value = null;
        return false;
    }

    private static bool TryGetQueryValue(IQueryCollection query, string name, out string? value)
    {
        foreach (var entry in query)
        {
            if (!string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                continue;
            value = entry.Value.ToString();
            return true;
        }

        value = null;
        return false;
    }

    private static object? Convert(string? value, Type targetType, string parameterName)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is null)
            return DefaultOf(targetType);

        if (underlying == typeof(string))
            return value;

        // A blank query value for a nullable parameter means "absent", matching the previous
        // per-module helpers, which returned null rather than failing.
        if (value.Length == 0)
            return DefaultOf(targetType);

        if (underlying == typeof(bool))
            return bool.TryParse(value, out var boolean) ? boolean : DefaultOf(targetType);
        if (underlying == typeof(int))
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : DefaultOf(targetType);
        if (underlying == typeof(long))
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue) ? longValue : DefaultOf(targetType);
        if (underlying == typeof(Guid))
            return Guid.TryParse(value, out var guid) ? guid : DefaultOf(targetType);
        if (underlying.IsEnum)
            return Enum.TryParse(underlying, value, ignoreCase: true, out var parsed) ? parsed : DefaultOf(targetType);
        if (underlying == typeof(DateTimeOffset))
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, out var instant) ? instant : DefaultOf(targetType);

        throw new InvalidOperationException(
            $"Request parameter '{parameterName}' has unsupported type '{targetType.FullName}'. " +
            "Extend EndpointRequestBinder deliberately rather than widening it implicitly.");
    }

    private static object? DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

    private static bool IsJsonContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) &&
        string.Equals(contentType.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase);
}
