using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Elsa.Studio.Tests.Hosting;

public sealed class ThemeConcurrencyTests
{
    [Fact]
    public async Task Concurrent_distinct_theme_and_visibility_edits_preserve_every_committed_change()
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement");
        var writes = Enumerable.Range(0, 20).Select(async index =>
        {
            var theme = ThemePersistenceTests.Definition($"managed-{index:000}", $"Managed {index}");
            using var response = await host.Client.PutAsJsonAsync("/_elsa/theme-store/themes/" + (string)theme["id"]!, theme);
            response.EnsureSuccessStatusCode();
        });
        var visibility = new[] { "drift", "atelier", "dusk", "signal" }.Select(async id =>
        {
            using var response = await host.Client.PutAsJsonAsync($"/_elsa/theme-store/themes/{id}/visibility", new { enabled = false });
            response.EnsureSuccessStatusCode();
        });
        var reads = Enumerable.Range(0, 10).Select(async _ =>
        {
            using var response = await host.Client.GetAsync("/_elsa/theme-store");
            response.EnsureSuccessStatusCode();
            Assert.NotNull(JsonNode.Parse(await response.Content.ReadAsStringAsync())!["themes"]);
        });
        await Task.WhenAll(writes.Concat(visibility).Concat(reads));
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(host.Root, "studio-theme-store.json")))!;
        Assert.Equal(20, persisted["themes"]!.AsArray().Count);
        Assert.Equal(20, persisted["themes"]!.AsArray().Select(theme => (string)theme!["id"]!).Distinct().Count());
        Assert.Equal(new[] { "atelier", "drift", "dusk", "signal" }, persisted["disabledBuiltInThemeIds"]!.AsArray().Select(id => (string)id!).Order().ToArray());
        Assert.Empty(Directory.EnumerateFiles(host.Root, "*.tmp"));
    }
}
