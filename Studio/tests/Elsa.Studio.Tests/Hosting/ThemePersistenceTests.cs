using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Elsa.Studio.Hosting.Themes;

namespace Elsa.Studio.Tests.Hosting;

public sealed class ThemePersistenceTests
{
    private const string Base = "/_elsa/theme-store";

    [Theory]
    [InlineData("snapshot-20261027")]
    [InlineData("collection.data")]
    public async Task A_json_named_source_link_loads_its_collection_when_the_target_has_another_extension(string targetName)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) =>
        {
            var target = Path.Combine(root, targetName);
            File.WriteAllText(target, new JsonObject
            {
                ["themes"] = new JsonArray(Definition("linked-source", "Linked source"))
            }.ToJsonString());
            var source = Path.Combine(root, "themes.json");
            if (OperatingSystem.IsWindows()) File.Copy(target, source, overwrite: true);
            else if (!File.Exists(source)) File.CreateSymbolicLink(source, target);
            options.ThemesDirectory = "themes.json";
            options.ThemesMode = StudioThemeSourceMode.DirectoryOnly;
            options.DefaultThemeId = "linked-source";
        });

        var response = await Read(host);
        Assert.Contains("linked-source", response["directoryThemeIds"]!.AsArray().Select(id => id!.GetValue<string>()));
        Assert.Equal("Linked source", (string?)Theme(response, "linked-source")["name"]);
        Assert.Equal("linked-source", (string?)response["defaultThemeId"]);
    }

    [Theory]
    [InlineData(".", "studio-theme-store.json")]
    [InlineData("state", "state/themes.json")]
    public async Task A_source_directory_containing_the_managed_store_does_not_adopt_it(string source, string store)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) =>
        {
            options.ThemesDirectory = source;
            options.StoreFile = store;
            options.ThemesMode = StudioThemeSourceMode.DirectoryOnly;
            Directory.CreateDirectory(Path.Combine(root, source));
        });
        await Save(host, Definition("managed-only", "Managed only"));
        var response = await Read(host);
        Assert.Empty(response["directoryThemeIds"]!.AsArray());
        Assert.Equal("Managed only", (string?)Theme(response, "managed-only")["name"]);
        using var defaultResponse = await host.Client.PutAsJsonAsync(Base + "/default", new { themeId = "managed-only" });
        Assert.Equal(HttpStatusCode.BadRequest, defaultResponse.StatusCode);
        using var deleted = await host.Client.DeleteAsync(Base + "/themes/managed-only");
        deleted.EnsureSuccessStatusCode();
        Assert.Empty((await Read(host))["themes"]!.AsArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Case_aliases_cannot_reclassify_managed_state_as_directory_content(bool filenameAlias)
    {
        bool aliases = false;
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) =>
        {
            var directory = Directory.CreateDirectory(Path.Combine(root, "state")).FullName;
            aliases = Directory.Exists(Path.Combine(root, "STATE"));
            options.ThemesDirectory = aliases ? "STATE" : "state";
            options.StoreFile = "state/themes.json";
            options.ThemesMode = StudioThemeSourceMode.DirectoryOnly;
            if (aliases && filenameAlias && !File.Exists(Path.Combine(directory, "Themes.json")))
                File.WriteAllText(Path.Combine(directory, "Themes.json"), """{"themes":[]}""");
        });
        // On a case-sensitive volume these are distinct paths; the ordinary no-copy contract still applies.
        await Save(host, Definition("managed-only", "Managed only"));
        Assert.Empty((await Read(host))["directoryThemeIds"]!.AsArray());
        using var deleted = await host.Client.DeleteAsync(Base + "/themes/managed-only");
        deleted.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Physical_aliases_cannot_reclassify_managed_state_as_directory_content(bool fileLink)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) =>
        {
            var state = Directory.CreateDirectory(Path.Combine(root, "state")).FullName;
            options.StoreFile = "state/themes.json";
            options.ThemesMode = StudioThemeSourceMode.DirectoryOnly;
            options.ThemesDirectory = "sources";
            if (OperatingSystem.IsWindows()) options.ThemesDirectory = "state"; // No link-creation privilege required on Windows.
            else if (fileLink)
            {
                Directory.CreateDirectory(Path.Combine(root, "sources"));
                var link = Path.Combine(root, "sources", "alias.json");
                if (new FileInfo(link).LinkTarget is null) File.CreateSymbolicLink(link, Path.Combine(state, "themes.json"));
            }
            else if (!Directory.Exists(Path.Combine(root, "sources"))) Directory.CreateSymbolicLink(Path.Combine(root, "sources"), state);
        });
        await Save(host, Definition("managed-only", "Managed only"));
        Assert.Empty((await Read(host))["directoryThemeIds"]!.AsArray());
        using var deleted = await host.Client.DeleteAsync(Base + "/themes/managed-only");
        deleted.EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("BuiltInsOnly")]
    [InlineData("DirectoryOnly")]
    [InlineData("AddToBuiltIns")]
    public async Task Unrelated_visibility_writes_preserve_shadowed_managed_state_without_adopting_directory_definitions(string mode)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) => Configure(options, root, mode));
        await Save(host, Definition("shared-theme", "Original managed"));
        var original = Stored(host)["themes"]![0]!.DeepClone();
        WriteDirectory(host);
        var merged = await Read(host);
        Assert.Equal("Directory wins", (string?)Theme(merged, "shared-theme")["name"]);
        using var visibility = await host.Client.PutAsJsonAsync(Base + "/themes/drift/visibility", new { enabled = true });
        visibility.EnsureSuccessStatusCode();
        var persisted = Stored(host);
        Assert.Single(persisted["themes"]!.AsArray());
        Assert.True(JsonNode.DeepEquals(original, persisted["themes"]![0]));
        Assert.False(persisted.AsObject().ContainsKey("themesMode"));
        Assert.False(persisted.AsObject().ContainsKey("directoryThemeIds"));
        Assert.Empty(persisted["assets"]!.AsArray());
        File.Delete(SourceFile(host));
        var removed = await Read(host);
        Assert.Equal("Original managed", (string?)Theme(removed, "shared-theme")["name"]);
        Assert.DoesNotContain(removed["themes"]!.AsArray(), theme => (string?)theme!["id"] == "directory-only");
        Assert.Empty(removed["directoryThemeIds"]!.AsArray());
    }

    [Theory]
    [InlineData("save")]
    [InlineData("import")]
    [InlineData("duplicate")]
    [InlineData("default")]
    [InlineData("delete")]
    public async Task Explicit_mutations_touch_only_their_intended_managed_state(string operation)
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) => Configure(options, root));
        await Save(host, Definition("shared-theme", "Original managed"));
        await Save(host, Definition("delete-theme", "Delete managed"));
        var original = Theme(Stored(host), "shared-theme").DeepClone();
        WriteDirectory(host);
        HttpResponseMessage? response = null;
        try
        {
            if (operation == "save") await Save(host, Definition("new-managed", "New managed"));
            if (operation == "import") response = await host.Client.PostAsJsonAsync(Base + "/import", new JsonObject
            {
                ["version"] = 1, ["themes"] = new JsonArray(Definition("new-managed", "Imported"))
            });
            if (operation == "duplicate") response = await host.Client.PostAsJsonAsync(Base + "/themes/directory-only/duplicate", new { name = "Chosen copy" });
            if (operation == "default") response = await host.Client.PutAsJsonAsync(Base + "/default", new { themeId = "directory-only" });
            if (operation == "delete") response = await host.Client.DeleteAsync(Base + "/themes/delete-theme");
            response?.EnsureSuccessStatusCode();
        }
        finally { response?.Dispose(); }
        var stored = Stored(host);
        Assert.True(JsonNode.DeepEquals(original, Theme(stored, "shared-theme")));
        Assert.DoesNotContain(stored["themes"]!.AsArray(), theme => (string?)theme!["id"] == "directory-only");
        if (operation is "save" or "import") Assert.NotNull(Theme(stored, "new-managed"));
        if (operation == "duplicate") Assert.NotNull(Theme(stored, "chosen-copy"));
        if (operation == "default") Assert.Equal("directory-only", (string?)stored["defaultThemeId"]);
        if (operation == "delete") Assert.DoesNotContain(stored["themes"]!.AsArray(), theme => (string?)theme!["id"] == "delete-theme");
        File.Delete(SourceFile(host));
        Assert.Equal("Original managed", (string?)Theme(await Read(host), "shared-theme")["name"]);
    }

    [Fact]
    public async Task Explicit_save_of_a_directory_id_is_a_deliberate_managed_definition_while_source_precedence_stays_intact()
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) => Configure(options, root));
        WriteDirectory(host);
        await Save(host, Definition("directory-only", "Explicit managed choice"));
        Assert.Equal("Directory only", (string?)Theme(await Read(host), "directory-only")["name"]);
        Assert.Single(Stored(host)["themes"]!.AsArray());
        Assert.Equal("Explicit managed choice", (string?)Theme(Stored(host), "directory-only")["name"]);
        File.Delete(SourceFile(host));
        Assert.Equal("Explicit managed choice", (string?)Theme(await Read(host), "directory-only")["name"]);
    }

    [Fact]
    public async Task Source_owned_delete_refuses_instead_of_reporting_a_deletion_that_the_next_read_reverses()
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) => Configure(options, root));
        await Save(host, Definition("shared-theme", "Original managed"));
        WriteDirectory(host);
        var before = await File.ReadAllBytesAsync(StoreFile(host));
        var sourceBefore = await File.ReadAllBytesAsync(SourceFile(host));
        using var response = await host.Client.DeleteAsync(Base + "/themes/shared-theme");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("configured directory", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await File.ReadAllBytesAsync(StoreFile(host)));
        Assert.Equal(sourceBefore, await File.ReadAllBytesAsync(SourceFile(host)));
    }

    [Fact]
    public async Task A_projected_empty_default_does_not_erase_the_original_managed_default_on_an_unrelated_write()
    {
        await using var host = await ThemeHost.StartAsync("ThemeManagement", configure: (options, root) => Configure(options, root, "BuiltInsOnly"));
        await Save(host, Definition("managed-default", "Stored default"));
        var document = Stored(host);
        document["defaultThemeId"] = "managed-default";
        await File.WriteAllTextAsync(StoreFile(host), document.ToJsonString());
        Assert.Equal("", (string?)((await Read(host))["defaultThemeId"]));
        using var visibility = await host.Client.PutAsJsonAsync(Base + "/themes/drift/visibility", new { enabled = true });
        visibility.EnsureSuccessStatusCode();
        Assert.Equal("managed-default", (string?)Stored(host)["defaultThemeId"]);
    }

    private static void Configure(StudioThemeStoreOptions options, string root, string mode = "AddToBuiltIns")
    {
        options.StoreFile = "state/themes.json";
        options.ThemesDirectory = "sources/themes.json";
        options.ThemesMode = Enum.Parse<StudioThemeSourceMode>(mode);
        Directory.CreateDirectory(Path.Combine(root, "sources"));
        var source = Path.Combine(root, "sources", "themes.json");
        if (!File.Exists(source)) File.WriteAllText(source, """{"themes":[]}""");
    }
    private static string StoreFile(ThemeHost host) => Path.Combine(host.Root, "state", "themes.json");
    private static string SourceFile(ThemeHost host) => Path.Combine(host.Root, "sources", "themes.json");
    private static JsonObject Stored(ThemeHost host) => JsonNode.Parse(File.ReadAllText(StoreFile(host)))!.AsObject();
    private static JsonObject Theme(JsonObject response, string id) => response["themes"]!.AsArray().Single(theme => (string?)theme!["id"] == id)!.AsObject();
    private static void WriteDirectory(ThemeHost host) => File.WriteAllText(SourceFile(host), new JsonObject
    {
        ["themes"] = new JsonArray(Definition("shared-theme", "Directory wins"), Definition("directory-only", "Directory only"))
    }.ToJsonString());
    private static async Task Save(ThemeHost host, JsonObject theme)
    {
        using var response = await host.Client.PutAsJsonAsync(Base + "/themes/" + (string)theme["id"]!, theme);
        response.EnsureSuccessStatusCode();
    }
    private static async Task<JsonObject> Read(ThemeHost host)
    {
        using var response = await host.Client.GetAsync(Base);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }
    internal static JsonObject Definition(string id, string name)
    {
        var palette = new JsonObject();
        foreach (var token in new[] { "primary", "primaryForeground", "secondary", "secondaryForeground", "accent", "accentForeground",
                     "success", "successForeground", "warning", "warningForeground", "danger", "dangerForeground", "background", "foreground",
                     "card", "cardForeground", "muted", "mutedForeground", "border", "input", "sidebar", "sidebarForeground", "sidebarActive", "sidebarActiveForeground", "ring" })
            palette[token] = "#ffffff";
        palette["chartColors"] = new JsonArray("#ffffff", "#ffffff", "#ffffff", "#ffffff", "#ffffff");
        return new JsonObject
        {
            ["id"] = id, ["name"] = name, ["source"] = "custom", ["version"] = 1, ["enabled"] = true, ["published"] = true,
            ["modes"] = new JsonObject { ["light"] = palette.DeepClone(), ["dark"] = palette.DeepClone() }
        };
    }
}
