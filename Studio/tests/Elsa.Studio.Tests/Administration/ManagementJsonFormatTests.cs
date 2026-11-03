using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Elsa.Studio.Tests.Administration;

public sealed class ManagementJsonFormatTests
{
    [Theory]
    [InlineData("retention")]
    [InlineData("feeds")]
    public async Task Management_writes_accept_the_same_comment_and_trailing_comma_syntax_as_host_configuration(string kind)
    {
        await using var host = await AdministrationHost.StartAsync(beforeStudio: builder =>
            File.WriteAllText(Path.Combine(builder.Environment.ContentRootPath, "nuplane-management.json"), """
            {
              // Operator-authored configuration accepted by AddJsonFile.
              "preserve": 42,
            }
            """));
        using var response = kind == "retention"
            ? await host.Client.PutAsJsonAsync("/_elsa/module-management/retention-policy", new { keepNewestInactiveVersionsPerPackage = 0 })
            : await host.Client.PostAsJsonAsync("/_elsa/module-management/feeds", new { name = "new-feed", directoryPath = "feed", includePatterns = new[] { "*" }, includeAll = true });
        response.EnsureSuccessStatusCode();
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(host.Root, "nuplane-management.json")))!;
        Assert.Equal(42, (int)saved["preserve"]!);
        if (kind == "retention") Assert.Equal(0, (int)saved["Studio"]!["PackageManagement"]!["RetentionPolicy"]!["KeepNewestInactiveVersionsPerPackage"]!);
        else Assert.Equal("new-feed", (string?)saved["Nuplane"]!["Setup"]!["Feeds"]![0]!["Name"]);
    }
}
