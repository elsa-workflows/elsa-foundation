using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Comparison;
using Elsa.Api.Compatibility.Testing.Http;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.OpenApi;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

public sealed class ActivitiesDesignCompatibilityTests
{
    [Fact]
    public void Activities_design_approval_registry_is_exact_and_deterministic()
    {
        var approvals = LoadApprovals();

        Assert.Equal(88, approvals.Length);
        Assert.Equal(44, approvals.Select(approval => approval.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(approvals, approval => Assert.Equal("Elsa.Activities.Design.Api", approval.Owner));

        var expectedOrder = approvals
            .OrderBy(approval => approval.Endpoint, StringComparer.Ordinal)
            .ThenBy(approval => approval.Method, StringComparer.Ordinal)
            .ThenBy(approval => approval.Case, StringComparer.Ordinal)
            .ThenBy(approval => approval.Facet, StringComparer.Ordinal)
            .ThenBy(approval => approval.Expected, StringComparer.Ordinal)
            .ThenBy(approval => approval.Actual, StringComparer.Ordinal)
            .ThenBy(approval => approval.Reverse)
            .ToArray();
        Assert.Equal(expectedOrder.Select(ApprovalIdentity), approvals.Select(ApprovalIdentity));

        foreach (var group in approvals.GroupBy(approval => approval.Key, StringComparer.Ordinal))
            Assert.Equal([false, true], group.Select(approval => approval.Reverse).Order());
    }

    [Fact]
    public void Duplicate_approval_is_rejected()
    {
        var (before, after, approval) = StatusMutation();
        var result = CompatibilityComparer.CompareBidirectional(before, after,
            [approval, approval with { Reverse = true }, approval]);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.InvalidApprovals, issue => issue.StartsWith("Duplicate approved difference:", StringComparison.Ordinal));
    }

    [Fact]
    public void Unused_stale_approval_is_rejected()
    {
        var evidence = StatusObservation(200);
        var approval = Approval("/design/activities/stale", "GET", "stale", CompatibilityFacet.Status, "200", "403");
        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = [evidence] },
            new CompatibilityEvidenceSet { Http = [evidence] },
            [approval]);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.InvalidApprovals, issue => issue.StartsWith("Unused approved difference:", StringComparison.Ordinal));
    }

    [Fact]
    public void No_op_approval_is_rejected_as_unused()
    {
        var evidence = StatusObservation(200);
        var approval = Approval(evidence.Endpoint.Route.Value, evidence.Endpoint.Method.Value, evidence.Case,
            CompatibilityFacet.Status, "200", "200");
        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = [evidence] },
            new CompatibilityEvidenceSet { Http = [evidence] },
            [approval]);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.InvalidApprovals, issue => issue.StartsWith("Unused approved difference:", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_facet_approval_is_rejected_as_unused()
    {
        var (before, after, _) = StatusMutation();
        var approval = Approval("/design/activities/orders", "GET", "status-change", "not-a-consumed-facet", "200", "403");
        var result = CompatibilityComparer.Compare(before, after, [approval]);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.InvalidApprovals, issue => issue.StartsWith("Unused approved difference:", StringComparison.Ordinal));
        Assert.Contains(result.Deltas, delta => delta.Facet == CompatibilityFacet.Status);
    }

    [Fact]
    public void Wrong_expected_or_actual_approval_is_rejected()
    {
        var (before, after, approval) = StatusMutation();
        var result = CompatibilityComparer.CompareBidirectional(before, after,
            [approval with { Expected = "401" }, approval with { Reverse = true, Actual = "401" }]);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.InvalidApprovals, issue => issue.StartsWith("Unused approved difference:", StringComparison.Ordinal));
        Assert.NotEmpty(result.Deltas);
    }

    [Fact]
    public void One_sided_approval_is_rejected_by_the_reverse_replay()
    {
        var (before, after, approval) = StatusMutation();
        var result = CompatibilityComparer.CompareBidirectional(before, after, [approval]);

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Deltas, delta => delta.Facet == CompatibilityFacet.Status && delta.Expected == "403" && delta.Actual == "200");
    }

    [Fact]
    public void Exact_two_sided_approval_pair_is_consumed()
    {
        var (before, after, approval) = StatusMutation();
        var result = CompatibilityComparer.CompareBidirectional(before, after, [approval, approval with { Reverse = true }]);

        Assert.True(result.IsCompatible, string.Join(Environment.NewLine, result.Failures));
    }

    [Theory]
    [InlineData(CompatibilityFacet.Route)]
    [InlineData(CompatibilityFacet.Method)]
    [InlineData(CompatibilityFacet.Binding)]
    [InlineData(CompatibilityFacet.Json)]
    [InlineData(CompatibilityFacet.Status)]
    [InlineData(CompatibilityFacet.MediaTypes)]
    [InlineData(CompatibilityFacet.Headers)]
    [InlineData(CompatibilityFacet.ProblemDetails)]
    [InlineData(CompatibilityFacet.PagingFiltering)]
    [InlineData(CompatibilityFacet.Streaming)]
    [InlineData(CompatibilityFacet.TerminalState)]
    [InlineData(CompatibilityFacet.Body)]
    public void Frozen_http_oracle_detects_every_consumed_facet_mutation(string facet)
    {
        var before = LoadHttp();
        var candidate = before[0];
        var mutated = facet switch
        {
            CompatibilityFacet.Route => candidate with { Endpoint = new EndpointIdentity("/mutated", candidate.Endpoint.Method.Value) },
            CompatibilityFacet.Method => candidate with { Endpoint = new EndpointIdentity(candidate.Endpoint.Route.Value, "OPTIONS") },
            CompatibilityFacet.Binding => candidate with { Binding = "mutated" },
            CompatibilityFacet.Json => candidate with { Json = "{\"mutated\":true}" },
            CompatibilityFacet.Status => candidate with { StatusCode = 599 },
            CompatibilityFacet.MediaTypes => candidate with { ContentType = "application/mutated" },
            CompatibilityFacet.Headers => candidate with { Headers = new Dictionary<string, string> { ["x-mutated"] = "true" } },
            CompatibilityFacet.ProblemDetails => candidate with { ProblemDetails = "{\"mutated\":true}" },
            CompatibilityFacet.PagingFiltering => candidate with { PagingFiltering = "mutated" },
            CompatibilityFacet.Streaming => candidate with { Streaming = "mutated" },
            CompatibilityFacet.TerminalState => candidate with { TerminalState = "Mutated" },
            _ => candidate with { Body = "mutated" }
        };
        var after = before.Select((observation, index) => index == 0 ? mutated : observation).ToArray();

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = after });

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Deltas, delta => delta.Facet == facet);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("method")]
    [InlineData("operationId")]
    [InlineData("tags")]
    [InlineData("security")]
    [InlineData("parameters")]
    [InlineData("requestBody")]
    [InlineData("responses")]
    [InlineData("mediaTypes")]
    [InlineData("schemas")]
    public void Frozen_openapi_oracle_detects_every_consumed_operation_mutation(string facet)
    {
        var before = LoadOpenApi();
        var candidate = before.Operations[0];
        var mutated = facet switch
        {
            "route" => candidate with { Endpoint = new EndpointIdentity("/mutated", candidate.Endpoint.Method.Value) },
            "method" => candidate with { Endpoint = new EndpointIdentity(candidate.Endpoint.Route.Value, "OPTIONS") },
            "operationId" => candidate with { OperationId = "mutated" },
            "tags" => candidate with { Tags = "[\"mutated\"]" },
            "security" => candidate with { Security = "[{\"mutated\":[]}]" },
            "parameters" => candidate with { Parameters = "[\"mutated\"]" },
            "requestBody" => candidate with { RequestBody = "{\"mutated\":true}" },
            "responses" => candidate with { Responses = "{\"599\":{}}" },
            "mediaTypes" => candidate with { MediaTypes = "[\"application/mutated\"]" },
            _ => candidate with { Schemas = "{\"mutated\":true}" }
        };
        var after = new OpenApiEvidenceDocument(before.Operations.Select((operation, index) => index == 0 ? mutated : operation).ToArray());

        var result = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { OpenApi = before },
            new CompatibilityEvidenceSet { OpenApi = after });

        Assert.False(result.IsCompatible);
        Assert.True(result.Deltas.Count > 0);
    }

    [Fact]
    public void Malformed_query_supplement_detects_an_unapproved_response_mutation()
    {
        var before = BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(
            BaselineDirectory(),
            "activities-design-query-binding-fastendpoints.json"));
        var after = before.Select((observation, index) => index == 0
            ? observation with { StatusCode = 200 }
            : observation).ToArray();

        var comparison = CompatibilityComparer.Compare(
            new CompatibilityEvidenceSet { Http = before },
            new CompatibilityEvidenceSet { Http = after });

        Assert.False(comparison.IsCompatible);
        Assert.Contains(comparison.Deltas, delta => delta.Facet == CompatibilityFacet.Status);
    }

    private static HttpCompatibilityObservation[] LoadHttp() =>
        BaselineFile.Load<HttpCompatibilityObservation[]>(Path.Join(BaselineDirectory(), "activities-design-http-fastendpoints.json"));

    private static OpenApiEvidenceDocument LoadOpenApi() =>
        BaselineFile.Load<OpenApiEvidenceDocument>(Path.Join(BaselineDirectory(), "activities-design-openapi-fastendpoints.json"));

    private static ApprovedDifference[] LoadApprovals() =>
        BaselineFile.Load<ApprovedDifference[]>(Path.Join(BaselineDirectory(), "activities-design-approved-differences.json"));

    private static (CompatibilityEvidenceSet Before, CompatibilityEvidenceSet After, ApprovedDifference Approval) StatusMutation()
    {
        var beforeObservation = StatusObservation(200);
        var afterObservation = beforeObservation with { StatusCode = 403 };
        var before = new CompatibilityEvidenceSet { Http = [beforeObservation] };
        var after = new CompatibilityEvidenceSet { Http = [afterObservation] };
        return (before, after, Approval("/design/activities/orders", "GET", "status-change", CompatibilityFacet.Status, "200", "403"));
    }

    private static HttpCompatibilityObservation StatusObservation(int statusCode) => new()
    {
        Endpoint = new EndpointIdentity("/design/activities/orders", "GET"),
        Case = "status-change",
        StatusCode = statusCode
    };

    private static ApprovedDifference Approval(
        string endpoint,
        string method,
        string @case,
        string facet,
        string expected,
        string actual) =>
        new()
        {
            Endpoint = endpoint,
            Method = method,
            Case = @case,
            Facet = facet,
            Expected = expected,
            Actual = actual,
            Owner = "Elsa.Activities.Design.Api",
            Reason = "T025 mutation bite",
            FollowUp = "#1373"
        };

    private static string ApprovalIdentity(ApprovedDifference approval) =>
        $"{approval.Key}|reverse={approval.Reverse}";

    private static string BaselineDirectory() => Path.Join(AppContext.BaseDirectory, "Baselines");
}
