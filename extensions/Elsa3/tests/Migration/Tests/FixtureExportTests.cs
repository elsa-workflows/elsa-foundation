using Elsa3.Migration.Tooling;
using Elsa3.Models;

namespace Elsa3.Migration.Tests;

/// <summary>
/// The spec 1237 fixture export (T002, <c>Fixtures/README.md</c>) reads as an export: the folder and its zip copy give
/// the same definitions, the two files that hold no definition become input findings, and every case of the README is
/// there.
/// </summary>
public sealed class FixtureExportTests
{
    private static readonly string FixturesRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string ExportFolder = Path.Combine(FixturesRoot, "export");
    private static readonly string ExportZip = Path.Combine(FixturesRoot, "export.zip");

    /// <summary>The definitions and versions of the README's case table.</summary>
    private static readonly (string DefinitionId, int Version)[] ExpectedVersions =
    [
        ("audit-entry", 1), ("bad-defaults", 1), ("calculate-discount", 1), ("calculate-discount", 2), ("calculate-discount", 3),
        ("close-order", 1), ("credit-check", 1), ("csharp-greeting", 1), ("customer-batch", 1), ("customer-lookup", 1),
        ("customer-profile", 1), ("daily-report", 1), ("dotnet-member", 1), ("draft-refund", 1), ("format-address", 1),
        ("instance-label", 1), ("inventory-sync", 1), ("inventory-sync", 1), ("invoice-reminder", 1), ("invoice-tax", 1),
        ("legacy-octal", 1), ("legacy-tax-lookup", 1), ("liquid-letter", 1), ("loan-application", 1), ("nightly-audit", 1),
        ("order-archive", 1), ("order-closing", 1), ("order-intake", 1), ("parallel-checks", 1), ("partner-sync", 1),
        ("quote-builder", 1), ("regional-pricing", 1), ("return-label", 1), ("session-cleanup", 1), ("ship-order", 1),
        ("shipment-notice", 1), ("ticket-escalation", 1), ("ticket-router", 1), ("welcome-customer", 1),
    ];

    [Fact]
    public void The_folder_reads_every_case_with_input_findings_for_the_files_that_hold_no_definition()
    {
        var input = ReadExport(ExportFolder);

        Assert.Equal(
            ExpectedVersions.OrderBy(version => version.DefinitionId, StringComparer.Ordinal).ThenBy(version => version.Version),
            input.Items.Select(item => (item.Definition.DefinitionId, item.Definition.Version))
                .OrderBy(version => version.DefinitionId, StringComparer.Ordinal).ThenBy(version => version.Version));
        Assert.Collection(input.InputFindings,
            finding => Assert.Equal(("notes/broken-export.json", Elsa3MigrationToolingCodes.InputUnreadable), (finding.File, finding.Finding.Code)),
            finding => Assert.Equal(("settings/studio-preferences.json", Elsa3MigrationDiagnosticCodes.UnsupportedInputKind), (finding.File, finding.Finding.Code)));
        // Only *.json is read: the notes text file is neither a definition nor a finding.
        Assert.DoesNotContain(input.Items, item => item.File.EndsWith(".txt", StringComparison.Ordinal));
        Assert.Contains(input.Items, item => item.File == "workflows/customers/customer-notifications.json");
    }

    [Fact]
    public void The_zip_copy_reads_the_same_definitions_and_findings_under_its_export_root()
    {
        var folder = ReadExport(ExportFolder);
        var zip = ReadExport(ExportZip);

        Assert.Equal(
            folder.Items.Select(item => ("export/" + item.File, item.Definition.Id, item.Definition.DefinitionId, item.Definition.Version)),
            zip.Items.Select(item => (item.File, item.Definition.Id, item.Definition.DefinitionId, item.Definition.Version)));
        Assert.Equal(
            folder.InputFindings.Select(finding => ("export/" + finding.File, finding.Finding.Code)),
            zip.InputFindings.Select(finding => (finding.File, finding.Finding.Code)));
    }

    [Fact]
    public void The_custom_activity_the_fixture_feature_catalogs_is_used_by_three_definitions()
    {
        var input = ReadExport(ExportFolder);

        Assert.Equal(
            ["invoice-reminder", "shipment-notice", "welcome-customer"],
            input.Items.Where(item => Activities(item.Definition.Root).Any(activity => activity.Type == "Acme.NotifyCustomer"))
                .Select(item => item.Definition.DefinitionId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Calculate_discount_has_a_latest_published_version_an_older_pinned_one_and_an_unpinned_one()
    {
        var versions = ReadExport(ExportFolder).Items
            .Where(item => item.Definition.DefinitionId == "calculate-discount")
            .ToDictionary(item => item.Definition.Version, item => item.Definition);
        var pins = ReadExport(ExportFolder).Items
            .SelectMany(item => Activities(item.Definition.Root))
            .Where(activity => activity.AdditionalProperties.TryGetValue("workflowDefinitionId", out var id)
                               && id.ValueKind == System.Text.Json.JsonValueKind.String && id.GetString() == "calculate-discount")
            .Select(activity => activity.AdditionalProperties["workflowDefinitionVersionId"].GetString());

        Assert.Equal([(1, false), (2, false), (3, true)], versions.OrderBy(entry => entry.Key).Select(entry => (entry.Key, entry.Value.IsPublished)));
        Assert.Equal(["calculate-discount-v1"], pins);
    }

    private static ExportInput ReadExport(string path)
    {
        var (input, refusal) = ExportInputReader.Read(path);
        Assert.Null(refusal);
        return input!;
    }

    private static IEnumerable<Elsa3Activity> Activities(Elsa3Activity root)
    {
        var stack = new Stack<Elsa3Activity>([root]);
        while (stack.TryPop(out var activity))
        {
            yield return activity;
            foreach (var child in activity.Activities ?? [])
                stack.Push(child);
        }
    }
}
