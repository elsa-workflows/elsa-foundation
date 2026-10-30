using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Elsa.Studio.Hosting.Nuplane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Admin;
using Nuplane.Reconciliation;

namespace Elsa.Studio.Tests.Administration;

public sealed class PackageAssetTests
{
    internal const string PackageId = "Acme.StudioAdmin";

    [Fact]
    public async Task Replacing_admitted_results_in_a_host_filter_releases_response_permits()
    {
        var replaced = 0;
        await using var host = await AdministrationHost.StartAsync(nativeStore: true, mapAdministration: endpoints =>
            endpoints.MapGroup("").AddEndpointFilter(async (context, next) =>
            {
                var result = await next(context);
                if (!context.HttpContext.Request.Query.ContainsKey("replace")) return result;
                // Refusals cannot stand in for a successful admitted payload in this ownership regression.
                Assert.False(result is IStatusCodeHttpResult { StatusCode: >= 400 });
                Interlocked.Increment(ref replaced);
                return Results.NoContent();
            }).MapElsaStudioNuplane());
        Pack(host, "1.0.0", "admitted package payload");
        await Reconcile(host);
        var path = $"/_content/{PackageId}/probe.txt";
        using (var initial = await host.Client.GetAsync(path))
        {
            initial.EnsureSuccessStatusCode();
            Assert.Equal("admitted package payload", await initial.Content.ReadAsStringAsync());
        }
        for (var index = 0; index < 4; index++)
        {
            using var response = await host.Client.GetAsync(path + "?replace=true");
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        Assert.Equal(4, replaced);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var ordinary = await host.Client.GetAsync(path, timeout.Token);
        ordinary.EnsureSuccessStatusCode();
        Assert.Equal("admitted package payload", await ordinary.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Native_package_bytes_preserve_mime_ranges_and_conditional_revalidation()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        Pack(host, "1.0.0", "package asset payload");
        await Reconcile(host);
        using var full = await host.Client.GetAsync($"/_content/{PackageId}/probe.txt");
        full.EnsureSuccessStatusCode();
        Assert.Equal("package asset payload", await full.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", full.Content.Headers.ContentType!.MediaType);
        Assert.True(full.Headers.CacheControl!.NoCache);
        Assert.NotNull(full.Headers.ETag);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/_content/{PackageId}/probe.txt");
        request.Headers.Range = new RangeHeaderValue(0, 6);
        using var range = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal("package", await range.Content.ReadAsStringAsync());
        using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/_content/{PackageId}/probe.txt");
        conditional.Headers.IfNoneMatch.Add(full.Headers.ETag!);
        using var unchanged = await host.Client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Oversized_published_asset_is_refused_by_preflight()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        Pack(host, "1.0.0", null, oversized: true);
        await Reconcile(host);
        using var response = await host.Client.GetAsync($"/_content/{PackageId}/probe.txt");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("16777216-byte", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("%2e%2e/outside.txt")]
    [InlineData("%2e%2e%2foutside.txt")]
    [InlineData("nested/%2e%2e/%2e%2e/outside.txt")]
    public async Task Traversal_never_delivers_outside_content(string path)
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        Pack(host, "1.0.0", "inside");
        await Reconcile(host);
        await File.WriteAllTextAsync(Path.Combine(host.Root, "outside.txt"), "outside-sentinel");
        using var response = await host.Client.GetAsync($"/_content/{PackageId}/{path}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("outside-sentinel", await response.Content.ReadAsStringAsync());
    }

    internal static void Pack(AdministrationHost host, string version, string? payload, bool oversized = false, string? manifest = null, string manifestPath = "elsa-package.json", string? assembly = null)
    {
        var feed = Directory.CreateDirectory(Path.Combine(host.Root, "feed")).FullName;
        using var archive = ZipFile.Open(Path.Combine(feed, PackageId + "." + version + ".nupkg"), ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(PackageId + ".nuspec").Open()))
            writer.Write($"<package><metadata><id>{PackageId}</id><version>{version}</version><authors>Studio qualification</authors><description>Owned fixture</description></metadata></package>");
        if (manifest is not null)
        {
            using var writer = new StreamWriter(archive.CreateEntry(manifestPath).Open());
            writer.Write(manifest);
        }
        if (assembly is not null) archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, assembly + ".dll"), "lib/net10.0/" + assembly + ".dll");
        using var stream = archive.CreateEntry("staticwebassets/probe.txt").Open();
        if (oversized)
        {
            var chunk = new byte[1024 * 1024];
            for (var index = 0; index < 16; index++) stream.Write(chunk);
            stream.WriteByte(0);
        }
        else stream.Write(Encoding.UTF8.GetBytes(payload!));
    }

    internal static async Task Reconcile(AdministrationHost host)
    {
        var outcome = await host.App.Services.GetRequiredService<INuplaneAdminOperations>().TriggerReconcileAsync(CancellationToken.None);
        Assert.Equal(ManualReconcileOutcomeCode.Completed, outcome.OutcomeCode);
        Assert.False(outcome.RunResult!.IsDegraded);
    }
}
