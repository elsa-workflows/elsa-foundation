using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// The Studio CORS policy as a browser sees it.
///
/// <para>
/// Exposing a response header is not the same thing as allowing a request header, and the two are easy to confuse:
/// <c>AllowAnyHeader</c> sets <c>Access-Control-Allow-Headers</c>, which governs what the browser may <em>send</em>.
/// Reading a response header from script needs <c>Access-Control-Expose-Headers</c>, and only the seven CORS-safelisted
/// response headers are readable without it.
/// </para>
///
/// <para>
/// <c>Content-Disposition</c> is not safelisted, so before #1794 the executable-artifact export
/// (<c>GET publishing/workflows/{versionId}/executable-export</c>) set a download name that no cross-origin Studio could
/// read. Nothing failed: the response was a normal 200 and the client quietly rebuilt the name from a hand-kept port of
/// this repository's own <c>CreateFileName</c>. That is the failure this test exists to prevent — a silent one, where
/// the server stops being the authority on a name it believes it owns and the two implementations drift apart.
/// </para>
/// </summary>
public sealed class WorkbenchStudioCorsTests
{
    /// <summary>
    /// One of the origins <c>Program.cs</c> falls back to when <c>Cors:AllowedOrigins</c> is not configured. The value
    /// only has to match that list — it is not the port the test host happens to bind.
    /// </summary>
    private const string StudioOrigin = "http://localhost:5089";

    [Fact]
    public async Task Studio_origin_can_read_the_content_disposition_download_name()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);

        // The root endpoint is anonymous and mapped after UseCors, so it exercises the policy without dragging
        // authentication or a published workflow into a test about response headers.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", StudioOrigin);
        using var response = await workbench.Client.SendAsync(request);

        response.EnsureSuccessStatusCode();

        Assert.True(
            response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedOrigin),
            $"The response carried no Access-Control-Allow-Origin, so the Studio policy did not run at all. Host output:{Environment.NewLine}{workbench.Output}");
        Assert.Contains(StudioOrigin, allowedOrigin);

        Assert.True(
            response.Headers.TryGetValues("Access-Control-Expose-Headers", out var exposed),
            $"The response exposed no headers, so a cross-origin Studio cannot read the export's download name. Host output:{Environment.NewLine}{workbench.Output}");
        Assert.Contains(
            exposed,
            value => value.Split(',').Any(header => header.Trim().Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// A request from an unlisted origin must not be granted the policy. Without this, a test asserting only the
    /// presence of the exposed header would still pass if the origin allow-list were widened to everything.
    /// </summary>
    [Fact]
    public async Task Unlisted_origin_is_not_granted_the_studio_policy()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", "https://studio.example.invalid");
        using var response = await workbench.Client.SendAsync(request);

        Assert.False(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            $"An unlisted origin was granted the Studio CORS policy. Host output:{Environment.NewLine}{workbench.Output}");
    }
}
