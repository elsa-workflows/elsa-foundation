using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Elsa.Studio.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nuplane.Admin;
using Nuplane.Feeds.Configuration;
using Nuplane.Feeds.Setup;
using Nuplane.Sources.Directory.Builder;

namespace Elsa.Studio.Tests.Administration;

public sealed class ModuleAdministrationTests
{
    private const string Prefix = "/_elsa/module-management";

    [Theory]
    [InlineData("GET", "/registry")]
    [InlineData("POST", "/packages/upload")]
    [InlineData("DELETE", "/packages/drop-folder/probe.nupkg")]
    [InlineData("POST", "/feeds")]
    [InlineData("PUT", "/feeds/probe")]
    [InlineData("DELETE", "/feeds/probe")]
    [InlineData("POST", "/reconcile")]
    [InlineData("POST", "/prune")]
    [InlineData("PUT", "/retention-policy")]
    public async Task Every_administration_route_rejects_anonymous_requests_before_any_write(string method, string path)
    {
        await using var host = await AdministrationHost.StartAsync(beforeStudio: builder => builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Studio:Auth:Enabled"] = "true" }));
        using var request = new HttpRequestMessage(new HttpMethod(method), Prefix + path);
        if (path == "/packages/upload")
        {
            var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Package()), "package", "Acme.StudioUpload.1.0.0.nupkg");
            request.Content = form;
        }
        else if (method is "POST" or "PUT") request.Content = JsonContent.Create(new { });
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(File.Exists(Path.Combine(host.Root, "nuplane-management.json")));
        Assert.Empty(Dropped(host));
    }

    [Theory]
    [InlineData("native")]
    [InlineData("ordinary")]
    [InlineData("oversized")]
    [InlineData("linked")]
    public async Task Registry_nuspec_reads_use_native_admission_and_the_metadata_bound(string profile)
    {
        await using var host = await AdministrationHost.StartAsync(nativeStore: profile != "ordinary", configureNuplane: nuplane =>
        {
            if (profile == "ordinary") nuplane.AddDirectoryFeed("ordinary", "feed", feed => { feed.Watch = false; feed.IncludeAll(); });
        });
        PackageAssetTests.Pack(host, "1.0.0", "payload");
        await PackageAssetTests.Reconcile(host);
        var installed = Assert.Single((await host.App.Services.GetRequiredService<INuplaneAdminOperations>().GetPackagesAsync(CancellationToken.None)).Packages);
        var nuspec = Assert.Single(Directory.EnumerateFiles(installed.InstallPath, "*.nuspec"));
        if (profile == "oversized") await File.WriteAllTextAsync(nuspec, new string(' ', 1024 * 1024 + 1));
        if (profile == "linked")
        {
            var outside = Path.Combine(host.Root, "outside.nuspec");
            await File.WriteAllTextAsync(outside, "outside-metadata-sentinel");
            File.Delete(nuspec);
            File.CreateSymbolicLink(nuspec, outside);
        }
        var result = await Registry(host);
        var package = Assert.Single(result["packages"]!.AsArray())!;
        if (profile == "native")
        {
            Assert.Equal("nuspec", (string?)package["manifest"]?["kind"]);
            Assert.Contains(PackageAssetTests.PackageId, (string)package["manifest"]!["text"]!);
        }
        else
        {
            Assert.Equal("manifest-error", (string?)package["manifest"]?["kind"]);
            Assert.Contains(result["diagnostics"]!.AsArray(), item => (string?)item!["status"] == "metadata-unavailable");
            Assert.DoesNotContain("outside-metadata-sentinel", result.ToJsonString());
        }
    }

    [Fact]
    public async Task Registry_uses_public_host_composition_without_a_Web_identity()
    {
        await using var host = await AdministrationHost.StartAsync();
        var registry = await Registry(host);
        Assert.Equal(host.Root, (string?)registry["host"]?["contentRootPath"]);
        Assert.NotEqual("Elsa.Studio.Web", (string?)registry["host"]?["runtime"]);
        Assert.Equal("Supported", (string?)registry["retentionPolicy"]?["status"]);
        Assert.Null(registry["retentionPolicy"]?["keepNewestInactiveVersionsPerPackage"]);
        Assert.True((bool)registry["capabilities"]!["canManageFeeds"]!);
        Assert.True((bool)registry["capabilities"]!["canManageRetentionPolicy"]!);
    }

    [Fact]
    public async Task Upload_and_delete_use_the_installed_directory_after_a_pending_feed_edit()
    {
        await using var host = await AdministrationHost.StartAsync(beforeStudio: builder =>
            File.WriteAllText(Path.Combine(builder.Environment.ContentRootPath, "nuplane-management.json"),
                """{"preserve":"unrelated","Nuplane":{"Setup":{"Feeds":[{"Name":"local","DirectoryPath":"current","IncludeAll":true,"extra":"preserved","Directory":{"Watch":false,"Role":"DesiredAndCache"}}]}}}"""));
        using var edit = await host.Client.PutAsJsonAsync(Prefix + "/feeds/local", Feed("local", "pending"));
        edit.EnsureSuccessStatusCode();
        Assert.True((bool)JsonNode.Parse(await edit.Content.ReadAsStringAsync())!["requiresRestart"]!);
        using var upload = await Upload(host, "Acme.StudioUpload.1.0.0.nupkg", Package());
        upload.EnsureSuccessStatusCode();
        var expected = Path.Combine(host.Root, "current", "Acme.StudioUpload.1.0.0.nupkg");
        Assert.Equal(expected, (string?)JsonNode.Parse(await upload.Content.ReadAsStringAsync())!["path"]);
        Assert.True(File.Exists(expected));
        Assert.False(Directory.Exists(Path.Combine(host.Root, "pending")));
        var stored = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(host.Root, "nuplane-management.json")))!;
        Assert.Equal("unrelated", (string?)stored["preserve"]);
        Assert.Equal("preserved", (string?)stored["Nuplane"]!["Setup"]!["Feeds"]![0]!["extra"]);
        Assert.Equal("DesiredAndCache", (string?)stored["Nuplane"]!["Setup"]!["Feeds"]![0]!["Directory"]!["Role"]);
        using var deleted = await host.Client.DeleteAsync(Prefix + "/packages/drop-folder/Acme.StudioUpload.1.0.0.nupkg");
        deleted.EnsureSuccessStatusCode();
        Assert.False(File.Exists(expected));
    }

    [Theory]
    [InlineData("package.zip")]
    [InlineData("package.dll")]
    [InlineData("package")]
    [InlineData("../outside.nupkg")]
    [InlineData("nested/evil.nupkg")]
    [InlineData("nested\\evil.nupkg")]
    [InlineData("C:outside.nupkg")]
    public async Task Upload_rejects_nonpackages_and_path_qualified_names(string fileName)
    {
        await using var host = await AdministrationHost.StartAsync();
        var sentinel = Path.Combine(host.Root, "outside.nupkg");
        await File.WriteAllTextAsync(sentinel, "outside-sentinel");
        using var response = await Upload(host, fileName, Package());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("outside-sentinel", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(Dropped(host));
    }

    [Fact]
    public async Task Malformed_or_missing_uploads_do_not_leave_staged_files()
    {
        await using var host = await AdministrationHost.StartAsync();
        using var malformed = await Upload(host, "broken.nupkg", Encoding.UTF8.GetBytes("not a ZIP"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var empty = new MultipartFormDataContent();
        using var missing = await host.Client.PostAsync(Prefix + "/packages/upload", empty);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Empty(Dropped(host));
    }

    [Fact]
    public async Task Concurrent_feed_and_retention_edits_preserve_both_sections_and_reject_duplicate_without_a_write()
    {
        await using var host = await AdministrationHost.StartAsync();
        var pending = await Task.WhenAll(host.Client.PostAsJsonAsync(Prefix + "/feeds", Feed("local", "next")),
            host.Client.PutAsJsonAsync(Prefix + "/retention-policy", new { keepNewestInactiveVersionsPerPackage = 0 }));
        try { Assert.All(pending, response => response.EnsureSuccessStatusCode()); }
        finally { foreach (var response in pending) response.Dispose(); }
        var file = Path.Combine(host.Root, "nuplane-management.json");
        var before = await File.ReadAllBytesAsync(file);
        var document = JsonNode.Parse(before)!;
        Assert.Equal("local", (string?)document["Nuplane"]!["Setup"]!["Feeds"]![0]!["Name"]);
        Assert.Equal(0, (int)document["Studio"]!["PackageManagement"]!["RetentionPolicy"]!["KeepNewestInactiveVersionsPerPackage"]!);
        using var duplicate = await host.Client.PostAsJsonAsync(Prefix + "/feeds", Feed("LOCAL", "another"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(before, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task An_overlaid_feed_configuration_is_read_only_and_not_found_does_not_create_a_file()
    {
        await using (var host = await AdministrationHost.StartAsync(afterStudio: builder => builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Nuplane:Setup:Feeds:0:Name"] = "application", ["Nuplane:Setup:Feeds:0:DirectoryPath"] = "application-feed" })))
        {
            using var response = await host.Client.PostAsJsonAsync(Prefix + "/feeds", Feed("extra", "extra"));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.False(File.Exists(Path.Combine(host.Root, "nuplane-management.json")));
        }
        await using (var host = await AdministrationHost.StartAsync())
        {
            using var response = await host.Client.DeleteAsync(Prefix + "/feeds/missing");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.False(File.Exists(Path.Combine(host.Root, "nuplane-management.json")));
        }
    }

    [Fact]
    public async Task A_named_cache_directory_accepts_uploads_without_claiming_desired_state_discovery()
    {
        await using var host = await AdministrationHost.StartAsync(
            configureNuplane: nuplane => nuplane.AddDirectoryFeed("cache", "cache", feed => { feed.Role = DirectoryFeedRole.Cache; feed.Watch = false; }),
            configureAdministration: options => options.DirectoryFeedName = "cache");
        using var response = await Upload(host, "Acme.StudioUpload.1.0.0.nupkg", Package());
        response.EnsureSuccessStatusCode();
        Assert.True(File.Exists(Path.Combine(host.Root, "cache", "Acme.StudioUpload.1.0.0.nupkg")));
        await ReconcileAfterUpload(host);
        Assert.Empty((await Registry(host))["packages"]!.AsArray());
    }

    [Fact]
    public async Task A_source_only_feed_cannot_be_selected_as_a_package_resolver_drop_directory()
    {
        await Assert.ThrowsAsync<StudioHostingException>(async () =>
        {
            await using var host = await AdministrationHost.StartAsync(
                configureNuplane: nuplane => nuplane.AddDirectoryFeed("source", "source", feed => { feed.Role = DirectoryFeedRole.Desired; feed.Watch = false; feed.IncludeAll(); }),
                configureAdministration: options => options.DirectoryFeedName = "source");
        });
    }

    private static async Task<JsonObject> Registry(AdministrationHost host)
    {
        using var response = await host.Client.GetAsync(Prefix + "/registry");
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private static async Task<HttpResponseMessage> Upload(AdministrationHost host, string fileName, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "package", fileName);
        return await host.Client.PostAsync(Prefix + "/packages/upload", form);
    }

    private static IEnumerable<string> Dropped(AdministrationHost host)
    {
        var directory = Path.Combine(host.Root, "packages");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory) : [];
    }

    private static async Task ReconcileAfterUpload(AdministrationHost host)
    {
        // The accepted upload schedules its own reconciliation. Wait for that ownership to leave
        // before requiring another complete public reconciliation of the now-present cache file.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            using var response = await host.Client.PostAsync(Prefix + "/reconcile", null);
            response.EnsureSuccessStatusCode();
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            if ((string?)result["reconcile"]?["outcome"] != "Unavailable")
            {
                Assert.Equal("Completed", (string?)result["reconcile"]?["outcome"]);
                Assert.False((bool)result["reconcile"]!["isDegraded"]!);
                return;
            }
            Assert.True(DateTimeOffset.UtcNow < deadline, result.ToJsonString());
            await Task.Delay(25);
        }
    }

    private static JsonObject Feed(string name, string path) => new()
    {
        ["name"] = name, ["directoryPath"] = path, ["includeAll"] = true,
        ["includePatterns"] = new JsonArray("*"),
        ["directory"] = new JsonObject { ["watch"] = false, ["debounceWindow"] = "00:00:01" }
    };

    private static byte[] Package()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("Acme.StudioUpload.nuspec");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write("<package><metadata><id>Acme.StudioUpload</id><version>1.0.0</version><authors>tests</authors><description>Studio upload probe</description></metadata></package>");
        }
        return stream.ToArray();
    }
}
