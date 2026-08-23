using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Api.Endpoints;

/// <summary>
/// Maps a module's routes onto the mediator requests and commands it already defines.
/// </summary>
/// <remarks>
/// This is a mapping convention, not an endpoint framework. There is no discovery, no process-global
/// registry, and no request or handler base type: a handler is an ordinary
/// <see cref="IRequestHandler{TRequest,TResponse}"/>, and every route is registered by an explicit
/// call. Each Map method returns the standard <see cref="IEndpointConventionBuilder"/>, so
/// authorization, filters, metadata, and any other ASP.NET Core convention compose on top as usual.
/// Permissions are applied by the caller with <c>RequirePermission</c>, keeping Foundation Identity
/// out of this project's dependencies.
/// <para>
/// Handlers are published as bare <see cref="RequestDelegate"/> instances on purpose. Passing a typed
/// lambda to <c>MapGet</c> would make RequestDelegateFactory publish the handler's own
/// <see cref="System.Reflection.MethodInfo"/> and async state machine into endpoint metadata, which
/// API Explorer retains for the host service-provider lifetime.
/// </para>
/// </remarks>
public sealed class ModuleEndpointGroup
{
    private readonly IEndpointRouteBuilder _endpoints;
    private const string JsonContentType = "application/json; charset=utf-8";
    private readonly JsonSerializerContext _jsonContext;

    internal ModuleEndpointGroup(IEndpointRouteBuilder endpoints, string ownerId, JsonSerializerContext jsonContext)
    {
        _endpoints = endpoints;
        OwnerId = ownerId;
        _jsonContext = jsonContext;
    }

    /// <summary>The stable owning module identifier applied to every endpoint in the group.</summary>
    public string OwnerId { get; }

    /// <summary>
    /// Maps a route onto an inline handler, for an operation whose contract is not a mediator request.
    /// </summary>
    /// <remarks>
    /// Binding, failure translation, and endpoint metadata are identical to
    /// <see cref="MapRequest{TRequest,TResponse}"/>; only dispatch differs. Authoring and
    /// expression-tooling operations resolve domain services directly rather than going through the
    /// mediator, and this keeps them on the same convention instead of leaving them hand-written.
    /// </remarks>
    public IEndpointConventionBuilder MapHandler<TRequest, TResponse>(
        string method,
        string pattern,
        string operation,
        Func<HttpContext, TRequest, CancellationToken, Task<TResponse>> handler,
        EndpointBodyMode? bodyMode = null,
        string[]? accepts = null,
        int successStatus = StatusCodes.Status200OK)
        where TResponse : notnull =>
        MapOperation<TRequest>(            method, pattern, operation, bodyMode, accepts, typeof(TResponse), successStatus, null,
            async (context, request, cancellationToken) =>
            {
                var response = await handler(context, request, cancellationToken);
                await WriteJsonAsync(context, response, successStatus);
            });

    /// <summary>Maps a route that takes no request contract onto an inline handler.</summary>
    public IEndpointConventionBuilder MapHandler<TResponse>(
        string method,
        string pattern,
        string operation,
        Func<HttpContext, CancellationToken, Task<TResponse>> handler,
        int successStatus = StatusCodes.Status200OK)
        where TResponse : notnull =>
        MapUnbound(method, pattern, operation, typeof(TResponse), successStatus,
            async context => await WriteJsonAsync(context, await handler(context, context.RequestAborted), successStatus));

    /// <summary>Writes a value using the owner's source-generated serializer metadata.</summary>
    public Task WriteJsonAsync<TValue>(HttpContext context, TValue value, int statusCode = StatusCodes.Status200OK)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.StatusCode = statusCode;
        var typeInfo = _jsonContext.GetTypeInfo(typeof(TValue))
                       ?? throw new InvalidOperationException($"No source-generated JSON metadata exists for '{typeof(TValue).FullName}'.");
        return Results.Json(value, typeInfo, JsonContentType).ExecuteAsync(context);
    }

    /// <summary>
    /// The low-level operation pipeline: bind, dispatch, translate failures, and attach the module
    /// operation metadata. The typed Map methods and external bridges compose on top of this.
    /// </summary>
    public IEndpointConventionBuilder MapOperation<TMessage>(
        string method,
        string pattern,
        string operation,
        EndpointBodyMode? bodyMode,
        string[]? accepts,
        Type? responseType,
        int successStatus,
        int? documentedStatus,
        Func<HttpContext, TMessage, CancellationToken, Task> dispatch)
    {
        var effectiveBodyMode = bodyMode ?? DefaultBodyMode(method);
        var jsonOptions = _jsonContext.Options;

        RequestDelegate handler = async context =>
        {
            var binding = await EndpointRequestBinder.BindAsync<TMessage>(context, jsonOptions, effectiveBodyMode);
            if (!binding.Succeeded)
            {
                // RequiredWithContentType rejects media types with a bare status, before any body is
                // read, so there is no problem document to write.
                if (binding.Failure is EndpointBindingFailure.UnsupportedMediaType &&
                    effectiveBodyMode is EndpointBodyMode.RequiredWithContentType)
                {
                    context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                    return;
                }

                await WriteProblemAsync(context, ToProblem(binding));
                return;
            }

            try
            {
                await dispatch(context, binding.Value!, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var problem = Translate(context, exception);
                if (problem is null)
                {
                    LogUnexpected(context, exception, typeof(TMessage));
                    problem = EndpointProblem.General(StatusCodes.Status500InternalServerError, "Unexpected error occurred");
                }

                await WriteProblemAsync(context, problem);
            }
        };

        var builder = _endpoints.MapMethods(pattern, [method], handler);

        // An endpoint may describe a request schema it does not bind from the body: several existing
        // GET operations advertise their request shape in the document while binding from the query.
        // Declaring accepts is therefore what decides the OpenAPI request type, not the body mode.
        var declaresRequest = accepts is not null || effectiveBodyMode is not EndpointBodyMode.None;
        return builder.WithModuleOperation(
            $"{OwnerId.Replace(".", string.Empty, StringComparison.Ordinal)}Endpoints{operation}",
            OwnerId,
            responseType,
            declaresRequest ? typeof(TMessage) : null,
            accepts,
            documentedStatus ?? successStatus);
    }

    /// <summary>Maps an options-described operation returning a body. Used by the endpoint-class mapper.</summary>
    internal IEndpointConventionBuilder MapBody<TRequest, TResponse>(
        ApiEndpointOptions options,
        Func<HttpContext, TRequest, CancellationToken, Task<TResponse>> dispatch)
        where TResponse : notnull =>
        MapOperation<TRequest>(            options.Method!, options.Route!, options.Operation!, options.BodyMode, options.Accepts,
            typeof(TResponse), options.SuccessStatus, options.DocumentedStatus,
            async (context, request, cancellationToken) =>
                await WriteJsonAsync(context, await dispatch(context, request, cancellationToken), options.SuccessStatus));

    /// <summary>Maps an options-described operation with no request contract. Used by the endpoint-class mapper.</summary>
    internal IEndpointConventionBuilder MapUnboundBody<TResponse>(
        ApiEndpointOptions options,
        Func<HttpContext, CancellationToken, Task<TResponse>> dispatch)
        where TResponse : notnull =>
        MapUnbound(options.Method!, options.Route!, options.Operation!, typeof(TResponse), options.SuccessStatus,
            async context => await WriteJsonAsync(context, await dispatch(context, context.RequestAborted), options.SuccessStatus));

    /// <summary>Maps an options-described operation whose status travels with the result. Used by the endpoint-class mapper.</summary>
    internal IEndpointConventionBuilder MapResultBody<TRequest, TResponse>(
        ApiEndpointOptions options,
        Func<HttpContext, TRequest, CancellationToken, Task<EndpointResult<TResponse>>> dispatch)
        where TResponse : notnull =>
        MapOperation<TRequest>(            options.Method!, options.Route!, options.Operation!, options.BodyMode, options.Accepts,
            typeof(TResponse), options.SuccessStatus, options.DocumentedStatus,
            async (context, request, cancellationToken) =>
            {
                var result = await dispatch(context, request, cancellationToken);
                await WriteJsonAsync(context, result.Response, result.StatusCode);
            });

    /// <summary>Maps an options-described operation returning no content. Used by the endpoint-class mapper.</summary>
    internal IEndpointConventionBuilder MapNoContent<TRequest>(
        ApiEndpointOptions options,
        Func<HttpContext, TRequest, CancellationToken, Task> dispatch) =>
        MapOperation<TRequest>(            options.Method!, options.Route!, options.Operation!, options.BodyMode, options.Accepts,
            null, StatusCodes.Status204NoContent, options.DocumentedStatus,
            async (context, request, cancellationToken) =>
            {
                await dispatch(context, request, cancellationToken);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            });

    private static EndpointBodyMode DefaultBodyMode(string method) => method switch
    {
        _ when HttpMethods.IsGet(method) || HttpMethods.IsHead(method) => EndpointBodyMode.None,
        _ when HttpMethods.IsDelete(method) => EndpointBodyMode.Optional,
        _ => EndpointBodyMode.Required
    };

    private static EndpointProblem ToProblem<T>(EndpointBindingResult<T> binding) => binding.Failure switch
    {
        EndpointBindingFailure.UnsupportedMediaType =>
            EndpointProblem.General(StatusCodes.Status415UnsupportedMediaType, binding.Message!),
        EndpointBindingFailure.MalformedBody =>
            EndpointProblem.General(StatusCodes.Status400BadRequest, binding.Message!, "serializerErrors"),
        _ => EndpointProblem.General(StatusCodes.Status400BadRequest, binding.Message!)
    };

    private static EndpointProblem? Translate(HttpContext context, Exception exception)
    {
        foreach (var translator in context.RequestServices.GetServices<IEndpointExceptionTranslator>())
        {
            var problem = translator.Translate(exception);
            if (problem is not null)
                return problem;
        }

        return null;
    }

    private IEndpointConventionBuilder MapUnbound(
        string method,
        string pattern,
        string operation,
        Type? responseType,
        int successStatus,
        Func<HttpContext, Task> dispatch)
    {
        RequestDelegate handler = async context =>
        {
            try
            {
                await dispatch(context);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var problem = Translate(context, exception);
                if (problem is null)
                {
                    LogUnexpected(context, exception, typeof(ModuleEndpointGroup));
                    problem = EndpointProblem.General(StatusCodes.Status500InternalServerError, "Unexpected error occurred");
                }

                await WriteProblemAsync(context, problem);
            }
        };

        return _endpoints.MapMethods(pattern, [method], handler)
            .WithModuleOperation(
                $"{OwnerId.Replace(".", string.Empty, StringComparison.Ordinal)}Endpoints{operation}",
                OwnerId,
                responseType,
                null,
                null,
                successStatus);
    }

    private static Task WriteProblemAsync(HttpContext context, EndpointProblem problem) =>
        context.RequestServices.GetRequiredService<IEndpointProblemWriter>().WriteAsync(context, problem);

    private static void LogUnexpected(HttpContext context, Exception exception, Type messageType) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(ModuleEndpointGroup))
            .LogError(exception, "Unexpected error occurred when handling request '{type}'", messageType);
}
