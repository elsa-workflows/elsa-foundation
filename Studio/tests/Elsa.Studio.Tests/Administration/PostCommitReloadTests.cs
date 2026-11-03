using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.Tests.Administration;

public sealed class PostCommitReloadTests
{
    [Theory]
    [InlineData("features")]
    [InlineData("feeds")]
    [InlineData("retention")]
    public async Task A_reload_failure_reports_that_the_configuration_was_already_saved(string kind)
    {
        var failing = new RejectReloadProvider();
        await using var host = await AdministrationHost.StartAsync(managedFeatures: true,
            beforeStudio: builder => ((IConfigurationBuilder)builder.Configuration).Add(new RejectReloadSource(failing)));
        JsonObject body;
        string url;
        var method = HttpMethod.Post;
        if (kind == "features")
        {
            var catalog = await host.Client.GetFromJsonAsync<JsonObject>("/modularity/features");
            body = new JsonObject
            {
                ["revision"] = catalog!["revision"]!.DeepClone(),
                ["features"] = new JsonArray(catalog["features"]!.AsArray().Select(feature => (JsonNode)new JsonObject
                {
                    ["id"] = feature!["id"]!.DeepClone(), ["enabled"] = feature["enabled"]!.DeepClone(),
                    ["configuration"] = feature["configuration"]!.DeepClone()
                }).ToArray())
            };
            body["features"]!.AsArray().Single(feature => (string?)feature!["id"] == "Configured")!["configuration"]!["Credential"] = "committed-before-reload-failure";
            url = "/modularity/features/apply";
        }
        else if (kind == "feeds")
        {
            body = new JsonObject { ["name"] = "saved-feed", ["directoryPath"] = "feed", ["includeAll"] = true, ["includePatterns"] = new JsonArray("*") };
            url = "/_elsa/module-management/feeds";
        }
        else
        {
            body = new JsonObject { ["keepNewestInactiveVersionsPerPackage"] = 0 };
            url = "/_elsa/module-management/retention-policy";
            method = HttpMethod.Put;
        }
        failing.Armed = true;
        HttpResponseMessage? response = null;
        var failure = await Record.ExceptionAsync(async () =>
        {
            using var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
            response = await host.Client.SendAsync(request);
        });
        using (response)
        {
            var file = kind == "features" ? host.ShellsFile : Path.Combine(host.Root, "nuplane-management.json");
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
            if (kind == "features") Assert.Equal("committed-before-reload-failure", (string?)saved["CShells"]!["Shells"]!["Authoring"]!["Features"]!["Configured"]!["Credential"]);
            if (kind == "feeds") Assert.Equal("saved-feed", (string?)saved["Nuplane"]!["Setup"]!["Feeds"]![0]!["Name"]);
            if (kind == "retention") Assert.Equal(0, (int)saved["Studio"]!["PackageManagement"]!["RetentionPolicy"]!["KeepNewestInactiveVersionsPerPackage"]!);
            Assert.Null(failure);
            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var error = await response.Content.ReadAsStringAsync();
            Assert.Contains("was saved", error);
            Assert.Contains("reload failed", error);
            Assert.DoesNotContain("committed-before-reload-failure", error);
            Assert.DoesNotContain("private-provider-sentinel", error);
        }
    }

    private sealed class RejectReloadSource(RejectReloadProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }

    private sealed class RejectReloadProvider : ConfigurationProvider
    {
        public bool Armed { get; set; }
        public override void Load()
        {
            if (Armed) throw new InvalidOperationException("private-provider-sentinel");
        }
    }
}
