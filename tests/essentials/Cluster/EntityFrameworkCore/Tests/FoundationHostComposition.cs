using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// How the <c>Elsa.Foundation.Host</c> boot tests compose a host: the feeds beside its own <c>packages</c> one, the engine
/// its modules bind, the finalization gate's timings, and the <c>shells.json</c> that enables the fixture's features.
/// </summary>
internal static class FoundationHostComposition
{
    /// <summary>Where the fixture's orders feature answers: 200 while it serves, 409 while it is dormant.</summary>
    public const string OrdersPath = "/feed-module-fixture/orders";

    /// <summary>The fixture's persistence feature, which binds its context and admits it through its finalization gate.</summary>
    public const string EntityFrameworkCoreFeature = "FeedModuleFixtureEntityFrameworkCore";

    /// <summary>
    /// The fixture's feature that needs version 2 of its family, the version its release writes. The release before the
    /// fixture's carries it too, needing version 1, the one version that release reads.
    /// </summary>
    public const string OrdersFeature = "FeedModuleFixtureOrders";

    /// <summary>The settings every boot test starts the host with, as configuration keys.</summary>
    public static Dictionary<string, string> Settings(FoundationHostFeed feed, string provider = "Sqlite") => new()
    {
        // A second feed beside the host's own `packages` one, which resolves what the packages there depend on and holds
        // nothing the host loads on its own account: with no include patterns it is a source, not a list of roots.
        ["Nuplane:Setup:Feeds:1:Name"] = "closure",
        ["Nuplane:Setup:Feeds:1:DirectoryPath"] = feed.ClosureDirectory,
        // The one engine the module's ef-provider capability offers that this database uses.
        ["Nuplane:Capabilities:ef-provider"] = provider,
        [$"{EfSchemaFinalizationOptions.SectionName}:{nameof(EfSchemaFinalizationOptions.EvaluationInterval)}"] = "00:00:00.200",
        [$"{EfSchemaFinalizationOptions.SectionName}:{nameof(EfSchemaFinalizationOptions.RefreshInterval)}"] = "00:00:00.100"
    };

    /// <summary>
    /// A <c>shells.json</c> with one shell that enables <paramref name="features"/> of the fixture over
    /// <paramref name="connectionString"/>. The shell validates its modules' migrations rather than applying them, because
    /// the fixture ships none: its database is created by the test, as a release before it did. The host's own policy is
    /// left at its default, so a module the host composes on its own container, the cluster membership table, migrates
    /// itself as the host starts.
    /// </summary>
    public static string Shells(string connectionString, params string[] features) => ShellsOn("Sqlite", connectionString, features);

    /// <inheritdoc cref="Shells"/>
    /// <param name="provider">The engine the fixture's persistence feature binds, by its <c>Provider</c> setting.</param>
    public static string ShellsOn(string provider, string connectionString, params string[] features)
    {
        var enabled = new JsonObject();
        foreach (var feature in features)
            enabled[feature] = feature == EntityFrameworkCoreFeature
                ? new JsonObject { ["ConnectionString"] = connectionString, ["Provider"] = provider }
                : new JsonObject();

        var configuration = new JsonObject
        {
            ["WebRouting"] = new JsonObject { ["Path"] = "" },
            [$"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}"] = nameof(EfMigratePolicy.Validate)
        };

        return new JsonObject
        {
            ["CShells"] = new JsonObject
            {
                ["Shells"] = new JsonObject
                {
                    ["default"] = new JsonObject
                    {
                        ["Name"] = "default",
                        ["Features"] = enabled,
                        ["Configuration"] = configuration
                    }
                }
            }
        }.ToJsonString();
    }

    /// <summary>The fixture's orders endpoint on <paramref name="host"/>: its status, and the text of what it answered.</summary>
    public static async Task<(HttpStatusCode Status, string Text)> OrdersAsync(FoundationHostProcess host)
    {
        var (status, body) = await host.GetAsync(OrdersPath);
        return (status, Text(body));
    }

    /// <summary>A minimal-API string result is a JSON string, whose quotes are escaped; this is the text it carries.</summary>
    private static string Text(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(body) ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }
}
