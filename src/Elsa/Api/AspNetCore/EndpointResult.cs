using Microsoft.AspNetCore.Http;

namespace Elsa.Api.AspNetCore;

/// <summary>
/// A response body paired with the status code it is written under.
/// </summary>
/// <remarks>
/// The return value of <see cref="ApiEndpointWithResult{TRequest,TResponse}.HandleAsync"/>. The
/// mapper unwraps it and writes <see cref="Response"/> with the module's serializer metadata, so the
/// struct never appears on the wire; the documented schema stays <typeparamref name="TResponse"/>.
/// </remarks>
public readonly record struct EndpointResult<TResponse>(int StatusCode, TResponse Response)
    where TResponse : notnull;

/// <summary>Factories for <see cref="EndpointResult{TResponse}"/>.</summary>
public static class EndpointResult
{
    /// <summary>The response written as 200 OK.</summary>
    public static EndpointResult<TResponse> Ok<TResponse>(TResponse response)
        where TResponse : notnull =>
        new(StatusCodes.Status200OK, response);

    /// <summary>The response written under the given status code.</summary>
    public static EndpointResult<TResponse> Status<TResponse>(int statusCode, TResponse response)
        where TResponse : notnull =>
        new(statusCode, response);
}
