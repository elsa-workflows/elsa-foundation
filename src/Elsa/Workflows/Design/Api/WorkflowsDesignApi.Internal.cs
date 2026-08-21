using Elsa.Api.AspNetCore;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Api.Authorization;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Constants;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Api.Services;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;

namespace Elsa.Workflows.Design.Api;

/// <summary>Maps the workflow design management surface using ordinary ASP.NET Core endpoints.</summary>
/// <summary>Shared mediator dispatch, JSON binding, result writing, and metadata helpers for the Workflows Design API.</summary>
public static partial class WorkflowsDesignApi
{
    private static async Task<TResponse?> SendRequestAsync<TRequest, TResponse>(HttpContext context, TRequest request)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        var sender = context.RequestServices.GetRequiredService<IRequestSender>();
        return await sender.Send(request, context.RequestAborted);
    }

    private static async Task SendCommandAsync<TCommand>(HttpContext context, TCommand command)
        where TCommand : ICommand =>
        await context.RequestServices.GetRequiredService<ICommandSender>().Send(command, context.RequestAborted);

    private static async Task RequestResult<TRequest, TResponse>(HttpContext context, TRequest? request)
        where TRequest : IRequest<TResponse>
        where TResponse : notnull
    {
        if (request is null)
            return;
        try
        {
            await JsonResult(context, await SendRequestAsync<TRequest, TResponse>(context, request));
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(TRequest));
            await WriteLegacyErrorAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task CommandResult<TCommand, TResponse>(HttpContext context, TCommand? command, int statusCode = StatusCodes.Status200OK, bool promote = false)
        where TCommand : ICommand<TResponse>
        where TResponse : notnull
    {
        if (command is null)
            return;
        try
        {
            var response = await context.RequestServices.GetRequiredService<ICommandSender>().Send(command, context.RequestAborted);
            await JsonResult(context, response, statusCode);
        }
        catch (DraftHasValidationErrorsException exception) when (promote)
        {
            var errors = exception.Errors
                .GroupBy(error => string.IsNullOrWhiteSpace(error.Path) ? "generalErrors" : error.Path, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray(), StringComparer.Ordinal);
            errors.TryAdd("generalErrors", [exception.Message]);
            await WriteValidationErrorAsync(context, errors, StatusCodes.Status409Conflict);
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (WorkflowDefinitionVersionConflictException exception) when (promote)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (WorkflowPromotionOperationConflictException exception) when (promote)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (WorkflowDefinitionNotSoftDeletedException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (PermanentDeletionUnavailableException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status501NotImplemented);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(TCommand));
            await WriteLegacyErrorAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task NoContentResult<TCommand>(HttpContext context, TCommand? command)
        where TCommand : ICommand
    {
        if (command is null)
            return;
        try
        {
            await SendCommandAsync(context, command);
            await Results.NoContent().ExecuteAsync(context);
        }
        catch (EntityNotFoundException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status404NotFound);
        }
        catch (WorkflowDefinitionNotSoftDeletedException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status409Conflict);
        }
        catch (PermanentDeletionUnavailableException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status501NotImplemented);
        }
        catch (ArgumentException exception)
        {
            await WriteLegacyErrorAsync(context, exception.Message, StatusCodes.Status400BadRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogUnexpected(context, exception, typeof(TCommand));
            await WriteLegacyErrorAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
        }
    }

    private static async ValueTask<T?> ReadJsonAsync<T>(HttpContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Request.ContentType) &&
            !string.Equals(context.Request.ContentType.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase))
        {
            await WriteLegacyErrorAsync(context, "The request content type must be application/json.", StatusCodes.Status415UnsupportedMediaType);
            return default;
        }

        try
        {
            var request = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, WorkflowsDesignJsonContext.Default.Options, context.RequestAborted);
            if (request is not null)
                return request;
        }
        catch (JsonException exception)
        {
            var message = exception.Message.Replace(" Path: $ |", "", StringComparison.Ordinal);
            await WriteBindingErrorAsync(context, message, StatusCodes.Status400BadRequest);
            return default;
        }

        await WriteLegacyErrorAsync(context, "A request body is required.", StatusCodes.Status400BadRequest);
        return default;
    }

    private static Task JsonResult<T>(HttpContext context, T value, int statusCode = StatusCodes.Status200OK, string contentType = JsonContentType)
    {
        context.Response.StatusCode = statusCode;
        var typeInfo = WorkflowsDesignJsonContext.Default.GetTypeInfo(typeof(T))
                       ?? throw new InvalidOperationException($"No source-generated JSON metadata exists for '{typeof(T).FullName}'.");
        return Results.Json(value, typeInfo, contentType).ExecuteAsync(context);
    }

    private static Task WriteLegacyErrorAsync(HttpContext context, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        return JsonResult(context, new WorkflowDesignError(
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["generalErrors"] = [message] },
            "One or more errors occurred!", statusCode), statusCode, ProblemJsonContentType);
    }

    private static Task WriteValidationErrorAsync(HttpContext context, IReadOnlyDictionary<string, string[]> errors, int statusCode) =>
        JsonResult(context, new WorkflowDesignError(errors, "One or more errors occurred!", statusCode), statusCode, ProblemJsonContentType);

    private static Task WriteBindingErrorAsync(HttpContext context, string message, int statusCode) =>
        JsonResult(context, new WorkflowDesignError(
            new Dictionary<string, string[]>(StringComparer.Ordinal) { ["serializerErrors"] = [message] },
            "One or more errors occurred!", statusCode), statusCode, ProblemJsonContentType);

    private static string? Route(HttpContext context, string key) =>
        context.Request.RouteValues.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool DeleteWithoutJsonBody(HttpContext context) =>
        HttpMethods.IsDelete(context.Request.Method) &&
        (string.IsNullOrWhiteSpace(context.Request.ContentType) ||
         !string.Equals(context.Request.ContentType.Split(';', 2)[0].Trim(), "application/json", StringComparison.OrdinalIgnoreCase));

    private static string? Query(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var value) ? value.ToString() : null;

    private static bool? NullableBool(IQueryCollection query, string key) =>
        bool.TryParse(Query(query, key), out var value) ? value : null;

    private static void LogUnexpected(HttpContext context, Exception exception, Type requestType) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(WorkflowsDesignApi))
            .LogError(exception, "Unexpected error occurred when handling request '{type}'", requestType);
}

internal sealed record WorkflowDesignError(
    IReadOnlyDictionary<string, string[]> Errors,
    string Message,
    int StatusCode);
