using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Hosting;
using Kernel.Graph.Exceptions;
using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Reconstructs only the wait keys and request committed by the authorized standalone HTTP source.</summary>
internal sealed class KernelStandaloneHttpWaits(KernelServing serving)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    internal bool CanAccept(RuntimeStandaloneIngressSource source) => Read(source) is { } plan && AllowsNew(plan);

    internal async ValueTask DeliverAsync(IPreparedRootStartHandoff foundation, RuntimePostCommitOutboxItem item, CancellationToken token)
    {
        if (item.SubscriptionDelivery is not { Recipients.Count: > 0 } || !PreparedStandaloneHttp.HasVerifiedEmptyWaits(item) ||
            item.StandaloneIngress is not { } source || Read(source) is not { } plan)
            throw new NotSupportedException("The accepted standalone HTTP source has no qualified detached wait plan.");
        var operation = RuntimeDeliveryFingerprint.Compute("elsa.runtime.foundation-http-wait-source.v1", source.OutboxItemId);
        _ = await foundation.DeliverWaitsAsync(operation, source.Fingerprint, plan.Deliveries, AllowsNew(plan), token);
    }

    private bool AllowsNew(WaitPlan plan) => serving.MatchHttp(plan.Method, plan.Path) switch
    {
        null => true,
        { Route.Route: { Start: false } route } => route.Template.Segments.All(segment => segment.Kind == HttpRouteSegmentKind.Literal) ||
            route.Template.Text == plan.Template,
        _ => false
    };

    private static WaitPlan? Read(RuntimeStandaloneIngressSource source)
    {
        if (!PreparedStandaloneHttp.IsHttp(source) || !source.CanonicalPayload.TryGetProperty("waitCandidate", out var saved) ||
            saved.ValueKind != JsonValueKind.Object ||
            !source.CanonicalPayload.TryGetProperty("submittedIntent", out var intent) ||
            !source.CanonicalPayload.TryGetProperty("payload", out var payload)) return null;
        try
        {
            if (saved.Deserialize<RuntimeIngressWaitCandidate>(Web) is not { Input: { } input, ObservedWaits.Count: 0 } candidate ||
                candidate.Metadata.GetValueOrDefault(RuntimeMetadataKeys.StandaloneHttpExactPath) is not { } path ||
                input.Deserialize<CapturedRequest>() is not { Method.Length: > 0, Headers: not null, Query: not null, RouteData: not null } request ||
                !SafeRelativePath(path) ||
                intent.GetProperty("method").GetString() != request.Method ||
                !string.Equals(request.Path, path, StringComparison.OrdinalIgnoreCase) ||
                intent.GetProperty("path").GetString() is not { } publicPath || !publicPath.StartsWith('/') ||
                !publicPath.TrimEnd('/').EndsWith("/" + path, StringComparison.Ordinal) ||
                source.CanonicalPayload.GetProperty("trigger").GetProperty("configuration").GetProperty("path").GetString() is not { } route)
                return null;
            var template = HttpRouteTemplate.Parse(route);
            var names = HttpFields.RouteValues(request.RouteData);
            if (!template.FullyBound || !SameConcretePath(template, path, names)) return null;

            var body = payload.ValueKind == JsonValueKind.Null ? null : JsonNode.Parse(payload.GetRawText());
            var model = new HttpRequest(request.Method, publicPath, body)
            {
                Headers = HttpFields.RequestHeaders(Flatten(request.Headers)),
                Query = HttpFields.Query(Flatten(request.Query))
            };
            var literal = HttpRouteMatcher.Literal(request.Method, path).Key;
            var deliveries = new List<PreparedWaitDelivery> { new(literal, JsonSerializer.Serialize(model)) };
            if (template.Segments.Any(segment => segment.Kind == HttpRouteSegmentKind.Parameter))
            {
                var resolved = HttpRoutes.WaitKey(request.Method, template, names);
                deliveries.Add(new(resolved, JsonSerializer.Serialize(model with { RouteValues = names })));
            }
            // The existing hashed alias can coincide with a literal '#<digest>' activity. This private bridge accepts
            // bounded concrete paths only, and never attempts a second legacy encoding of a route.
            return deliveries.Any(delivery => delivery.Key.Contains('#')) ? null : new(request.Method, "/" + path, template.Text, deliveries);
        }
        catch (Exception error) when (error is JsonException or HttpRouteTemplateException or HttpFieldsException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static bool SameConcretePath(HttpRouteTemplate template, string path, IReadOnlyDictionary<string, string> values)
    {
        var actual = path.Split('/');
        return actual.Length == template.Segments.Count && template.Segments.Zip(actual).All(pair => pair.First.Kind switch
        {
            HttpRouteSegmentKind.Literal => string.Equals(pair.First.Text, pair.Second, StringComparison.OrdinalIgnoreCase),
            HttpRouteSegmentKind.Parameter => values.TryGetValue(pair.First.Text, out var value) && value == pair.Second,
            _ => false
        });
    }

    private static bool SafeRelativePath(string path) => path.Length is > 0 and <= HttpRoutes.MaxReadableKeyBytes &&
        path.Split('/').All(segment => segment.Length > 0 && segment is not ("." or "..") &&
            segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~'));

    private static IEnumerable<KeyValuePair<string, string>> Flatten(IReadOnlyDictionary<string, string[]> fields) =>
        fields.SelectMany(field => field.Value.Select(value => KeyValuePair.Create(field.Key, value)));

    private sealed record CapturedRequest(string Method, string Path, Dictionary<string, string[]> Headers, Dictionary<string, string[]> Query,
        Dictionary<string, string> RouteData);
    private sealed record WaitPlan(string Method, string Path, string Template, IReadOnlyList<PreparedWaitDelivery> Deliveries);
}
