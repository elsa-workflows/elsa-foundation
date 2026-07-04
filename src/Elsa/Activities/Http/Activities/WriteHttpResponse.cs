using Elsa.Activities.Http.Models;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Http.Activities;

/// <summary>
/// Records the HTTP response a workflow intends to return, as a durable, observable artifact. In the async/202
/// endpoint model (see <see cref="HttpEndpoint"/>) no HTTP request is waiting when the workflow executes — the
/// endpoint already replied <c>202 Accepted</c> — so this activity does <b>not</b> write to a live response
/// stream. Instead it records exactly one <see cref="HttpResponseInstruction"/> into the durable workflow-output
/// channel under <see cref="HttpResponseInstruction.OutputName"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is not a no-op.</b> Recording the artifact is the activity's whole effect, and it is durably
/// persisted and assertable immediately after the run (through the same output channel any workflow output
/// uses). The recorded artifact is also, by construction, the exact payload the future "HTTP synchronous
/// response correlation" subsystem will deliver to a caller waiting on a keyed channel — so nothing about the
/// authored contract changes when synchronous correlation lands; only the delivery mechanism is added.
/// </para>
/// <para>
/// Like the other control leaves, <see cref="WriteHttpResponse"/> requires the Elsa runtime activity execution
/// context (it records a workflow output).
/// </para>
/// </remarks>
public sealed class WriteHttpResponse : CodeActivity
{
    /// <summary>The HTTP status code to record. Defaults to 200 when not set.</summary>
    public InputArgument<int>? StatusCode { get; set; }

    /// <summary>The response body to record.</summary>
    public InputArgument<string>? Body { get; set; }

    /// <summary>The response content type (media type) to record.</summary>
    public InputArgument<string>? ContentType { get; set; }

    /// <summary>Response headers to record, keyed by header name.</summary>
    public InputArgument<IDictionary<string, string[]>>? Headers { get; set; }

    protected override void Execute(IActivityExecutionContext context)
    {
        var runtimeContext = RequireRuntimeContext(context);

        var statusCode = context.Get(StatusCode);
        var effectiveStatus = statusCode > 0 ? statusCode : 200;
        var headers = context.Get(Headers) ?? new Dictionary<string, string[]>();

        var instruction = new HttpResponseInstruction(
            StatusCode: effectiveStatus,
            Headers: headers,
            Body: context.Get(Body),
            ContentType: context.Get(ContentType));

        runtimeContext.SetWorkflowOutput(HttpResponseInstruction.OutputName, instruction);
    }

    private static IRuntimeActivityExecutionContext RequireRuntimeContext(IActivityExecutionContext context)
    {
        if (context is IRuntimeActivityExecutionContext runtimeContext)
            return runtimeContext;

        throw new InvalidOperationException("WriteHttpResponse requires an Elsa runtime activity execution context.");
    }
}
