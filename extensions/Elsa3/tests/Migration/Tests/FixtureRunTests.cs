using System.Text.Json;
using Elsa3.Migration.Tests.Support;
using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Tests;

/// <summary>Spec 1237 T018, T027 and T029: the fixture export through the whole pipeline, in process.</summary>
[Collection(nameof(CompositionCollection))]
public sealed class FixtureRunTests
{
    private const string Sentinel = "SENTINEL-1237";

    [Fact]
    public async Task Inventory_of_the_fixture_reports_every_selected_version_and_exits_1()
    {
        using var host = new TestHostFolder();

        var (result, output) = await FixtureRun.RunAsync(host);

        Assert.Null(result.Refusal);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(output, result.OutputPath);
        using var report = FixtureRun.ReadReport(output);
        var root = report.RootElement;
        Assert.Equal("inventory", root.GetProperty("command").GetString());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());

        var definitions = Definitions(root);
        Assert.Equal("convert-as-is", definitions[("order-intake", 1)].GetProperty("bucket").GetString());
        Assert.Contains("ELS3-ACTIVITY-UNMAPPED-TYPE", Codes(definitions[("invoice-reminder", 1)]));
        Assert.Contains("ELS3-JS-HOST-FUNCTION-MISSING", Codes(definitions[("customer-lookup", 1)]));
        Assert.Contains("VF-IMP-012", Codes(definitions[("dotnet-member", 1)]));
        Assert.Equal("incompatible", definitions[("legacy-tax-lookup", 1)].GetProperty("bucket").GetString());
        Assert.Equal(["legacy-tax-lookup-v1"], definitions[("invoice-tax", 1)].GetProperty("blockedBy").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains("ELS3-MIGRATION-NO-PUBLISHED-VERSION", Codes(definitions[("draft-refund", 1)]));
        Assert.Contains("ELS3-MIGRATION-SUPERSEDED-VERSION", Codes(definitions[("calculate-discount", 1)]));
        Assert.False(definitions.ContainsKey(("calculate-discount", 2)));

        // Every skip is an error finding, and an apply-refused definition carries the converter's own diagnostic.
        var unmapped = definitions[("invoice-reminder", 1)].GetProperty("findings").EnumerateArray()
            .Single(finding => finding.GetProperty("code").GetString() == "ELS3-ACTIVITY-UNMAPPED-TYPE");
        Assert.Equal("error", unmapped.GetProperty("severity").GetString());
        Assert.Equal("Acme.NotifyCustomer", unmapped.GetProperty("componentName").GetString());

        var components = root.GetProperty("components").EnumerateArray()
            .ToDictionary(component => (component.GetProperty("kind").GetString(), component.GetProperty("name").GetString()), component => component.GetProperty("usages").GetInt32());
        Assert.Equal(3, components[("activity", "Acme.NotifyCustomer")]);
        Assert.True(components.ContainsKey(("javascript-function", "lookupCustomer")));

        var missing = Assert.Single(root.GetProperty("missingTargets").EnumerateArray());
        Assert.Equal("partner-onboarding", missing.GetProperty("definitionId").GetString());
        Assert.True(root.GetProperty("run").GetProperty("persistenceOverridden").GetBoolean());
    }

    [Fact]
    public async Task The_reports_carry_no_input_text_and_nothing_is_left_outside_the_output_folder()
    {
        using var host = new TestHostFolder();
        var hostFolderBefore = Entries(host.HostFolder);
        var runFoldersBefore = RunFolders();

        var (result, output) = await FixtureRun.RunAsync(host);

        Assert.Null(result.Refusal);
        Assert.DoesNotContain(Sentinel, File.ReadAllText(Path.Combine(output, "report.json")), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, File.ReadAllText(Path.Combine(output, "report.html")), StringComparison.Ordinal);
        Assert.Equal(hostFolderBefore, Entries(host.HostFolder));
        Assert.Equal(["host", "out"], Directory.EnumerateFileSystemEntries(host.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(["migrated", "report.html", "report.json"], Directory.EnumerateFileSystemEntries(output).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(runFoldersBefore, RunFolders());
        Assert.False(Directory.Exists(Path.Combine(Environment.CurrentDirectory, "App_Data")));
    }

    [Fact]
    public async Task A_zip_export_reports_the_same_as_the_folder()
    {
        using var host = new TestHostFolder();

        var (folderRun, folderOutput) = await FixtureRun.RunAsync(host, output: Path.Combine(host.Root, "folder"));
        var (zipRun, zipOutput) = await FixtureRun.RunAsync(host, export: FixtureRun.ExportZip, output: Path.Combine(host.Root, "zip"));

        Assert.Equal(folderRun.ExitCode, zipRun.ExitCode);
        using var folderReport = FixtureRun.ReadReport(folderOutput);
        using var zipReport = FixtureRun.ReadReport(zipOutput);
        Assert.Equal(folderReport.RootElement.GetProperty("definitions").GetRawText(), zipReport.RootElement.GetProperty("definitions").GetRawText());
    }

    /// <summary>
    /// SC-007 (T027): with the fixture feature listed locally, <c>assess</c> clears exactly the findings that name its
    /// components; the value-only read becomes <c>ELS3-REPLACED-FUNCTION-REFERENCE</c>, and the definitions it unblocks
    /// publish.
    /// </summary>
    [Fact]
    public async Task Assess_with_the_fixture_feature_clears_exactly_the_findings_naming_its_components()
    {
        string[] provided = ["Acme.NotifyCustomer", "lookupCustomer", "getConfig", "getCustomerData", "Acme.Orders.OrderDto"];
        using var host = new TestHostFolder();
        var (inventoryRun, inventoryOutput) = await FixtureRun.RunAsync(host, output: Path.Combine(host.Root, "inventory"));
        host.WriteLocalSettings("""{ "CShells": { "Shells": { "default": { "Features": { "AcmeElsa3MigrationFixture": {} } } } } }""");

        var (assessRun, assessOutput) = await FixtureRun.RunAsync(host, MigrationCommand.Assess, output: Path.Combine(host.Root, "assess"));

        Assert.Null(inventoryRun.Refusal);
        Assert.Null(assessRun.Refusal);
        using var inventory = FixtureRun.ReadReport(inventoryOutput);
        using var assess = FixtureRun.ReadReport(assessOutput);
        var before = Definitions(inventory.RootElement);
        var after = Definitions(assess.RootElement);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());

        foreach (var (key, definition) in before)
        {
            var expected = NonRoutine(definition).Where(finding => !provided.Contains(finding.ComponentName)).Select(finding => finding.Code).Order(StringComparer.Ordinal).ToList();
            if (key == ("customer-batch", 1))
                expected = ["ELS3-REPLACED-FUNCTION-REFERENCE"];
            Assert.True(expected.SequenceEqual(NonRoutine(after[key]).Select(finding => finding.Code).Order(StringComparer.Ordinal)),
                $"{key}: expected [{string.Join(", ", expected)}], got [{string.Join(", ", NonRoutine(after[key]).Select(finding => finding.Code))}].");
        }

        foreach (var unblocked in new[] { "invoice-reminder", "shipment-notice", "welcome-customer", "customer-lookup", "customer-profile", "regional-pricing", "order-archive" })
            Assert.True(after[(unblocked, 1)].GetProperty("published").GetBoolean(), $"{unblocked} did not publish.");

        var components = assess.RootElement.GetProperty("components").EnumerateArray().Select(component => component.GetProperty("name").GetString()).ToArray();
        Assert.DoesNotContain("lookupCustomer", components);
        Assert.DoesNotContain("Acme.NotifyCustomer", components);
        Assert.Equal("assess", assess.RootElement.GetProperty("command").GetString());
        Assert.Contains("AcmeElsa3MigrationFixture", assess.RootElement.GetProperty("run").GetProperty("features").EnumerateArray().Select(feature => feature.GetString()));
    }

    private static IEnumerable<(string Code, string? ComponentName)> NonRoutine(JsonElement definition) =>
        definition.GetProperty("findings").EnumerateArray()
            .Where(finding => !finding.TryGetProperty("routine", out var routine) || !routine.GetBoolean())
            .Select(finding => (finding.GetProperty("code").GetString()!,
                finding.TryGetProperty("componentName", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null));

    private static Dictionary<(string?, int), JsonElement> Definitions(JsonElement root) =>
        root.GetProperty("definitions").EnumerateArray()
            .GroupBy(definition => (definition.GetProperty("definitionId").GetString(), definition.GetProperty("version").GetInt32()))
            .ToDictionary(group => group.Key, group => group.First());

    private static string[] Codes(JsonElement definition) =>
        definition.GetProperty("findings").EnumerateArray().Select(finding => finding.GetProperty("code").GetString()!).ToArray();

    /// <summary>The composition's run-scoped temporary folders (<c>elsa3-migration-&lt;guid&gt;</c>).</summary>
    private static string[] RunFolders() =>
        Directory.EnumerateDirectories(Path.GetTempPath(), "elsa3-migration-*")
            .Where(folder => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder), "^elsa3-migration-[0-9a-f]{32}$"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] Entries(string folder) =>
        Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Select(path => Path.GetRelativePath(folder, path)).Order(StringComparer.Ordinal).ToArray();
}
