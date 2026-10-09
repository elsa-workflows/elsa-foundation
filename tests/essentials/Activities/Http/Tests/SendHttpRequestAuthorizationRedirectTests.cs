using Elsa.Activities.Testing;
using Xunit;
using static Elsa.Activities.Http.Tests.SendHttpRequestTestSupport;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Spec 188, T115 (research R17, spec Assumptions): <c>SendHttpRequest</c> goes through the named client that
/// <see cref="ActivitiesHttpFeature"/> configures with default <c>HttpActivityOptions</c> (automatic redirects on), so a
/// redirect is followed by the real redirect logic of that primary handler. Over loopback servers on OS-assigned
/// ports, the original request carries the <c>Authorization</c> value and the redirect target, same-origin or
/// cross-origin, never receives it. Scope: the two 302 redirects below on this runtime's HTTP stack.
/// </summary>
public sealed class SendHttpRequestAuthorizationRedirectTests : IAsyncDisposable
{
    private const string StartPath = "/start";
    private const string LandingPath = "/landing";

    private readonly string _authorizationValue = $"Bearer canary-{Guid.NewGuid():N}";
    private readonly LoopbackHttpServer _origin;
    private readonly LoopbackHttpServer _otherOrigin = new(_ => (200, null));
    private readonly WorkflowExecutionHarness _harness = NewBuilder().Build(ActivityExecutionId);

    public SendHttpRequestAuthorizationRedirectTests()
    {
        // The origin answers /start with a 302 to wherever the test pointed it, and /landing (same-origin) with 200.
        _origin = new LoopbackHttpServer(request =>
            request.Path == StartPath ? (302, RedirectTarget) : (200, null));
    }

    private Uri? RedirectTarget { get; set; }

    [Fact]
    public async Task SameOriginRedirect_DoesNotCarryTheAuthorizationValueToTheTarget()
    {
        RedirectTarget = new Uri(_origin.BaseAddress, LandingPath);

        await SendAndFollowRedirectAsync();

        AssertTargetReceivedRedirectedRequestWithoutAuthorization(_origin);
    }

    [Fact]
    public async Task CrossOriginRedirect_DoesNotCarryTheAuthorizationValueToTheTarget()
    {
        RedirectTarget = new Uri(_otherOrigin.BaseAddress, LandingPath);

        await SendAndFollowRedirectAsync();

        AssertTargetReceivedRedirectedRequestWithoutAuthorization(_otherOrigin);
        Assert.NotEqual(_origin.BaseAddress.Authority, _otherOrigin.BaseAddress.Authority);
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();
        await _origin.DisposeAsync();
        await _otherOrigin.DisposeAsync();
    }

    private async Task SendAndFollowRedirectAsync()
    {
        var run = await _harness.RunAsync(WorkflowExecutionHarness.NewExecutable(NewSendNode(
            url: new Uri(_origin.BaseAddress, StartPath),
            authorization: _authorizationValue)));

        run.AssertOutcomes(NodeId, "Done");
        // The original request carried the value, so its absence on the target is the client's doing.
        var original = Assert.Single(_origin.Requests, request => request.Path == StartPath);
        Assert.Equal(_authorizationValue, original.Headers["Authorization"]);
    }

    private static void AssertTargetReceivedRedirectedRequestWithoutAuthorization(LoopbackHttpServer target)
    {
        // Precondition: the redirect was followed and reached the target.
        var redirected = Assert.Single(target.Requests, request => request.Path == LandingPath);
        Assert.DoesNotContain(redirected.Headers.Keys, name => string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase));
    }
}
