using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// #1880 built <c>EfPendingMigrationActivationGuard</c> and its <c>AddEfPendingMigrationActivationGuard()</c>
/// composition extension but deliberately left composing it into any concrete host out of scope. This proves
/// that composition for Elsa.Workbench: a real, unmodified Workbench process — the stock Development shell,
/// no environment overrides — refuses a live <c>modularity/features/apply</c> call that points an EF feature
/// at a provider name this build's <c>EfRelationalProviderBinding</c> does not recognize, and leaves the
/// stored shell configuration untouched.
/// </summary>
/// <remarks>
/// This drives the guard's provider-binding refusal (<c>Verdict.Unbindable</c>) rather than its
/// pending-migration refusal (<c>Verdict.Pending</c>). Both live in the same
/// <c>EfPendingMigrationActivationGuard.EvaluateAsync</c> and produce the same
/// <c>FeatureActivationRefusedException</c> -&gt; 409 path (see <c>ModularityFaultRenderer</c>), so this is
/// still a genuine end-to-end proof that the guard is composed into Workbench's host container and actually
/// runs during a real apply call — not merely a unit-level assertion. Unbindable is also the only verdict the
/// guard can reach under whatever migrate policy this host happens to run (it is checked before the
/// AutoMigrate early-return), so this test needs no policy override at all. Workbench ships all four provider
/// engines (Sqlite, SqlServer, Npgsql, MySql — see Elsa.Workbench.csproj), so a real provider name will not
/// do; "Oracle" is not one <c>EfRelationalProviderBinding.Select</c> knows, so it refuses the same way an
/// engine assembly that failed to load would.
/// <para>
/// The pending-migration path itself is already covered at the guard's own unit level
/// (tests/Elsa/Modularity/EntityFramework/Tests/EfPendingMigrationActivationGuardTests.cs) and is not repeated
/// here. Driving it end-to-end would additionally require Migrate:Policy=Validate for the whole default shell,
/// which — because CShells' own shell-activation schema check reads the same policy — would require every
/// already-enabled EF feature's database (identity included) to be pre-migrated before the process starts, a
/// disproportionate amount of new harness for what this composition needs to prove.
/// </para>
/// </remarks>
public sealed class WorkbenchActivationGuardCompositionTests
{
    private const string AdminUsername = "admin";
    private const string AdminPassword = "Password123!";
    private const string TargetFeature = "SecretsEntityFrameworkCore";

    [Fact]
    public async Task Refuses_enabling_an_EF_feature_whose_provider_engine_this_host_cannot_bind()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        using var client = new HttpClient { BaseAddress = workbench.Client.BaseAddress };

        await SignInAsAdminAsync(client);

        var before = await ReadCatalogAsync(client);
        var revision = (string)before["revision"]!;
        var beforeFeatures = before["features"]!.AsArray();
        var target = beforeFeatures.Single(feature => (string)feature!["id"]! == TargetFeature)!.AsObject();
        Assert.True((bool)target["enabled"]!, $"{TargetFeature} is expected to be enabled by the stock Development shell.");

        var applyBody = BuildApplyRequestRedirectingProvider(revision, beforeFeatures, TargetFeature, "Oracle");
        using var response = await client.PostAsync("/modularity/features/apply", JsonBody(applyBody));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = (string)(await response.Content.ReadFromJsonAsync<JsonNode>())!["errors"]!["generalErrors"]![0]!;
        Assert.Contains(TargetFeature, problem, StringComparison.Ordinal);
        // The provider name is named (it is not a secret); the guard's own unit coverage pins this same
        // wording for an unbindable engine (EfPendingMigrationActivationGuardTests).
        Assert.Contains("'Oracle' provider engine could not be bound", problem, StringComparison.Ordinal);
        Assert.Contains("Nothing was saved", problem, StringComparison.Ordinal);

        // Nothing was saved: same revision, and the stored feature is not carrying the refused provider.
        var after = await ReadCatalogAsync(client);
        Assert.Equal(revision, (string)after["revision"]!);
        var stillTarget = after["features"]!.AsArray().Single(feature => (string)feature!["id"]! == TargetFeature)!.AsObject();
        Assert.NotEqual("Oracle", ReadProvider(stillTarget["configuration"]));
    }

    private static async Task SignInAsAdminAsync(HttpClient client)
    {
        var login = new JsonObject { ["username"] = AdminUsername, ["password"] = AdminPassword };
        using var response = await client.PostAsync("/_elsa/identity/login", JsonBody(login));
        Assert.True(response.IsSuccessStatusCode, $"Admin sign-in failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task<JsonObject> ReadCatalogAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/modularity/features");
        Assert.True(response.IsSuccessStatusCode, $"Reading the feature catalog failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (JsonObject)(await response.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    /// <summary>
    /// Builds the full-desired-state apply body the endpoint requires (every currently-enabled feature, its
    /// own configuration echoed back unchanged so any masked secret round-trips), except <paramref name="targetFeature"/>,
    /// whose <c>Provider</c> setting is redirected to <paramref name="provider"/>.
    /// </summary>
    private static JsonObject BuildApplyRequestRedirectingProvider(string revision, JsonArray currentFeatures, string targetFeature, string provider)
    {
        var features = new JsonArray();
        foreach (var feature in currentFeatures)
        {
            if (!(bool)feature!["enabled"]!)
                continue;

            var id = (string)feature["id"]!;
            var configuration = feature["configuration"]!.DeepClone()!.AsObject();
            if (string.Equals(id, targetFeature, StringComparison.Ordinal))
                SetProvider(configuration, provider);

            features.Add(new JsonObject
            {
                ["id"] = id,
                ["enabled"] = true,
                ["configuration"] = configuration
            });
        }

        return new JsonObject { ["revision"] = revision, ["features"] = features };
    }

    /// <summary>Sets exactly one <c>Provider</c> property, replacing any existing one regardless of casing (the guard reads it case-insensitively).</summary>
    private static void SetProvider(JsonObject configuration, string provider)
    {
        var existingKey = configuration.Select(property => property.Key)
            .FirstOrDefault(key => string.Equals(key, "Provider", StringComparison.OrdinalIgnoreCase));
        if (existingKey is not null)
            configuration.Remove(existingKey);

        configuration["Provider"] = provider;
    }

    private static string? ReadProvider(JsonNode? configuration)
    {
        if (configuration is not JsonObject obj)
            return null;

        var key = obj.Select(property => property.Key).FirstOrDefault(k => string.Equals(k, "Provider", StringComparison.OrdinalIgnoreCase));
        return key is null ? null : (string?)obj[key];
    }

    private static StringContent JsonBody(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");
}
