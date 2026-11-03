using System.Net;
using Elsa.Studio.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Studio.Tests;

/// <summary>
/// Stable-URL entry points import content-hashed chunks that a rebuild deletes, so every served asset must be revalidated
/// before reuse through the public Hosting pipeline.
/// </summary>
public sealed class StudioAssetCachingTests : IAsyncLifetime
{
    private const string HostAssemblyId = "Acme.Studio.Host";
    private const string ModuleEntry = "studio/modules/acme/module.js";
    private const string ModuleSource = "export const register = () => {};";
    private const string HostModuleUrl = "/" + ModuleEntry;
    private const string HostContentModuleUrl = "/_content/" + HostAssemblyId + "/" + ModuleEntry;
    private const string SpaRouteUrl = "/workflows/definitions";
    private readonly string _root = Path.Join(Path.GetTempPath(), $"elsa-asset-caching-{Guid.NewGuid():N}");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private string WebRoot => Path.Join(_root, "wwwroot");

    [Theory]
    [InlineData(HostModuleUrl)]
    [InlineData(HostContentModuleUrl)] // A module's static web assets, as the manifest addresses them.
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

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = _root,
            WebRootPath = WebRoot
        });
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["CShells:Shells:Default:Features:StudioApi"] = "true" });
        builder.AddElsaStudio(options => options.ShellsFile = null);
        _app = builder.Build();
        _app.UseElsaStudio();
        _app.MapElsaStudio();

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

}
