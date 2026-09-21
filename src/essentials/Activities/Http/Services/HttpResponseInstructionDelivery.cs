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
    IEnumerable<IHttpContentFactory> contentFactories)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string ResultTypeAlias = TypeAliasConvention.CanonicalAlias(typeof(HttpResponseInstruction));

    /// <summary>
    /// Writes the first committed instruction in dispatch order. Returns false when none of the dispatched
    /// workflow instances reached <see cref="Activities.WriteHttpResponse"/> during the inline drain.
    /// </summary>
    public async ValueTask<bool> TryDeliverAsync(
        HttpResponse response,
        IReadOnlyCollection<string> workflowExecutionIds,
        CancellationToken cancellationToken = default)
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

        if (string.IsNullOrEmpty(instruction.Body))
        {
            if (!string.IsNullOrWhiteSpace(instruction.ContentType))
                response.ContentType = instruction.ContentType;
            return;
        }

        var factory = MatchContentFactory(instruction.ContentType);
        if (factory is not null)
        {
            using var content = factory.CreateHttpContent(instruction.Body, instruction.ContentType!);
            if (content.Headers.ContentType is { } factoryContentType)
                response.ContentType = factoryContentType.ToString();
            await content.CopyToAsync(response.Body, cancellationToken);
            return;
        }

        response.ContentType = string.IsNullOrWhiteSpace(instruction.ContentType)
            ? "text/plain"
            : instruction.ContentType;
        await response.WriteAsync(instruction.Body, cancellationToken);
    }

    private IHttpContentFactory? MatchContentFactory(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return contentFactories.FirstOrDefault(factory => factory.SupportedContentTypes
            .Any(supported => string.Equals(supported, mediaType, StringComparison.OrdinalIgnoreCase)));
    }
}
