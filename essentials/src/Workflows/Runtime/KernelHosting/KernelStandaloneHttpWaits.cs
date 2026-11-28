using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Foundation.Hosting;
using Kernel.Graph.Exceptions;
using Kernel.Native;

namespace Elsa.Workflows.Runtime.KernelHosting;

/// <summary>Reconstructs only the wait keys and request committed by the authorized standalone HTTP source.</summary>
internal sealed class KernelStandaloneHttpWaits(KernelServing serving)
{
    internal const string AuthorityConflict = "actor-http-wait-authority-conflict";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    internal bool CanAccept(RuntimeStandaloneIngressSource source) => Read(source) is { } plan && AllowsNew(plan);

    internal async ValueTask DeliverAsync(IPreparedRootStartHandoff foundation, RuntimePostCommitOutboxItem item, CancellationToken token)
    {
        if (item.SubscriptionDelivery is not { Recipients.Count: > 0 } || !PreparedStandaloneHttp.HasVerifiedEmptyWaits(item) ||
            item.StandaloneIngress is not { } source || Read(source) is not { } plan)
            throw new NotSupportedException("The accepted standalone HTTP source has no qualified detached wait plan.");
        var operation = RuntimeDeliveryFingerprint.Compute("elsa.runtime.foundation-http-wait-source.v1", source.OutboxItemId);
        // Runtime accepted this detached plan. A later authored publication cannot revoke it. The provider
        // independently checks every matched wait against the captured provenance before committing the cohort.
        try
        {
            _ = await foundation.DeliverWaitsAsync(operation, source.Fingerprint, plan.Deliveries, allowNew: true, token);
        }
        catch (PreparedWaitAuthorityConflictException error)
        {
            throw new RuntimePostCommitDeliveryException(PostCommitFailureKind.Permanent, AuthorityConflict,
                "The accepted HTTP source is blocked by an incompatible Actor workflow wait.", error);
        }
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
            !source.CanonicalPayload.TryGetProperty("submittedIntent", out var intent)) return null;
        try
        {
            if (saved.Deserialize<RuntimeIngressWaitCandidate>(Web) is not { Input: { } input, ObservedWaits.Count: 0 } candidate ||
                candidate.Metadata.GetValueOrDefault(RuntimeMetadataKeys.StandaloneHttpExactPath) is not { } path ||
                input.Deserialize<CapturedRequest>() is not { Method.Length: > 0, Headers: not null, RouteData: not null } request ||
                !SafeRelativePath(path) ||
                intent.GetProperty("method").GetString() != request.Method ||
                !string.Equals(request.Path, path, StringComparison.OrdinalIgnoreCase) ||
                intent.GetProperty("path").GetString() is not { } publicPath || !publicPath.StartsWith('/') ||
                !publicPath.TrimEnd('/').EndsWith("/" + path, StringComparison.Ordinal) ||
                intent.GetProperty("rawQuery").GetString() is not { } rawQuery ||
                !TryReadBody(intent, request.Body, out var body) ||
                source.CanonicalPayload.GetProperty("trigger").GetProperty("configuration").GetProperty("path").GetString() is not { } route)
                return null;
            var template = HttpRouteTemplate.Parse(route);
            var names = HttpFields.RouteValues(request.RouteData);
            if (!template.FullyBound || !SameConcretePath(template, path, names)) return null;

            var model = new HttpRequest(request.Method, publicPath, body)
            {
                Headers = HttpFields.RequestHeaders(Flatten(request.Headers)),
                Query = HttpFields.Query(KernelHttpRequestHandler.QueryPairs(rawQuery))
            };
            if (ResolvedPaths(path) is not { } variants) return null;
            var literal = HttpRouteMatcher.Literal(request.Method, path).Key;
            var deliveries = new List<PreparedWaitDelivery> { new(literal, JsonSerializer.Serialize(model)) };
            var matches = template.Segments.Any(segment => segment.Kind == HttpRouteSegmentKind.Parameter)
                ? HttpRoutes.WaitKey(request.Method, template, names)
                : null;
            var suffix = matches is null ? null : HttpRoutes.PreparedWaitSuffix(template.Text);
            foreach (var variant in variants)
            {
                var key = HttpRoutes.ResolvedKey(request.Method, variant);
                // A stamped alternate is the same template for a different case-sensitive parameter value.
                // An unstamped or differently stamped alternate could belong to another template's literal.
                deliveries.Add(new(key, key == matches ? JsonSerializer.Serialize(model with { RouteValues = names }) : null, suffix));
            }
            // The existing hashed alias can coincide with a literal '#<digest>' activity. A resolved key
            // which cannot be checked as a concrete path is likewise unsafe for a new source.
            return deliveries.Any(delivery => delivery.Key.Contains('#')) || !PreparedWaitDelivery.FitsEnvelope(deliveries)
                ? null
                : new(request.Method, "/" + path, template.Text, deliveries);
        }
        catch (Exception error) when (error is JsonException or HttpRouteTemplateException or HttpFieldsException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private static bool TryReadBody(JsonElement intent, string? text, out JsonNode? body)
    {
        body = null;
        if (text is null) return true;
        if (!intent.GetProperty("utf8Body").GetBoolean() ||
            !MediaTypeHeaderValue.TryParse(intent.GetProperty("contentType").GetString(), out var contentType) ||
            contentType.MediaType is not { } mediaType) return false;
        if (mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            // The ordinary body model retains UTF-8 text only. Refuse a different declared decoder
            // before accepting an Actor source rather than hand the wait replacement characters.
            if (KernelHttpRequestHandler.EncodingOf(contentType.CharSet?.Trim('"'))?.CodePage != Encoding.UTF8.CodePage) return false;
            body = JsonValue.Create(text);
            return true;
        }

        if (!KernelHttpRequestHandler.IsJson(mediaType)) return false;
        body = JsonNode.Parse(text);
        return true;
    }

    private static IReadOnlyList<string>? ResolvedPaths(string path)
    {
        // Historical route literals are lower case while historical parameter values retain case. Inspect
        // every possible resolved key without consulting today's publication. Bound the alias probes distinctly
        // from the payload copies (one literal input and at most one attested resolved input).
        var variants = new List<string> { "" };
        foreach (var segment in path.Split('/'))
        {
            var lower = segment.ToLowerInvariant();
            var count = variants.Count;
            if (segment != lower && 2 * count >= PreparedWaitDelivery.MaxKeys) return null;
            for (var index = 0; index < count; index++)
            {
                var parent = variants[index];
                variants[index] = parent + "/" + segment;
                if (lower != segment) variants.Add(parent + "/" + lower);
            }
        }
        return variants;
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

    private sealed record CapturedRequest(string Method, string Path, string? Body, Dictionary<string, string[]> Headers,
        Dictionary<string, string> RouteData);
    private sealed record WaitPlan(string Method, string Path, string Template, IReadOnlyList<PreparedWaitDelivery> Deliveries);
}
