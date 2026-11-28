using System.Text.Json;
using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa3.Migration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Migration.Tests;

/// <summary>
/// Spec 1237 T022 (the migrated export, FR-022 to FR-024) and T024 (its loadability, SC-004): the fixture export runs
/// through the whole pipeline twice, and a run whose only definition is refused once; the migration host's committed
/// composition then loads each run's <c>migrated/</c>.
/// </summary>
[Collection(nameof(CompositionCollection))]
public sealed class MigratedExportTests(MigratedExportTests.Runs runs) : IClassFixture<MigratedExportTests.Runs>
{
    private const string ActivitiesPath = "migrated/activities/activities.json";

    [Fact]
    public void Migrated_holds_one_file_per_exported_workflow_definition_and_the_activities_file()
    {
        var migrated = Path.Combine(runs.First, "migrated");
        Assert.Equal(["activities", "workflows"], Directory.EnumerateFileSystemEntries(migrated).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["activities.json"], Directory.EnumerateFileSystemEntries(Path.Combine(migrated, "activities")).Select(Path.GetFileName));

        var definitions = Definitions(runs.First);
        var named = definitions.Values
            .SelectMany(definition => new[] { Text(definition, "exportedTo"), Text(definition, "wrapperExportedTo") })
            .OfType<string>()
            .Where(path => path.StartsWith("migrated/workflows/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        var written = Directory.EnumerateFiles(Path.Combine(migrated, "workflows")).Select(path => "migrated/workflows/" + Path.GetFileName(path)).Order(StringComparer.Ordinal);
        Assert.Equal(written, named);

        // A workflow goes to its own file; a reusable to the activities file, and its wrapper to its own file.
        Assert.Equal("migrated/workflows/order-intake.json", Text(definitions[("order-intake", 1)], "exportedTo"));
        Assert.Null(Text(definitions[("order-intake", 1)], "wrapperExportedTo"));
        Assert.Equal(ActivitiesPath, Text(definitions[("format-address", 1)], "exportedTo"));
        Assert.Equal("migrated/workflows/format-address.json", Text(definitions[("format-address", 1)], "wrapperExportedTo"));

        foreach (var file in Directory.EnumerateFiles(Path.Combine(migrated, "workflows")))
        {
            using var envelopes = JsonDocument.Parse(File.ReadAllBytes(file));
            foreach (var envelope in envelopes.RootElement.EnumerateArray())
            {
                Assert.Equal(Path.GetFileNameWithoutExtension(file), envelope.GetProperty("definitionId").GetString());
                Assert.Equal(JsonValueKind.Null, envelope.GetProperty("sourceCreatedAt").ValueKind);
                Assert.Equal(JsonValueKind.Null, envelope.GetProperty("contentHash").ValueKind);
            }
        }
    }

    [Fact]
    public void Refused_and_blocked_definitions_are_left_out()
    {
        var definitions = Definitions(runs.First);
        foreach (var id in new[] { "legacy-tax-lookup", "invoice-tax", "csharp-greeting", "invoice-reminder", "ticket-router", "inventory-sync" })
        {
            var definition = definitions[(id, 1)];
            Assert.Null(Text(definition, "exportedTo"));
            Assert.Null(Text(definition, "wrapperExportedTo"));
            Assert.False(File.Exists(Path.Combine(runs.First, "migrated", "workflows", id + ".json")), id);
        }

        Assert.DoesNotContain("LegacyTaxLookup", Activities(runs.First).Select(activity => activity.GetProperty("displayName").GetString()));
    }

    [Fact]
    public void A_run_that_exports_nothing_writes_an_empty_workflows_folder_and_an_empty_activities_array()
    {
        var migrated = Path.Combine(runs.Empty, "migrated");

        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(migrated, "workflows")));
        Assert.Empty(Activities(runs.Empty));
        Assert.All(Definitions(runs.Empty).Values, definition => Assert.Null(Text(definition, "exportedTo")));
    }

    [Fact]
    public void Both_published_versions_of_calculate_discount_are_exported_and_quote_builder_names_the_pinned_one()
    {
        var versions = Activities(runs.First).Where(activity => activity.GetProperty("displayName").GetString() == "CalculateDiscount").ToArray();

        Assert.Equal(["1.0.0", "3.0.0"], versions.Select(activity => activity.GetProperty("version").GetString()));
        Assert.Equal(ActivitiesPath, Text(Definitions(runs.First)[("calculate-discount", 1)], "exportedTo"));
        var pinned = CatalogVersionId(versions[0]);
        Assert.Contains(pinned, File.ReadAllText(Path.Combine(runs.First, "migrated", "workflows", "quote-builder.json")), StringComparison.Ordinal);
        Assert.DoesNotContain(CatalogVersionId(versions[1]), File.ReadAllText(Path.Combine(runs.First, "migrated", "workflows", "quote-builder.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Credit_check_declares_done_and_its_custom_outcome()
    {
        var creditCheck = Activities(runs.First).Single(activity => activity.GetProperty("displayName").GetString() == "CreditCheck");

        var ports = creditCheck.GetProperty("designFacets").EnumerateArray()
            .Single(facet => facet.GetProperty("kind").GetString() == "elsa.outcomes")
            .GetProperty("payload").GetProperty("ports").EnumerateArray()
            .Select(port => port.GetProperty("referenceKey").GetString())
            .Order(StringComparer.Ordinal);
        Assert.Equal(["Escalated", "done"], ports);
        Assert.Equal("elsa.activity-graph", creditCheck.GetProperty("providerKey").GetString());
    }

    [Fact]
    public void A_nested_reusable_is_named_by_its_catalog_id_inside_containers_and_comes_before_its_user()
    {
        var activities = Activities(runs.First);
        var names = activities.Select(activity => activity.GetProperty("displayName").GetString()).ToList();
        var auditEntry = CatalogVersionId(activities[names.IndexOf("AuditEntry")]);

        Assert.True(names.IndexOf("AuditEntry") < names.IndexOf("CloseOrder"));
        Assert.Contains(auditEntry, activities[names.IndexOf("CloseOrder")].GetProperty("descriptor").GetRawText(), StringComparison.Ordinal);
        Assert.Contains(auditEntry, File.ReadAllText(Path.Combine(runs.First, "migrated", "workflows", "nightly-audit.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Two_runs_write_byte_identical_migrated_folders()
    {
        var first = Files(Path.Combine(runs.First, "migrated"));
        var second = Files(Path.Combine(runs.Second, "migrated"));

        Assert.Equal(first.Keys, second.Keys);
        foreach (var (path, bytes) in first)
            Assert.True(bytes.SequenceEqual(second[path]), $"{path} differs between two runs.");
    }

    [Fact]
    public async Task The_migration_host_loads_every_exported_reusable_and_workflow()
    {
        await using var host = await MigratedHostRun.StartAsync(Path.Combine(runs.First, "migrated"));
        await using var scope = host.Shell.BeginScope();
        var services = scope.ServiceProvider;

        // Each version the activities file holds went through SourceOwnedActivityVersionPublisher, which alone writes a
        // source-owned publication, and none was refused.
        var refusals = services.GetRequiredService<IActivityReconciliationDiagnosticStore>().List();
        Assert.True(refusals.Count == 0, string.Join(Environment.NewLine, refusals.SelectMany(entry => entry.Value.Select(diagnostic => $"{entry.Key}: {diagnostic.Code} {diagnostic.Message}"))));
        var expected = Activities(runs.First).Select(CatalogVersionId).ToArray();
        var workflowIds = Directory.EnumerateFiles(Path.Combine(runs.First, "migrated", "workflows")).Select(Path.GetFileNameWithoutExtension).OfType<string>().ToArray();
        var published = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);
        await services.GetRequiredService<IPersistenceScopeRunner>().RunAsync(async (_, operation, token) =>
        {
            var publications = operation.ServiceProvider.GetRequiredService<IActivityDefinitionVersionPublicationStore>();
            foreach (var id in expected)
                if (await publications.FindAsync(id, token) is not null)
                    published.Add(id);
            var authority = operation.ServiceProvider.GetRequiredService<IWorkflowActivationAuthority>();
            foreach (var id in workflowIds)
                if ((await authority.ListByDefinitionAsync(id, token)).Any(slot => slot.ActiveActivationId is not null))
                    active.Add(id);
        });

        Assert.Equal(6, expected.Length);
        Assert.Equal(expected.Order(StringComparer.Ordinal), published.Order(StringComparer.Ordinal));
        Assert.Contains("quote-builder", workflowIds);
        Assert.Contains("nightly-audit", workflowIds);
        Assert.Equal(workflowIds.Order(StringComparer.Ordinal), active.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_migration_host_loads_an_empty_migrated_folder()
    {
        await using var host = await MigratedHostRun.StartAsync(Path.Combine(runs.Empty, "migrated"));
        await using var scope = host.Shell.BeginScope();

        Assert.Empty(scope.ServiceProvider.GetRequiredService<IActivityReconciliationDiagnosticStore>().List());
    }

    private static string CatalogVersionId(JsonElement activity) =>
        ActivityCatalogStableIds.VersionId(activity.GetProperty("activityTypeKey").GetString()!, activity.GetProperty("version").GetString()!)!;

    private static List<JsonElement> Activities(string output)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "migrated", "activities", "activities.json")));
        return document.RootElement.EnumerateArray().Select(activity => activity.Clone()).ToList();
    }

    private static Dictionary<(string?, int), JsonElement> Definitions(string output)
    {
        using var report = FixtureRun.ReadReport(output);
        return report.RootElement.GetProperty("definitions").EnumerateArray()
            .GroupBy(definition => (definition.GetProperty("definitionId").GetString(), definition.GetProperty("version").GetInt32()))
            .ToDictionary(group => group.Key, group => group.First().Clone());
    }

    private static string? Text(JsonElement definition, string member) =>
        definition.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static SortedDictionary<string, byte[]> Files(string folder) =>
        new(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), File.ReadAllBytes), StringComparer.Ordinal);

    /// <summary>Two runs of the fixture export, and one of an export whose only definition the converter refuses.</summary>
    public sealed class Runs : IAsyncLifetime
    {
        private readonly TestHostFolder _host = new();

        public string First { get; private set; } = null!;
        public string Second { get; private set; } = null!;
        public string Empty { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            First = await RunAsync(null, "first");
            Second = await RunAsync(null, "second");

            var export = Path.Combine(_host.Root, "refused-only");
            Directory.CreateDirectory(export);
            File.Copy(Path.Combine(FixtureRun.ExportFolder, "workflows", "legacy", "csharp-greeting.json"), Path.Combine(export, "csharp-greeting.json"));
            Empty = await RunAsync(export, "empty");
        }

        private async Task<string> RunAsync(string? export, string name)
        {
            var (result, output) = await FixtureRun.RunAsync(_host, export: export, output: Path.Combine(_host.Root, name));
            Assert.Null(result.Refusal);
            return output;
        }

        public Task DisposeAsync()
        {
            _host.Dispose();
            return Task.CompletedTask;
        }
    }
}
