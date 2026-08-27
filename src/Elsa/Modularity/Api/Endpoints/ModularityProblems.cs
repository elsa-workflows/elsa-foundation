using Elsa.Api.AspNetCore;
using Elsa.Modularity.Core.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeEndpoints;
using System.Text.Json;

namespace Elsa.Modularity.Api.Endpoints;

/// <summary>The owner's published legacy error envelope, written exactly as the mapper wrote it.</summary>
internal static class ModularityProblemWriting
{
    public static Task WriteLegacyErrorAsync(HttpContext context, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json; charset=utf-8";
        var error = new ModularityError(
            new Dictionary<string, string[]> { ["generalErrors"] = [message] },
            "One or more errors occurred!",
            statusCode);
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(error, ModularityJsonContext.Default.ModularityError),
            context.RequestAborted);
    }
}

/// <summary>Publishes binder failures in the owner's legacy general-errors envelope.</summary>
public sealed class ModularityProblemWriter : IEndpointProblemWriter
{
    public Task WriteAsync(HttpContext context, EndpointProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var message = problem.Errors.Values.SelectMany(messages => messages).FirstOrDefault() ?? "Unexpected error occurred";
        return ModularityProblemWriting.WriteLegacyErrorAsync(context, message, problem.StatusCode);
    }
}

/// <summary>Reproduces the apply operation's catch ladder over the owner's legacy envelope.</summary>
public sealed class ModularityFaultRenderer : IEndpointFaultRenderer
{
    public async ValueTask<bool> TryWriteAsync(HttpContext context, Exception exception)
    {
        switch (exception)
        {
            // The revision conflict derives from InvalidOperationException, so its arm goes first.
            case FeatureCatalogRevisionConflictException conflict:
                await ModularityProblemWriting.WriteLegacyErrorAsync(context, conflict.Message, StatusCodes.Status409Conflict);
                return true;
            case ArgumentException argument:
                await ModularityProblemWriting.WriteLegacyErrorAsync(context, argument.Message, StatusCodes.Status400BadRequest);
                return true;
            case InvalidOperationException invalid:
                await ModularityProblemWriting.WriteLegacyErrorAsync(context, invalid.Message, StatusCodes.Status400BadRequest);
                return true;
            default:
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(ModularityApi))
                    .LogError(exception, "Unexpected error occurred when applying feature management changes.");
                await ModularityProblemWriting.WriteLegacyErrorAsync(context, "Unexpected error occurred", StatusCodes.Status500InternalServerError);
                return true;
        }
    }
}
