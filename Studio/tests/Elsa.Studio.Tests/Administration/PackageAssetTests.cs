using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Elsa.Studio.Hosting.Nuplane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nuplane.Admin;
using Nuplane.Loading;
using Nuplane.Loading.Hosting.Builder;
using Nuplane.Reconciliation;

namespace Elsa.Studio.Tests.Administration;

public sealed class PackageAssetTests
{
    internal const string PackageId = "Acme.StudioAdmin";

    [Fact]
    public async Task Loaded_assembly_urls_read_the_owning_native_package_when_its_id_differs()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true, loadPackages: true,
            configureNuplane: nuplane => nuplane.AutoloadPackages(loading => loading.WithDefaultLoadMode(PackageLoadMode.Collectible)));
        Pack(host, "1.0.0", "assembly-owned protected payload", assembly: "First");
        await Reconcile(host);
        var packages = await host.App.Services.GetRequiredService<IPackageAssemblyCatalog>().GetPackagedAssembliesAsync(CancellationToken.None);
        var owner = Assert.Single(packages, package => package.PackageId == PackageId);
        Assert.Contains(owner.Assemblies, assembly => assembly.GetName().Name == "First");

        using var response = await host.Client.GetAsync("/_content/First/probe.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("assembly-owned protected payload", await response.Content.ReadAsStringAsync());
        Assert.NotNull(response.Headers.ETag);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ambiguous_assembly_urls_refuse_both_loaded_owner_and_direct_package_id_collisions(bool directIdCollision)
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true, loadPackages: true,
            configureNuplane: nuplane => nuplane.AutoloadPackages(loading => loading.WithDefaultLoadMode(PackageLoadMode.Collectible)));
        Pack(host, "1.0.0", "first-owner-sentinel", assembly: "First");
        Pack(host, "1.0.0", "other-owner-sentinel", assembly: directIdCollision ? null : "First",
            packageId: directIdCollision ? "First" : "Other.Package");
        await Reconcile(host);
        var loaded = await host.App.Services.GetRequiredService<IPackageAssemblyCatalog>().GetPackagedAssembliesAsync(CancellationToken.None);
        Assert.Equal(directIdCollision ? 1 : 2, loaded.Count(package => package.Assemblies.Any(assembly => assembly.GetName().Name == "First")));

        using var response = await host.Client.GetAsync("/_content/First/probe.txt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("owner-sentinel", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unloaded_assembly_alias_and_unknown_name_refuse_without_losing_direct_package_urls()
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true);
        Pack(host, "1.0.0", "direct package payload", assembly: "First");
        await Reconcile(host);
        Assert.Empty(await host.App.Services.GetRequiredService<IPackageAssemblyCatalog>().GetPackagedAssembliesAsync(CancellationToken.None));

        using var unloaded = await host.Client.GetAsync("/_content/First/probe.txt");
        using var unknown = await host.Client.GetAsync("/_content/Unknown.Assembly/probe.txt");
        using var direct = await host.Client.GetAsync($"/_content/{PackageId}/probe.txt");

        Assert.Equal(HttpStatusCode.NotFound, unloaded.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.OK, direct.StatusCode);
        Assert.Equal("direct package payload", await direct.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_or_removed_loaded_owners_cannot_deliver_a_retained_install(bool removed)
    {
        FrozenAssemblyCatalog? observed = null;
        await using var host = await AdministrationHost.StartAsync(nativeStore: true, loadPackages: true,
            configureNuplane: nuplane => nuplane.AutoloadPackages(loading => loading.WithDefaultLoadMode(PackageLoadMode.Collectible)),
            afterStudio: builder =>
            {
                var original = builder.Services.Last(service => service.ServiceType == typeof(IPackageAssemblyCatalog));
                builder.Services.Replace(ServiceDescriptor.Singleton<IPackageAssemblyCatalog>(provider =>
                    observed = new FrozenAssemblyCatalog((IPackageAssemblyCatalog)(original.ImplementationInstance ??
                        original.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)))));
            });
        Pack(host, "1.0.0", "retired-owner-sentinel", assembly: "First");
        await Reconcile(host);
        var catalog = host.App.Services.GetRequiredService<IPackageAssemblyCatalog>();
        var captured = await catalog.GetPackagedAssembliesAsync(CancellationToken.None);
        Assert.Contains(Assert.Single(captured).Assemblies, assembly => assembly.GetName().Name == "First");
        observed!.Snapshot = captured;
        var old = Assert.Single((await host.App.Services.GetRequiredService<INuplaneAdminOperations>().GetPackagesAsync(CancellationToken.None)).Packages);
        // A stale assembly claim must refuse even when ignoring it would select a different direct-ID owner.
        Pack(host, "1.0.0", "wrong-direct-owner-sentinel", assembly: "Second", packageId: "First");
        if (removed) File.Delete(Path.Combine(host.Root, "feed", PackageId + ".1.0.0.nupkg"));
        else Pack(host, "2.0.0", "current-owner-payload", assembly: "First");
        await Reconcile(host);
        Assert.True(Directory.Exists(old.InstallPath));

        using var response = await host.Client.GetAsync("/_content/First/probe.txt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("retired-owner-sentinel", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("wrong-direct-owner-sentinel", await response.Content.ReadAsStringAsync());
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_package_bytes_preserve_mime_ranges_and_conditional_revalidation(bool assemblyAlias)
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: true, loadPackages: assemblyAlias,
            configureNuplane: nuplane =>
            {
                if (assemblyAlias) nuplane.AutoloadPackages(loading => loading.WithDefaultLoadMode(PackageLoadMode.Collectible));
            });
        Pack(host, "1.0.0", "package asset payload", assembly: assemblyAlias ? "First" : null);
        await Reconcile(host);
        var path = assemblyAlias ? "/_content/First/probe.txt" : $"/_content/{PackageId}/probe.txt";
        using var full = await host.Client.GetAsync(path);
        full.EnsureSuccessStatusCode();
        Assert.Equal("package asset payload", await full.Content.ReadAsStringAsync());
        Assert.Equal("text/plain", full.Content.Headers.ContentType!.MediaType);
        Assert.True(full.Headers.CacheControl!.NoCache);
        Assert.NotNull(full.Headers.ETag);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Range = new RangeHeaderValue(0, 6);
        using var range = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal("package", await range.Content.ReadAsStringAsync());
        using var conditional = new HttpRequestMessage(HttpMethod.Get, path);
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

    internal static void Pack(AdministrationHost host, string version, string? payload, bool oversized = false, string? manifest = null, string manifestPath = "elsa-package.json", string? assembly = null, string packageId = PackageId)
    {
        var feed = Directory.CreateDirectory(Path.Combine(host.Root, "feed")).FullName;
        using var archive = ZipFile.Open(Path.Combine(feed, packageId + "." + version + ".nupkg"), ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(packageId + ".nuspec").Open()))
            writer.Write($"<package><metadata><id>{packageId}</id><version>{version}</version><authors>Studio qualification</authors><description>Owned fixture</description></metadata></package>");
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
        Assert.False(outcome.RunResult!.IsDegraded, JsonSerializer.Serialize(outcome.RunResult));
    }

    private sealed class FrozenAssemblyCatalog(IPackageAssemblyCatalog inner) : IPackageAssemblyCatalog
    {
        public IReadOnlyList<PackageAssemblies>? Snapshot { get; set; }
        public Task<IReadOnlyList<PackageAssemblies>> GetPackagedAssembliesAsync(CancellationToken cancellationToken) =>
            Snapshot is { } snapshot ? Task.FromResult(snapshot) : inner.GetPackagedAssembliesAsync(cancellationToken);
        public async Task<PackageAssemblies?> GetPackagedAssembliesAsync(string packageId, CancellationToken cancellationToken) =>
            (await GetPackagedAssembliesAsync(cancellationToken)).FirstOrDefault(package => package.PackageId == packageId);
    }
}
