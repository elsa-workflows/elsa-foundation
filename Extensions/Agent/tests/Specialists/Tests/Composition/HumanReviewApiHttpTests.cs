using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Agent.Specialists.Review.Api;
using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Models;
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
public sealed class HumanReviewApiHttpTests : IAsyncLifetime
{
    internal const string Scheme = "EditorialTest";
    private const string Route = "/agent/reviews/run-1/review-1";
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
                new SpecialistsReviewApiFeature().ConfigureServices(services);
                services.AddSingleton<IHumanReviews>(reviews);
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(HumanReviewApi.MapHumanReviewApi);
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
            """{"contentHash":"hash","decision":"Approve","comment":"Ship it.","requestId":"request-0001"}""", HumanReviewIds.DecidePermission);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Accepted", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        var (command, decider) = Assert.Single(reviews.Decisions);
        Assert.Equal(new ReviewDecider("tenant-a", "subject-ada"), decider);
        Assert.Equal(new ReviewDecisionCommand("run-1", "review-1", "hash", ReviewDecision.Approve, "Ship it.", "request-0001"), command);
    }

    [Fact]
    public async Task An_external_sign_in_decides_as_its_canonical_Elsa_user_not_the_provider_subject()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route + "/decision")
        {
            Content = new StringContent("""{"contentHash":"hash","decision":"Approve","requestId":"request-0009"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Permissions", HumanReviewIds.DecidePermission);
        request.Headers.Add("X-External", "true");

        Assert.Equal(HttpStatusCode.OK, (await host.GetTestClient().SendAsync(request)).StatusCode);
        Assert.Equal(new ReviewDecider("tenant-a", "elsa-user-ada"), Assert.Single(reviews.Decisions).Decider);
    }

    [Theory]
    [InlineData("""{"contentHash":"hash","decision":"Approve","requestId":"request-0002","actorId":"someone-else"}""")]
    [InlineData("""{"contentHash":"hash","decision":"Approve","requestId":"request-0003","requestedBy":"admin"}""")]
    [InlineData("""{"contentHash":"hash","decision":"Publish","requestId":"request-0004"}""")]
    public async Task Caller_authored_actors_and_unknown_decisions_are_refused_before_the_service(string body)
    {
        var response = await SendAsync(HttpMethod.Post, Route + "/decision", body, HumanReviewIds.DecidePermission);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(reviews.Decisions);
    }

    [Fact]
    public async Task Missing_permissions_are_refused_and_reading_never_implies_deciding()
    {
        var decide = await SendAsync(HttpMethod.Post, Route + "/decision",
            """{"contentHash":"hash","decision":"Deny","requestId":"request-0005"}""", HumanReviewIds.ReadPermission);
        var read = await SendAsync(HttpMethod.Get, Route, null, HumanReviewIds.DecidePermission);
        var anonymous = await SendAsync(HttpMethod.Get, Route, null, null);

        Assert.Equal(HttpStatusCode.Forbidden, decide.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(reviews.Decisions);
    }

    [Fact]
    public async Task Reading_resolves_the_exact_wait_and_hides_missing_or_foreign_reviews()
    {
        var found = await SendAsync(HttpMethod.Get, Route, null, HumanReviewIds.ReadPermission);
        var missing = await SendAsync(HttpMethod.Get, "/agent/reviews/run-1/missing", null, HumanReviewIds.ReadPermission);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var view = await found.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", view.GetProperty("status").GetString());
        Assert.Equal("Moss gardens.", view.GetProperty("content").GetString());
        Assert.Equal("Draft", view.GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? body, string? permission)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (permission is not null) request.Headers.Add("X-Permissions", permission);
        return await host.GetTestClient().SendAsync(request);
    }

    private sealed class RecordingReviews : IHumanReviews
    {
        public ConcurrentQueue<(ReviewDecisionCommand Command, ReviewDecider Decider)> Decisions { get; } = new();

        public ValueTask<HumanReviewView?> GetAsync(HumanReviewReference reference, ReviewDecider caller, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(reference.ActivityExecutionId == "review-1" && caller.TenantId == "tenant-a"
                ? new HumanReviewView(reference.WorkflowExecutionId, reference.ActivityExecutionId, "Draft", JsonSerializer.SerializeToElement("Moss gardens."), null, "hash", HumanReviewStatus.Pending, null, null, null)
                : null);

        public ValueTask<ReviewDecisionReceipt> DecideAsync(ReviewDecisionCommand command, ReviewDecider decider, CancellationToken cancellationToken = default)
        {
            Decisions.Enqueue((command, decider));
            return ValueTask.FromResult(new ReviewDecisionReceipt(ReviewDecisionReceiptStatus.Accepted));
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
                new(ClaimTypes.NameIdentifier, Request.Headers.ContainsKey("X-External") ? "provider-subject" : "subject-ada"),
                new(IdentityClaimTypes.TenantId, "tenant-a"),
                new(IdentityClaimTypes.Normalized, "v1")
            };
            if (Request.Headers.ContainsKey("X-External")) claims.Add(new(IdentityClaimTypes.UserId, "elsa-user-ada"));
            claims.AddRange(permissions.ToString().Split(',').Select(permission => new Claim(IdentityClaimTypes.Permission, permission)));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, HumanReviewApiHttpTests.Scheme)), HumanReviewApiHttpTests.Scheme)));
        }
    }
}
