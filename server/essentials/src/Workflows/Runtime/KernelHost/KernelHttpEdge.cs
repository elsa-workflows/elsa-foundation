using System.Text.Json.Nodes;
using Kernel.Native;
using Contract = Kernel.Foundation.Contracts;

namespace Elsa.Workflows.Runtime.KernelHost;

/// <summary>A request's start on a kernel host: the start of the workflow its route serves, and whether it answers before the commit.</summary>
public sealed record KernelHttpStart(Contract.StartSpec Spec, string Workflow, bool RespondBeforeCommit);

/// <summary>What an HTTP edge writes back for a held start's reply.</summary>
public sealed record KernelHttpAnswer(int StatusCode, string ContentType, string? Body, IReadOnlyDictionary<string, string> Headers);

/// <summary>
/// The framework-free half of a kernel host's HTTP edge. A request's path, relative to the edge's base path, is matched
/// against the start routes of what serves now (<see cref="KernelServing.Routes"/>); a match starts the route's workflow
/// with the request as its trigger. Wait routes are not served by a start: a request for one, or for no route, is not
/// found. A held start's reply becomes the HTTP answer as the test edges map it.
/// </summary>
public static class KernelHttpEdge
{
    // Before NotFound: static fields initialize in order.
    private static readonly IReadOnlyDictionary<string, string> NoHeaders = new Dictionary<string, string>();

    /// <summary>The start route of what serves now that a request's method and path match, or null when none does.</summary>
    /// <param name="path">The request path as the client sent it, relative to the edge's base path.</param>
    /// <remarks>An edge matches the route before it reads the request's body, so a path no route serves reads none.</remarks>
    public static NativeHttpRouteMatch? Route(KernelServing serving, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(serving);
        var match = HttpRouteMatcher.Match(serving.Routes, method, path);
        return match.Route is { Start: true } route && serving.Registry.Contains(route.Workflow) ? match : null;
    }

    /// <summary>
    /// The start a request on a matched route makes, or null when the route's workflow no longer serves (a republish between
    /// the match and the start).
    /// </summary>
    /// <param name="request">The request, built by the edge from the wire; its route values are filled in from the match.</param>
    public static KernelHttpStart? Start(KernelServing serving, NativeHttpRouteMatch match, HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(serving);
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(request);
        // What serves now: the route the match found, still served by the same workflow's node.
        if (match.Route is not { Start: true } route || !serving.Routes.TryStart(route.Method, route.Template.Text, out var served) ||
            served != (route.Workflow, route.Node) || !serving.Registry.TryGet(route.Workflow, out var workflow)) return null;
        var trigger = Values.From(request with { RouteValues = match.Values });
        var start = NativeExecutionModel.Start(workflow.Id, Modes.Balanced, null, route.Node, trigger, workflow.Definition);
        return new(Kernel.WorkflowActor.WorkflowActor.Start(start), workflow.Id, route.RespondBeforeCommit);
    }

    /// <summary>The start a request makes, or null when no start route of what serves matches its method and path.</summary>
    /// <param name="path">The request path as the client sent it, relative to the edge's base path.</param>
    public static KernelHttpStart? Match(KernelServing serving, HttpRequest request, string path)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Route(serving, request.Method, path) is { } match ? Start(serving, match, request) : null;
    }

    /// <summary>The HTTP answer to a held start's reply.</summary>
    public static KernelHttpAnswer Answer(Contract.Reply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        return reply switch
        {
            { Error: "no waiter" or "missing" } => Text(404, "no workflow serves this route"),
            { Error: "timeout" } => Text(504, "the workflow did not answer in time"),
            { Error: "lost" } => Text(503, "the workflow's owner was lost"),
            { Status: Contract.ActorStatus.Faulted } => Text(500, "the workflow faulted"),
            { Error: null, Channel: "response", Item: JsonNode item } when Values.To<HttpResponse>(item) is { } response => Designed(response),
            // Terminated before it answered: no response, which a 204 would pass off as one.
            { Error: null, Item: null, Status: Contract.ActorStatus.Cancelled } => Text(503, "The workflow was terminated before it answered."),
            { Error: null, Item: null } => new(204, "text/plain", null, NoHeaders),
            _ => Text(500, "the workflow answered with something this edge cannot send")
        };
    }

    /// <summary>The answer for a request no start route serves.</summary>
    public static KernelHttpAnswer NotFound { get; } = Text(404, "no workflow serves this route");

    private static KernelHttpAnswer Designed(HttpResponse response) => new(response.StatusCode, response.ContentType,
        response.Body is JsonValue scalar && response.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
        scalar.TryGetValue<string>(out var text) ? text : response.Body?.ToJsonString(), response.Headers);

    private static KernelHttpAnswer Text(int status, string text) => new(status, "text/plain; charset=utf-8", text, NoHeaders);
}
