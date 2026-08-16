using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Elsa.Api.Compatibility.Testing.Serialization;

namespace Elsa.Api.Compatibility.Testing.Comparison;

public sealed record CompatibilityEvidenceSet
{
    public IReadOnlyList<HttpCompatibilityObservation> Http { get; init; } = [];
    public OpenApiEvidenceDocument? OpenApi { get; init; }
}

public sealed record ApprovedDifference
{
    public required string Endpoint { get; init; }
    public required string Method { get; init; }
    public required string Case { get; init; }
    public required string Facet { get; init; }
    public required string Expected { get; init; }
    public required string Actual { get; init; }
    public required string Owner { get; init; }
    public required string Reason { get; init; }
    public required string FollowUp { get; init; }

    public string Key => CompatibilityJson.Serialize(new[] { Endpoint, Method, Case, Facet, Expected, Actual });
}

public sealed record CompatibilityDelta
{
    public required string Endpoint { get; init; }
    public required string Method { get; init; }
    public required string Case { get; init; }
    public required string Facet { get; init; }
    public required string Expected { get; init; }
    public required string Actual { get; init; }
    public string Key => CompatibilityJson.Serialize(new[] { Endpoint, Method, Case, Facet, Expected, Actual });
}

public sealed record CompatibilityComparisonResult(
    IReadOnlyList<CompatibilityDelta> Deltas,
    IReadOnlyList<string> InvalidApprovals)
{
    public bool IsCompatible => Deltas.Count == 0 && InvalidApprovals.Count == 0;
    public IReadOnlyList<string> Failures => Deltas.Select(d => $"{d.Endpoint} {d.Method} [{d.Facet}] expected {d.Expected}, actual {d.Actual}")
        .Concat(InvalidApprovals).ToArray();
}

/// <summary>Compares consumed evidence one facet at a time and applies only exact approvals.</summary>
public static class CompatibilityComparer
{
    private static readonly string[] HttpFacets =
    [CompatibilityFacet.Binding, CompatibilityFacet.Json, CompatibilityFacet.Status, CompatibilityFacet.MediaTypes, CompatibilityFacet.Headers,
        CompatibilityFacet.ProblemDetails, CompatibilityFacet.PagingFiltering, CompatibilityFacet.Streaming, CompatibilityFacet.TerminalState,
        CompatibilityFacet.Body];

    public static CompatibilityComparisonResult Compare(CompatibilityEvidenceSet before, CompatibilityEvidenceSet after,
        IEnumerable<ApprovedDifference>? approvals = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var deltas = CompareHttp(before.Http, after.Http).Concat(CompareOpenApi(before.OpenApi, after.OpenApi)).ToList();
        var invalid = new List<string>();
        var approved = (approvals ?? []).ToArray();
        var valid = new Dictionary<string, ApprovedDifference>(StringComparer.Ordinal);
        foreach (var approval in approved)
        {
            var error = Validate(approval);
            if (error is not null) { invalid.Add(error); continue; }
            if (!valid.TryAdd(approval.Key, approval)) invalid.Add($"Duplicate approved difference: {approval.Key}");
        }

        var remaining = deltas.Where(delta => !valid.Remove(delta.Key, out _)).ToList();
        invalid.AddRange(valid.Values.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"Unused approved difference: {x.Key}"));
        return new(remaining.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(), invalid.Order(StringComparer.Ordinal).ToArray());
    }

    public static void AssertCompatible(CompatibilityEvidenceSet before, CompatibilityEvidenceSet after,
        IEnumerable<ApprovedDifference>? approvals = null)
    {
        var result = Compare(before, after, approvals);
        if (!result.IsCompatible) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Failures));
    }

    private static IEnumerable<CompatibilityDelta> CompareHttp(IReadOnlyList<HttpCompatibilityObservation> before,
        IReadOnlyList<HttpCompatibilityObservation> after)
    {
        var left = before.ToDictionary(Key, StringComparer.Ordinal);
        var right = after.ToDictionary(Key, StringComparer.Ordinal);
        foreach (var key in left.Keys.Intersect(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            foreach (var delta in CompareMatched(left[key], right[key]))
                yield return delta;
        }

        var unmatchedBefore = left.Where(entry => !right.ContainsKey(entry.Key))
            .Select(entry => entry.Value).OrderBy(observation => Key(observation), StringComparer.Ordinal).ToArray();
        var unmatchedAfter = right.Where(entry => !left.ContainsKey(entry.Key))
            .Select(entry => entry.Value).OrderBy(observation => Key(observation), StringComparer.Ordinal).ToArray();
        var pairedAfter = new HashSet<HttpCompatibilityObservation>();

        foreach (var oldValue in unmatchedBefore)
        {
            var candidate = unmatchedAfter
                .Where(value => !pairedAfter.Contains(value) && value.Case == oldValue.Case)
                .OrderByDescending(value => value.Endpoint.Route.Equals(oldValue.Endpoint.Route))
                .ThenByDescending(value => value.Endpoint.Method.Equals(oldValue.Endpoint.Method))
                .ThenBy(value => Key(value), StringComparer.Ordinal)
                .FirstOrDefault();
            if (candidate is null)
            {
                yield return Delta(oldValue.Endpoint, oldValue.Case, CompatibilityFacet.Route,
                    oldValue.Endpoint.Route.Value, "null");
                continue;
            }

            pairedAfter.Add(candidate);
            if (!candidate.Endpoint.Route.Equals(oldValue.Endpoint.Route))
                yield return Delta(candidate.Endpoint, oldValue.Case, CompatibilityFacet.Route,
                    oldValue.Endpoint.Route.Value, candidate.Endpoint.Route.Value);
            if (!candidate.Endpoint.Method.Equals(oldValue.Endpoint.Method))
                yield return Delta(candidate.Endpoint, oldValue.Case, CompatibilityFacet.Method,
                    oldValue.Endpoint.Method.Value, candidate.Endpoint.Method.Value);
        }

        foreach (var newValue in unmatchedAfter.Where(value => !pairedAfter.Contains(value)))
        {
            yield return Delta(newValue.Endpoint, newValue.Case, CompatibilityFacet.Route,
                "null", newValue.Endpoint.Route.Value);
        }
    }

    private static IEnumerable<CompatibilityDelta> CompareMatched(HttpCompatibilityObservation oldValue,
        HttpCompatibilityObservation newValue)
    {
        foreach (var facet in HttpFacets)
        {
            var expected = oldValue.Facet(facet);
            var actual = newValue.Facet(facet);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                yield return Delta(newValue.Endpoint, newValue.Case, facet, expected, actual);
        }
    }

    private static IEnumerable<CompatibilityDelta> CompareOpenApi(OpenApiEvidenceDocument? before, OpenApiEvidenceDocument? after)
    {
        var left = (before?.Operations ?? []).ToDictionary(x => x.Endpoint.ToString(), StringComparer.Ordinal);
        var right = (after?.Operations ?? []).ToDictionary(x => x.Endpoint.ToString(), StringComparer.Ordinal);
        foreach (var key in left.Keys.Union(right.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            left.TryGetValue(key, out var oldValue);
            right.TryGetValue(key, out var newValue);
            var identity = newValue?.Endpoint ?? oldValue!.Endpoint;
            var expected = oldValue?.Canonical ?? "null";
            var actual = newValue?.Canonical ?? "null";
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                yield return Delta(identity, "openapi", CompatibilityFacet.OpenApi, expected, actual);
        }
    }

    private static string? Validate(ApprovedDifference approval)
    {
        if (approval is null) return "Approved difference cannot be null.";
        if (new[] { approval.Endpoint, approval.Method, approval.Case, approval.Facet, approval.Owner, approval.Reason, approval.FollowUp }
            .Any(string.IsNullOrWhiteSpace)) return $"Approved difference has an empty scope or attribution: {approval.Key}";
        try { _ = new Manifests.EndpointIdentity(approval.Endpoint, approval.Method); }
        catch (ArgumentException) { return $"Approved difference has an invalid endpoint: {approval.Key}"; }
        return null;
    }

    private static string Key(HttpCompatibilityObservation observation) =>
        observation.Endpoint + "|" + observation.Case;

    private static CompatibilityDelta Delta(Manifests.EndpointIdentity endpoint, string @case, string facet,
        string expected, string actual) => new()
    {
        Endpoint = endpoint.Route.Value, Method = endpoint.Method.Value, Case = @case, Facet = facet,
        Expected = expected, Actual = actual
    };
}
