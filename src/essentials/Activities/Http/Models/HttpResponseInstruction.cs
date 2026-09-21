using Elsa.Activities.Runtime.Core.Attributes;

namespace Elsa.Activities.Http.Models;

/// <summary>
/// The atomic HTTP response instruction returned by <see cref="Activities.WriteHttpResponse"/>.
/// </summary>
/// <remarks>The runtime persists this value as the activity completion result. Live HTTP delivery, when present,
/// uses this same value and does not create a second workflow-data channel.</remarks>
/// <param name="StatusCode">The HTTP status code to return. Defaults to 200 when not set by the author.</param>
/// <param name="Headers">Response headers to return, keyed by header name.</param>
/// <param name="Body">The response body, if any.</param>
/// <param name="ContentType">The response content type (media type), if any.</param>
public sealed record HttpResponseInstruction(
    [property: Output(Key = "StatusCode", Path = "statusCode")] int StatusCode,
    [property: Output(Key = "Headers", Path = "headers")] IDictionary<string, string[]> Headers,
    [property: Output(Key = "Body", Path = "body")] string? Body,
    [property: Output(Key = "ContentType", Path = "contentType")] string? ContentType);
