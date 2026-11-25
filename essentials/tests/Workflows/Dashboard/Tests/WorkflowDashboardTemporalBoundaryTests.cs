using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MinimalEndpoints;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

public sealed class WorkflowDashboardTemporalBoundaryTests
{
    private const string Route = "/_elsa/workflows/dashboard/runs";

    [Theory]
    [InlineData("from")]
    [InlineData("to")]
    public async Task Offsetless_dates_are_refused_by_the_actual_endpoint_before_dispatch(string parameter)
    {
        var service = new RecordingRunHealthService();
        using var provider = Services(service);
        var endpoint = Map(provider);
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<EndpointParameterMetadata>(), metadata =>
            metadata.Name == parameter && metadata.Source == EndpointBindingSource.Query && metadata.Type == typeof(DateTimeOffset));
        var query = Query("2026-10-28T04:15:00Z", "2026-10-28T05:15:00Z");
        query[parameter] = "2026-10-28T04:30:00";

        var response = await Invoke(provider, endpoint, query);

        Assert.Equal(StatusCodes.Status400BadRequest, response.Status);
        Assert.Equal("application/json", response.ContentType);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("json-datetime-offset-missing", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("$." + parameter, document.RootElement.GetProperty("path").GetString());
        Assert.DoesNotContain("2026-10-28", response.Body);
        Assert.Empty(service.Queries);
    }

    [Theory]
    [InlineData("2026-10-28T04:15:00Z", "2026-10-28T05:15:00Z")]
    [InlineData("2026-10-28T09:15:00+05:00", "2026-10-28T10:15:00+05:00")]
    [InlineData("2026-10-28T01:15:00-03:00", "2026-10-28T02:15:00-03:00")]
    public async Task Explicit_offsets_keep_the_same_UTC_interval_and_existing_query_fields(string from, string to)
    {
        var service = new RecordingRunHealthService();
        using var provider = Services(service);
        var endpoint = Map(provider);
        var expectedFrom = new DateTimeOffset(2026, 10, 28, 4, 15, 0, TimeSpan.Zero);
        var expectedTo = expectedFrom.AddHours(1);

        var response = await Invoke(provider, endpoint, Query(from, to));

        Assert.Equal(StatusCodes.Status200OK, response.Status);
        var dispatched = Assert.Single(service.Queries);
        Assert.Equal(expectedFrom, dispatched.From);
        Assert.Equal(expectedTo, dispatched.To);
        Assert.Equal("Europe/Amsterdam", dispatched.TimeZone);
        Assert.Equal(WorkflowRunHealthBucketSize.Hour, dispatched.Bucket);
        Assert.Equal("tenant-a", dispatched.TenantId);
        Assert.True(dispatched.IncludeTestRuns);
        using var document = JsonDocument.Parse(response.Body);
        var returnedFrom = document.RootElement.GetProperty("from").GetDateTimeOffset();
        var returnedTo = document.RootElement.GetProperty("to").GetDateTimeOffset();
        Assert.Equal(expectedFrom, returnedFrom);
        Assert.Equal(expectedTo, returnedTo);
        Assert.Equal(TimeSpan.Zero, returnedFrom.Offset);
        Assert.Equal(TimeSpan.Zero, returnedTo.Offset);
    }

    private static ServiceProvider Services(IWorkflowRunHealthService service) =>
        new ServiceCollection().AddLogging().AddRouting().AddElsaEndpoints()
            .AddSingleton(service).BuildServiceProvider();

    private static RouteEndpoint Map(IServiceProvider provider)
    {
        var routes = new TestEndpointRouteBuilder(provider);
        WorkflowsDashboardApi.MapWorkflowsDashboardApi(routes);
        return Assert.Single(routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>(),
            endpoint => endpoint.RoutePattern.RawText == Route);
    }

    private static Dictionary<string, string?> Query(string from, string to) => new()
    {
        ["from"] = from,
        ["to"] = to,
        ["timeZone"] = "Europe/Amsterdam",
        ["bucket"] = "hour",
        ["includeTestRuns"] = "true"
    };

    private static async Task<(int Status, string? ContentType, string Body)> Invoke(
        IServiceProvider provider, RouteEndpoint endpoint, Dictionary<string, string?> query)
    {
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(IdentityClaimTypes.TenantId, "tenant-a")], "probe")),
            Response = { Body = body }
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = Route;
        context.Request.QueryString = QueryString.Create(query);
        context.SetEndpoint(endpoint);
        await endpoint.RequestDelegate!(context);
        return (context.Response.StatusCode, context.Response.ContentType, Encoding.UTF8.GetString(body.ToArray()));
    }

    private sealed class RecordingRunHealthService : IWorkflowRunHealthService
    {
        internal List<WorkflowRunHealthQuery> Queries { get; } = [];

        public ValueTask<WorkflowRunHealthSnapshot> QueryAsync(WorkflowRunHealthQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return ValueTask.FromResult(WorkflowRunHealthSnapshot.Unavailable(query,
                new DateTimeOffset(2026, 10, 28, 6, 0, 0, TimeSpan.Zero)));
        }
    }
}
