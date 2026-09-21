using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NativeEndpoints;

namespace Elsa.Api.AspNetCore;

/// <summary>
/// The two mapping shapes Elsa's owners were written against, over the package's descriptor API.
/// </summary>
/// <remarks>
/// These forward rather than re-implement: <see cref="EndpointGroup.MapOperation{TMessage}"/> and
/// <see cref="EndpointGroup.MapRaw"/> do the work, and everything here does is carry the arguments
/// across. They exist because the package takes its settings as an object initializer while Elsa's
/// fifty-odd call sites pass them positionally, and rewriting those call sites would put a large
/// mechanical diff in front of a reviewer who needs to see that no operation's behaviour moved.
/// </remarks>
public static class ElsaEndpointGroupExtensions
{
    /// <summary>
    /// The low-level operation pipeline: bind, dispatch, translate failures, and attach the module
    /// operation metadata.
    /// </summary>
    public static IEndpointConventionBuilder MapOperation<TMessage>(
        this EndpointGroup api,
        string method,
        string pattern,
        string operation,
        EndpointBodyMode? bodyMode,
        string[]? accepts,
        Type? responseType,
        int successStatus,
        int? documentedStatus,
        Func<HttpContext, TMessage, CancellationToken, Task> dispatch,
        bool strictTypedParsing = false,
        string? name = null,
        bool documentAuthResponses = true)
    {
        ArgumentNullException.ThrowIfNull(api);
        return api.MapOperation(
            new EndpointOperationDescriptor
            {
                Method = method,
                Pattern = pattern,
                Operation = operation,
                Name = name,
                BodyMode = bodyMode,
                Accepts = accepts,
                ResponseType = responseType,
                SuccessStatus = successStatus,
                DocumentedStatus = documentedStatus,
                StrictTypedParsing = strictTypedParsing,
                DocumentAuthResponses = documentAuthResponses
            },
            dispatch,
            null);
    }

    /// <summary>
    /// Maps an operation with no bound request whose dispatch owns the entire response — the escape
    /// hatch for streaming and other non-JSON responses that still belong to the module convention.
    /// </summary>
    public static IEndpointConventionBuilder MapUnboundOperation(
        this EndpointGroup api,
        string method,
        string pattern,
        string operation,
        Type? responseType,
        int successStatus,
        int? documentedStatus,
        Func<HttpContext, Task> dispatch,
        string? name = null,
        bool documentAuthResponses = true,
        string? successContentType = null,
        bool containFailures = true)
    {
        ArgumentNullException.ThrowIfNull(api);
        return api.MapRaw(
            new ApiEndpointOptions
            {
                Method = method,
                Route = pattern,
                Operation = operation,
                Name = name,
                ResponseType = responseType,
                SuccessStatus = successStatus,
                DocumentedStatus = documentedStatus,
                SuccessContentType = successContentType,
                DocumentAuthResponses = documentAuthResponses,
                ContainFailures = containFailures
            },
            dispatch);
    }
}
