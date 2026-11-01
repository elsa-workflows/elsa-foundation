using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Agent.Specialists.Editorial.Api;
using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Api.Capabilities;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

/// <summary>The HTTP boundary: permission gates, a server-derived actor, and no caller-authored authority.</summary>
public sealed class EditorialReviewApiHttpTests : IAsyncLifetime
{
    internal const string Scheme = "EditorialTest";
    private const string Route = "/agent/editorial-reviews/run-1/review-1";
    private readonly RecordingReviews reviews = new();
    private IHost host = null!;

    public async Task InitializeAsync()
    {
        host = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddRouting();
                services.AddAuthentication(Scheme).AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(Scheme, _ => { });
                services.AddAuthorization();
                services.AddFoundationIdentityAbstractions(options => options.NormalizedAuthenticationTypes = new HashSet<string>([Scheme], StringComparer.Ordinal));
                new ApiCapabilitiesFeature().ConfigureServices(services);
                new SpecialistsEditorialApiFeature().ConfigureServices(services);
                services.AddSingleton<IEditorialReviews>(reviews);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(EditorialReviewApi.MapEditorialReviewApi);
            });
        }).Build();
        await host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
    }

    [Fact]
    public async Task A_decision_carries_the_authenticated_subject_and_tenant_not_anything_the_caller_names()
    {
        var response = await SendAsync(HttpMethod.Post, Route + "/decision",
            """{"round":2,"articleHash":"hash","decision":"Approve","comment":"Ship it.","requestId":"request-0001"}""", EditorialReviewIds.DecidePermission);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Accepted", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        var (command, decider) = Assert.Single(reviews.Decisions);
        Assert.Equal(new EditorialDecider("tenant-a", "subject-ada"), decider);
        Assert.Equal(new EditorialDecisionCommand("run-1", "review-1", 2, "hash", EditorialDecision.Approve, "Ship it.", "request-0001"), command);
    }

    [Theory]
    [InlineData("""{"round":2,"articleHash":"hash","decision":"Approve","requestId":"request-0002","actorId":"someone-else"}""")]
    [InlineData("""{"round":2,"articleHash":"hash","decision":"Approve","requestId":"request-0003","requestedBy":"admin"}""")]
    [InlineData("""{"round":2,"articleHash":"hash","decision":"Publish","requestId":"request-0004"}""")]
    public async Task Caller_authored_actors_and_unknown_decisions_are_refused_before_the_service(string body)
    {
        var response = await SendAsync(HttpMethod.Post, Route + "/decision", body, EditorialReviewIds.DecidePermission);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(reviews.Decisions);
    }

    [Fact]
    public async Task Missing_permissions_are_refused_and_reading_never_implies_deciding()
    {
        var decide = await SendAsync(HttpMethod.Post, Route + "/decision",
            """{"round":1,"articleHash":"hash","decision":"Deny","requestId":"request-0005"}""", EditorialReviewIds.ReadPermission);
        var read = await SendAsync(HttpMethod.Get, Route, null, EditorialReviewIds.DecidePermission);
        var anonymous = await SendAsync(HttpMethod.Get, Route, null, null);

        Assert.Equal(HttpStatusCode.Forbidden, decide.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(reviews.Decisions);
    }

    [Fact]
    public async Task Reading_resolves_the_exact_wait_and_hides_missing_or_foreign_reviews()
    {
        var found = await SendAsync(HttpMethod.Get, Route, null, EditorialReviewIds.ReadPermission);
        var missing = await SendAsync(HttpMethod.Get, "/agent/editorial-reviews/run-1/missing", null, EditorialReviewIds.ReadPermission);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var view = await found.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", view.GetProperty("status").GetString());
        Assert.Equal("Moss gardens.", view.GetProperty("article").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body, string? permission)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (permission is not null) request.Headers.Add("X-Permissions", permission);
        return await host.GetTestClient().SendAsync(request);
    }

    private sealed class RecordingReviews : IEditorialReviews
    {
        public ConcurrentQueue<(EditorialDecisionCommand Command, EditorialDecider Decider)> Decisions { get; } = new();

        public ValueTask<EditorialReviewView?> GetAsync(EditorialReviewReference reference, EditorialDecider caller, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(reference.ActivityExecutionId == "review-1" && caller.TenantId == "tenant-a"
                ? new EditorialReviewView(reference.WorkflowExecutionId, reference.ActivityExecutionId, 1, "hash", "Moss gardens.", null, EditorialReviewStatus.Pending, null, null, null)
                : null);

        public ValueTask<EditorialDecisionReceipt> DecideAsync(EditorialDecisionCommand command, EditorialDecider decider, CancellationToken cancellationToken = default)
        {
            Decisions.Enqueue((command, decider));
            return ValueTask.FromResult(new EditorialDecisionReceipt(EditorialDecisionReceiptStatus.Accepted));
        }
    }

    private sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Permissions", out var permissions))
                return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "subject-ada"),
                new(IdentityClaimTypes.TenantId, "tenant-a"),
                new(IdentityClaimTypes.Normalized, "v1")
            };
            claims.AddRange(permissions.ToString().Split(',').Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, EditorialReviewApiHttpTests.Scheme)), EditorialReviewApiHttpTests.Scheme)));
        }
    }
}
