using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Activities.Http.Models;
using Elsa.Http.Core.Contracts;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Http.Services;

/// <summary>
/// Delivers a committed <see cref="HttpResponseInstruction"/> to the live response after a synchronous inline
/// workflow drain. The request remains outside the transient activity activation and its isolated DI scope.
/// </summary>
public sealed class HttpResponseInstructionDelivery(
    IActivityExecutionStateStore activityExecutionStateStore,
    IEnumerable<IHttpContentFactory> contentFactories,
    HttpResponseDeliveryEvidence? evidence = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonWriterOptions BodyWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly string ResultTypeAlias = TypeAliasConvention.CanonicalAlias(typeof(HttpResponseInstruction));

    /// <summary>
    /// Writes the first committed instruction in dispatch order. Returns false when none of the dispatched
    /// workflow instances reached <see cref="Activities.WriteHttpResponse"/> during the inline drain.
    /// </summary>
    public async ValueTask<bool> TryDeliverAsync(
        HttpResponse response,
        IReadOnlyCollection<string> workflowExecutionIds,
        CancellationToken cancellationToken = default,
        string? exchangeId = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(workflowExecutionIds);

        if (response.HasStarted)
            return true;

        foreach (var workflowExecutionId in workflowExecutionIds.Distinct(StringComparer.Ordinal))
        {
            var states = await activityExecutionStateStore.ListAllAsync(workflowExecutionId, cancellationToken);
            foreach (var state in states
                         .Where(IsResponseInstructionCompletion)
                         .OrderBy(candidate => candidate.CompletedAt)
                         .ThenBy(candidate => candidate.ExecutionSequence))
            {
                var payload = state.Completion!.Result.InlineValue!.Value;
                var instruction = payload.Deserialize<HttpResponseInstruction>(SerializerOptions)
                    ?? throw new InvalidOperationException(
                        $"Committed HTTP response instruction '{state.InvocationId}' resolved to null.");
                await WriteAsync(response, instruction, cancellationToken);
                if (evidence is not null) await evidence.RecordWrittenAsync(state, exchangeId, CancellationToken.None);
                return true;
            }
        }

        return false;
    }

    private static bool IsResponseInstructionCompletion(ActivityExecutionState state) =>
        state.Status == ActivityExecutionStatus.Completed &&
        state.Completion is { Result.Presence: ValuePresence.Present, Result.InlineValue: not null } completion &&
        StringComparer.Ordinal.Equals(completion.Result.Type.Alias, ResultTypeAlias);

    private async ValueTask WriteAsync(
        HttpResponse response,
        HttpResponseInstruction instruction,
        CancellationToken cancellationToken)
    {
        response.StatusCode = instruction.StatusCode;

        foreach (var (name, values) in instruction.Headers)
            response.Headers[name] = values;

        var body = BodyText(instruction.Body);
        var contentType = string.IsNullOrWhiteSpace(instruction.ContentType)
            ? InferContentType(instruction.Body)
            : instruction.ContentType;

        if (string.IsNullOrEmpty(body) || instruction.StatusCode == StatusCodes.Status204NoContent)
        {
            if (!string.IsNullOrWhiteSpace(instruction.ContentType))
                response.ContentType = instruction.ContentType;
            return;
        }

        var factory = MatchContentFactory(contentType);
        if (factory is not null)
        {
            using var content = factory.CreateHttpContent(body, contentType);
            if (content.Headers.ContentType is { } factoryContentType)
                response.ContentType = factoryContentType.ToString();
            await content.CopyToAsync(response.Body, cancellationToken);
            return;
        }

        response.ContentType = contentType;
        await response.WriteAsync(body, cancellationToken);
    }

    /// <summary>
    /// The text to send: a JSON string as its text, any other value as compact JSON with its keys and characters as the
    /// workflow produced them. A JSON null is no body.
    /// </summary>
    private static string? BodyText(JsonElement? body) => body switch
    {
        null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
        { ValueKind: JsonValueKind.String } text => text.GetString(),
        { } value => CompactJson(value)
    };

    private static string CompactJson(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, BodyWriterOptions))
            value.WriteTo(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Elsa 3's inference for an unset content type: text is <c>text/plain</c>, any other value JSON.</summary>
    private static string InferContentType(JsonElement? body) =>
        body is { ValueKind: JsonValueKind.String } ? "text/plain" : "application/json";

    private IHttpContentFactory? MatchContentFactory(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return contentFactories.FirstOrDefault(factory => factory.SupportedContentTypes
            .Any(supported => string.Equals(supported, mediaType, StringComparison.OrdinalIgnoreCase)));
    }
}
