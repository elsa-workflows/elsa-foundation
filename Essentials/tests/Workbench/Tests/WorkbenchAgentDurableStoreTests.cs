using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// #3441 on the stock Workbench: the durable Weaver session store is opt-in. The stock Development shell does not run it,
/// and listing the feature with its provider in the shell configuration composes it with SQLite. Ordinary shells select
/// common effects; the durable overlay replaces that selection with EF effects. Public support discovery validates the
/// actual selected store/feature composition, and the Agent API serves the migrated database. Restart recovery itself is
/// proved by the persistence tests and the package witness's restart inventory.
/// </summary>
public sealed class WorkbenchAgentDurableStoreTests : IDisposable
{
    private const string Feature = "FoundationAgentEntityFrameworkCore";
    private const string CommonEffects = "FoundationAgentEffects";
    private const string EfEffects = "FoundationAgentEfEffects";
    private const string AdminUsername = "admin";
    private const string AdminPassword = "Password123!";

    private readonly string _databaseFolder = Directory.CreateTempSubdirectory("workbench-agent-records-").FullName;

    public void Dispose()
    {
        if (Directory.Exists(_databaseFolder))
            Directory.Delete(_databaseFolder, recursive: true);
    }

    [Fact]
    public async Task Stock_development_shell_selects_common_effects_and_enables_all_host_privileges()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);

        var catalog = (await workbench.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        Assert.True(catalog.TryGetValue(Feature, out var entry) && entry.SourceKind == "runtime", $"{Feature} is not in the runtime catalog.");
        Assert.False(entry!.Enabled, $"{Feature} must stay opt-in; the stock shell enabled it.");
        Assert.True(catalog.TryGetValue(CommonEffects, out var common) && common.Runs, "The common effects feature does not run.");
        Assert.True(catalog.TryGetValue(EfEffects, out var durable) && durable.SourceKind == "runtime", "The EF effects feature is not in the runtime catalog.");
        Assert.False(durable!.Enabled, "EF effects must stay opt-in with the durable store.");

        using var client = await SignedInClientAsync(workbench);
        await AssertSupportResolvesAsync(client);
        var features = (await client.GetFromJsonAsync<JsonNode>("/modularity/features"))!["features"]!.AsArray();
        var selected = features.Single(feature => (string?)feature!["id"] == CommonEffects)!;
        Assert.Equal("available", (string?)selected["availability"]!["status"]);

        var bootstrap = await client.GetFromJsonAsync<JsonNode>("/_elsa/agent/bootstrap");
        var privileges = bootstrap!["data"]!["privileges"]!;
        Assert.True((bool)privileges["administrative"]!);
        Assert.True((bool)privileges["irrecoverable"]!);
        Assert.True((bool)privileges["activation"]!);
    }

    [Fact]
    public async Task Production_keeps_common_effects_and_the_durable_store_opt_in()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Production);

        var catalog = (await workbench.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        Assert.True(catalog.TryGetValue(CommonEffects, out var common) && common.Runs, "Production does not run common effects.");
        Assert.True(catalog.TryGetValue(Feature, out var store) && store.SourceKind == "runtime", "The durable store is not in the runtime catalog.");
        Assert.False(store!.Enabled, "Production enabled the opt-in durable store.");
        Assert.True(catalog.TryGetValue(EfEffects, out var effects) && effects.SourceKind == "runtime", "The EF effects feature is not in the runtime catalog.");
        Assert.False(effects!.Enabled, "Production enabled EF effects without the durable store.");
        // This fixture binds HTTP only. Production's secure sign-in cookie is deliberately not relaxed for a test;
        // authenticated policy/bootstrap coverage here belongs to the Development instance above.
    }

    [Fact]
    public async Task Configured_sqlite_store_composes_migrates_and_serves_the_owner_list()
    {
        var database = Path.Combine(_databaseFolder, "agent-records.db");
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development, EnableDurableStore(database));

        var catalog = (await workbench.ReadFeatureCatalogAsync()).ToDictionary(feature => feature.Id, StringComparer.Ordinal);
        Assert.True(catalog.TryGetValue(Feature, out var entry) && entry.Runs, $"{Feature} does not run. Host output:{Environment.NewLine}{workbench.Output}");
        Assert.True(catalog.TryGetValue(EfEffects, out var effects) && effects.Runs, "The durable overlay did not select EF effects.");
        Assert.False(catalog[CommonEffects].Enabled, "The durable overlay must replace, rather than add to, common effects.");
        // The store protects its payloads with the Secrets feature it depends on.
        Assert.True(catalog.TryGetValue("Secrets", out var secrets) && secrets.Runs, "Secrets does not run.");

        // The owner list is answered by the store's own query, so it needs the migrated SQLite tables. The in-memory
        // default would answer too, which is why the database file must also exist and hold the migrated schema.
        using var client = await SignedInClientAsync(workbench);
        await AssertSupportResolvesAsync(client);
        using var list = await client.GetAsync("/_elsa/agent/sessions?take=1");
        Assert.True(list.StatusCode == HttpStatusCode.OK, $"The session list failed with {(int)list.StatusCode}: {await list.Content.ReadAsStringAsync()}");
        Assert.NotNull((await list.Content.ReadFromJsonAsync<JsonNode>())!["data"]!["items"]!.AsArray());
        Assert.True(File.Exists(database) && new FileInfo(database).Length > 0, "The configured SQLite database was not created and migrated.");
    }

    /// <summary>
    /// Lists the durable store, with SQLite at <paramref name="database"/>, in the copied stock Development shell file, as an
    /// operator enables an opt-in feature. Its effects variation replaces the common selection; everything else remains.
    /// </summary>
    private static Action<string> EnableDurableStore(string database) => contentRoot =>
    {
        var path = Path.Combine(contentRoot, "shells.json");
        var document = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        })!;
        var features = document["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        Assert.False(features.ContainsKey(Feature), $"The stock shell already lists {Feature}.");
        Assert.True(features.Remove(CommonEffects), "The stock shell does not select common effects.");
        features[Feature] = new JsonObject { ["Provider"] = "Sqlite", ["ConnectionString"] = $"Data Source={database};Pooling=False" };
        features[EfEffects] = new JsonObject();
        File.WriteAllText(path, document.ToJsonString());
    };

    private static async Task AssertSupportResolvesAsync(HttpClient client)
    {
        // The real support source resolves through AgentRegistrationValidator, so a wrong or duplicate effects
        // selection fails here even if a feature name appears in a configuration file or the runtime catalog.
        using var response = await client.GetAsync("/_elsa/agent/support");
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Agent support discovery failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.NotNull((await response.Content.ReadFromJsonAsync<JsonNode>())!["data"]);
    }

    private static async Task<HttpClient> SignedInClientAsync(WorkbenchProcess workbench)
    {
        var client = new HttpClient { BaseAddress = workbench.Client.BaseAddress };
        try
        {
            using var login = await client.PostAsync("/_elsa/identity/login", Json(new JsonObject { ["username"] = AdminUsername, ["password"] = AdminPassword }));
            Assert.True(login.IsSuccessStatusCode, $"Admin sign-in failed with {(int)login.StatusCode}: {await login.Content.ReadAsStringAsync()}");
            var token = await client.GetFromJsonAsync<JsonNode>("/_elsa/identity/token");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (string)token!["accessToken"]!);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
}
