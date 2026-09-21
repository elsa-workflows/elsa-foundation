using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.Activities;

/// <summary>Returns one atomic, durable HTTP response instruction.</summary>
/// <remarks>
/// This activity has no live request dependency and does not mutate an ambient workflow-output bag. The runtime
/// commits the returned <see cref="HttpResponseInstruction"/> as the typed activity completion. A synchronous
/// HTTP transport may deliver that committed instruction after its inline workflow drain completes.
/// </remarks>
public sealed class WriteHttpResponse : Activity<HttpResponseInstruction>
{
    /// <summary>The HTTP status code to return. Values less than one default to 200.</summary>
    [ActivityInput(Key = nameof(StatusCode), DefaultValue = "200", DefaultSyntax = "Literal")]
    public int StatusCode { get; set; } = StatusCodes.Status200OK;

    /// <summary>The response body to return.</summary>
    [ActivityInput(Key = nameof(Body))]
    public string? Body { get; set; }

    /// <summary>The response content type (media type) to return.</summary>
    [ActivityInput(Key = nameof(ContentType))]
    public string? ContentType { get; set; }

    /// <summary>Response headers to return, keyed by header name.</summary>
    [ActivityInput(Key = nameof(Headers))]
    public IDictionary<string, string[]>? Headers { get; set; }

    protected override ValueTask<ActivityTransition<HttpResponseInstruction>> ExecuteAsync(
        ActivityExecutionContext context)
    {
        var instruction = new HttpResponseInstruction(
            StatusCode > 0 ? StatusCode : StatusCodes.Status200OK,
            Headers ?? new Dictionary<string, string[]>(),
            Body,
            ContentType);

        return ValueTask.FromResult(ActivityTransition.Complete(instruction));
    }
}
