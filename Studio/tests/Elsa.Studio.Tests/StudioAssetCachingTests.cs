using System.Net;
using Elsa.Studio.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Operational;
using Nuplane.Reconciliation;

namespace Elsa.Studio.Tests;

/// <summary>
/// Stable-URL entry points import content-hashed chunks that a rebuild deletes, so every served asset must be revalidated
/// before reuse — see <see cref="StudioAssetCaching"/>.
/// </summary>
public sealed class StudioAssetCachingTests : IAsyncLifetime
{
    private const string HostAssemblyId = "Acme.Studio.Host";
    private const string PackageId = "Acme.Studio.Module";
    private const string ModuleEntry = "studio/modules/acme/module.js";
    private const string ModuleSource = "export const register = () => {};";
    private const string HostModuleUrl = "/" + ModuleEntry;
    private const string HostContentModuleUrl = "/_content/" + HostAssemblyId + "/" + ModuleEntry;
    private const string PackageModuleUrl = "/_content/" + PackageId + "/" + ModuleEntry;
    private const string SpaRouteUrl = "/workflows/definitions";
    private readonly string _root = Path.Join(Path.GetTempPath(), $"elsa-asset-caching-{Guid.NewGuid():N}");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private string WebRoot => Path.Join(_root, "wwwroot");
    private string PackageInstallPath => Path.Join(_root, "packages", PackageId);

    [Theory]
    [InlineData(HostModuleUrl)]
    [InlineData(HostContentModuleUrl)] // A module's static web assets, as the manifest addresses them.
    [InlineData(PackageModuleUrl)] // Served by the Nuplane package endpoint.
    [InlineData("/studio/index.html")]
    [InlineData(SpaRouteUrl)] // SPA fallback to studio/index.html.
    public async Task ServedAssetsMustBeRevalidated(string path)
    {
        using var response = await _client.GetAsync(path);

        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoCache, $"Expected Cache-Control: no-cache on {path}.");
        Assert.NotNull(response.Content.Headers.LastModified);
    }

    [Theory]
    [InlineData(HostModuleUrl)]
    [InlineData(HostContentModuleUrl)]
    [InlineData(PackageModuleUrl)]
    [InlineData(SpaRouteUrl)]
    public async Task RevalidatingAnUnchangedAssetIsANotModified(string path)
    {
        using var first = await _client.GetAsync(path);
        using var revalidation = new HttpRequestMessage(HttpMethod.Get, path);
        revalidation.Headers.IfModifiedSince = first.Content.Headers.LastModified;

        using var second = await _client.SendAsync(revalidation);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    public async Task InitializeAsync()
    {
        WriteFile(Path.Join(WebRoot, ModuleEntry), ModuleSource);
        WriteFile(Path.Join(WebRoot, "_content", HostAssemblyId, ModuleEntry), ModuleSource);
        WriteFile(Path.Join(WebRoot, "studio", "index.html"), "<!doctype html>");
        WriteFile(Path.Join(PackageInstallPath, "staticwebassets", ModuleEntry), ModuleSource);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = _root,
            WebRootPath = WebRoot
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<INuplaneAdminOperations>(new SinglePackageNuplaneAdmin(PackageId, PackageInstallPath));

        _app = builder.Build();
        _app.UseStudioStaticFiles();
        _app.MapNuplaneStaticWebAssets();
        _app.MapStudioFallback();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void WriteFile(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private sealed class SinglePackageNuplaneAdmin(string packageId, string installPath) : INuplaneAdminOperations
    {
        // Only PackageId and InstallPath matter to the asset endpoint; the rest is placeholder.
        public Task<ActivePackagesSnapshot> GetPackagesAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var package = new ActivePackage(
                PackageId: packageId,
                Version: "1.0.0",
                FeedName: "feed",
                SourceName: "source",
                InstallPath: installPath,
                ActivatedAtUtc: now,
                ActivationCorrelationId: "correlation",
                GraphId: "graph",
                GraphGenerationId: "generation",
                PackageRole: ActivePackageRole.Root,
                RootPackageIds: [],
                DependencyOfPackageIds: [],
                Discoverable: true);
            return Task.FromResult(new ActivePackagesSnapshot(now, now, [package], "correlation"));
        }

        public Task<OperationalStateSnapshot> GetStateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ManualReconcileOutcome> TriggerReconcileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
