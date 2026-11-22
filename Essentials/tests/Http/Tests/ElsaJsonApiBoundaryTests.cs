using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Api.AspNetCore;
using Elsa.Serialization.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MinimalEndpoints;
using Xunit;

namespace Elsa.Http.Tests;

public sealed class ElsaJsonApiBoundaryTests
{
    [Theory]
    [InlineData("/api/raw", 200)]
    [InlineData("/api/problem", 409)]
    [InlineData("/api/fault", 500)]
    [InlineData("/api/query?at=2026-01-15T09%3A15%3A00", 400)]
    [InlineData("/api/auth", 401)]
    public async Task Owned_success_and_failure_responses_have_the_common_headers(string route, int status)
    {
        await using var host = await Host.Start();
        using var response = await host.Client.GetAsync(route);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        if (response.Content.Headers.ContentType is { } contentType) Assert.Equal("application/json", contentType.MediaType);
    }

    [Fact]
    public async Task Authorization_forbidden_is_covered_before_the_operation_runs()
    {
        await using var host = await Host.Start();
        host.Client.DefaultRequestHeaders.Add("X-Probe-User", "present");
        using var response = await host.Client.GetAsync("/api/auth");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
    }

    [Theory]
    [InlineData("2026-01-15T09:15:00Z")]
    [InlineData("2026-01-15T09:15:00+05:00")]
    [InlineData("2026-07-15T09:15:00-03:00")]
    public async Task Query_dates_with_an_explicit_offset_bind_and_return_UTC(string value)
    {
        await using var host = await Host.Start();
        using var response = await host.Client.GetAsync("/api/query?at=" + Uri.EscapeDataString(value));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.EndsWith("+00:00", document.RootElement.GetProperty("at").GetString());
        Assert.Equal(DateTimeOffset.Parse(value), document.RootElement.GetProperty("at").GetDateTimeOffset());
    }

    [Fact]
    public async Task A_supplied_offsetless_query_names_the_code_path_and_target_without_echoing_the_value()
    {
        await using var host = await Host.Start();
        using var response = await host.Client.GetAsync("/api/query?at=2026-01-15T09%3A15%3A00");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("json-datetime-offset-missing", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("$.at", document.RootElement.GetProperty("path").GetString());
        Assert.Equal(typeof(DateTimeOffset).FullName, document.RootElement.GetProperty("targetType").GetString());
        Assert.DoesNotContain("2026-01-15", document.RootElement.GetRawText());
        using var absent = await host.Client.GetAsync("/api/query");
        Assert.Equal(HttpStatusCode.OK, absent.StatusCode);
    }

    [Theory]
    [InlineData("{\"at\":\"2026-01-15T09:15:00\"}", "$.at")]
    [InlineData("{\"nested\":{\"at\":\"2026-01-15T09:15:00\"}}", "$.nested.at")]
    public async Task Typed_body_dates_are_refused_with_the_stable_code_and_JSON_path(string body, string path)
    {
        await using var host = await Host.Start();
        using var response = await host.Client.PostAsync("/api/body", new StringContent(body, Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("json-datetime-offset-missing", text);
        Assert.Contains(path, text);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Date_looking_strings_inside_carried_JSON_remain_raw_data()
    {
        await using var host = await Host.Start();
        const string raw = "{ \"at\":\"2026-01-15T09:15:00\", \"n\":1.0 }";
        using var response = await host.Client.PostAsync("/api/body", new StringContent("{\"data\":" + raw + "}", Encoding.UTF8, "application/json"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(raw, document.RootElement.GetProperty("data").GetRawText());
    }

    [Theory]
    [InlineData("/workflow/html", "text/html", false)]
    [InlineData("/workflow/vendor", "application/vnd.elsa-test+json", false)]
    [InlineData("/assets/vendor", "application/vnd.elsa-test+json", true)]
    [InlineData("/api/events", "text/event-stream", true)]
    public async Task Authored_content_and_assets_preserve_their_media_type(string route, string mediaType, bool nosniff)
    {
        await using var host = await Host.Start();
        using var response = await host.Client.GetAsync(route);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(nosniff, response.Headers.Contains("X-Content-Type-Options"));
    }

    private sealed class Host(WebApplication app) : IAsyncDisposable
    {
        internal HttpClient Client { get; } = app.GetTestClient();
        internal static async Task<Host> Start()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddRouting().AddElsaEndpoints();
            builder.Services.AddAuthentication("probe").AddScheme<AuthenticationSchemeOptions, ProbeAuthentication>("probe", _ => { });
            builder.Services.AddAuthorization(options => options.AddPolicy("denied", policy => policy.RequireAssertion(_ => false)));
            builder.Services.ConfigureHttpJsonOptions(options => JsonBaseline.Configure(options.SerializerOptions));
            var app = builder.Build();
            app.UseAuthentication(); app.UseAuthorization();
            var api = app.MapEndpointGroup("Tests.JsonBoundary", WireContext());
            api.MapOperation<ApiTemporalProbe>("GET", "/api/query", "Query", EndpointBodyMode.None, null, typeof(ApiTemporalProbe), 200, null, Write);
            api.MapOperation<ApiTemporalProbe>("POST", "/api/body", "Body", EndpointBodyMode.Required, null, typeof(ApiTemporalProbe), 200, null, Write);
            api.MapUnboundOperation("GET", "/api/auth", "Auth", typeof(string), 200, null, context => context.Response.WriteAsync("must not run")).RequireAuthorization("denied");
            api.MapUnboundOperation("GET", "/api/problem", "Problem", null, 409, null,
                context => Results.Problem(statusCode: 409, title: "Owned conflict").ExecuteAsync(context));
            api.MapUnboundOperation("GET", "/api/fault", "Fault", null, 500, null, _ => throw new InvalidOperationException("private failure"));
            api.MapUnboundOperation("GET", "/api/events", "Events", null, 200, null,
                context => Results.Content("event: ready\ndata: {}\n\n", "text/event-stream").ExecuteAsync(context), successContentType: "text/event-stream");
            app.MapGet("/api/raw", () => Results.Json(new { orderTotal = 1 })).WithElsaJsonResponseHeaders();
            app.MapGet("/workflow/html", () => Results.Content("<b>Hello</b>", "text/html"));
            app.MapGet("/workflow/vendor", () => Results.Content("{\"value\":1}", "application/vnd.elsa-test+json"));
            app.MapGet("/assets/vendor", () => Results.Content("{\"value\":1}", "application/vnd.elsa-test+json")).WithElsaJsonResponseHeaders(normalizeContentType: false);
            await app.StartAsync();
            return new Host(app);
        }
        private static Task Write(HttpContext context, ApiTemporalProbe request, CancellationToken token) =>
            context.Response.WriteAsJsonAsync(request, WireContext().ApiTemporalProbe, cancellationToken: token);
        private static ApiBoundaryJsonContext WireContext()
        {
            var options = JsonBaseline.CreateCopy(); StrictDateTimeConverters.Configure(options, apiMode: true);
            return new ApiBoundaryJsonContext(options);
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.DisposeAsync(); }
    }
    private sealed class ProbeAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.ContainsKey("X-Probe-User")
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "probe")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
    }
}

public sealed record ApiTemporalProbe(DateTimeOffset? At = null, ApiTemporalNested? Nested = null, JsonElement? Data = null);
public sealed record ApiTemporalNested(DateTime? At);
[JsonSerializable(typeof(ApiTemporalProbe))]
internal partial class ApiBoundaryJsonContext : JsonSerializerContext;
