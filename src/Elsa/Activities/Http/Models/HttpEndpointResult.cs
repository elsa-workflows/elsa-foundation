using System.Text.Json;
using Elsa.Activities.Runtime.Core.Attributes;

namespace Elsa.Activities.Http.Models;

/// <summary>The atomic, persistable value returned by <see cref="Activities.HttpEndpoint"/>.</summary>
public sealed record HttpEndpointResult(
    [property: Output(Key = "Request", Path = "request")] HttpRequestModel Request,
    [property: Output(Key = "RouteData", Path = "routeData")] IReadOnlyDictionary<string, string> RouteData,
    [property: Output(Key = "ParsedContent", Path = "parsedContent", IsRequired = false)] JsonElement? ParsedContent);

/// <summary>The complete private state required to produce a defensive authored-route fallback on resume.</summary>
public sealed record HttpEndpointState(string Path);
