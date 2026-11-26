using System.IO.Compression;
using System.Text;
using Elsa3.Migration.Tooling;
using Elsa3.Models;

namespace Elsa3.Migration.Tests;

public sealed class ExportInputTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "elsa3-migration-tests-" + Guid.NewGuid().ToString("N"));

    public ExportInputTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Folder_is_read_recursively_in_ordinal_order_with_input_findings_for_unusable_files()
    {
        Write("b.json", Definition("b", 1));
        Write("a/nested.json", $"[{Definition("n1", 1)},{Definition("n2", 1)}]");
        Write("bad.json", "{ not json SENTINEL-1237");
        Write("other.json", "{\"name\":\"SENTINEL-1237\"}");
        Write("ignored.txt", Definition("x", 1));

        var (input, refusal) = ExportInputReader.Read(_root);

        Assert.Null(refusal);
        Assert.Equal(["a/nested.json", "a/nested.json", "b.json"], input!.Items.Select(item => item.File));
        Assert.Equal(["n1", "n2", "b"], input.Items.Select(item => item.Definition.DefinitionId));
        Assert.Collection(input.InputFindings,
            finding => Assert.Equal(("bad.json", Elsa3MigrationToolingCodes.InputUnreadable), (finding.File, finding.Finding.Code)),
            finding => Assert.Equal(("other.json", Elsa3MigrationDiagnosticCodes.UnsupportedInputKind), (finding.File, finding.Finding.Code)));
        Assert.All(input.InputFindings, finding => Assert.DoesNotContain("SENTINEL-1237", finding.Finding.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void Zip_entries_are_read_by_their_entry_path()
    {
        var zip = Path.Combine(_root, "export.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            Add(archive, "z/second.json", Definition("second", 1));
            Add(archive, "first.json", Definition("first", 1));
        }

        var (input, refusal) = ExportInputReader.Read(zip);

        Assert.Null(refusal);
        Assert.Equal(["first.json", "z/second.json"], input!.Items.Select(item => item.File));
    }

    [Fact]
    public void Missing_path_refuses_with_input_missing()
    {
        Assert.Equal(MigrationRefusal.InputMissing, ExportInputReader.Read(Path.Combine(_root, "absent")).Refusal?.Reason);
        Write("plain.json", Definition("a", 1));
        Assert.Equal(MigrationRefusal.InputMissing, ExportInputReader.Read(Path.Combine(_root, "plain.json")).Refusal?.Reason);
    }

    [Fact]
    public void Broken_zip_refuses_with_input_unreadable()
    {
        Write("broken.zip", "not a zip");

        Assert.Equal(MigrationRefusal.InputUnreadable, ExportInputReader.Read(Path.Combine(_root, "broken.zip")).Refusal?.Reason);
    }

    [Fact]
    public void An_export_with_no_definition_refuses_with_input_empty()
    {
        Write("bad.json", "nope");

        var refusal = ExportInputReader.Read(_root).Refusal;

        Assert.Equal(MigrationRefusal.InputEmpty, refusal?.Reason);
        Assert.StartsWith("elsa3-migration: refused: input-empty: ", refusal!.ToLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_byte_order_mark_is_accepted()
    {
        File.WriteAllBytes(Path.Combine(_root, "bom.json"), [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(Definition("bom", 1))]);

        var (input, refusal) = ExportInputReader.Read(_root);

        Assert.Null(refusal);
        Assert.Equal("bom", Assert.Single(input!.Items).Definition.DefinitionId);
        Assert.Empty(input.InputFindings);
    }

    [Fact]
    public void An_unusable_element_leaves_the_other_definitions_of_its_file()
    {
        Write("mixed.json", $"[{Definition("a", 1)},{{\"definitionId\":\"b\",\"root\":null}},{Definition("c", 1)}]");

        var (input, _) = ExportInputReader.Read(_root);

        Assert.Equal(["a", "c"], input!.Items.Select(item => item.Definition.DefinitionId));
        Assert.Equal(("mixed.json", Elsa3MigrationDiagnosticCodes.UnsupportedInputKind), (Assert.Single(input.InputFindings).File, input.InputFindings[0].Finding.Code));
    }

    [Fact]
    public void The_output_folder_inside_the_export_is_not_read_and_case_does_not_matter()
    {
        Write("a.JSON", Definition("a", 1));
        Write("out/report.json", "{\"schemaVersion\":1}");
        Write("out/migrated/workflows/a.json", "[]");

        var (input, _) = ExportInputReader.Read(_root, Path.Combine(_root, "out"));

        Assert.Equal(["a.JSON"], input!.Items.Select(item => item.File));
        Assert.Empty(input.InputFindings);
    }

    internal static string Definition(string definitionId, int version, bool published = true, string? versionId = null, string rootActivities = "", bool reusable = false)
    {
        var options = reusable ? "{\"usableAsActivity\":true}" : "{}";
        return $$$"""
          {"id":"{{{versionId ?? definitionId + "-v" + version}}}","definitionId":"{{{definitionId}}}","name":"{{{definitionId}}}","version":{{{version}}},
           "isPublished":{{{(published ? "true" : "false")}}},"isLatest":true,"options":{{{options}}},
           "root":{"id":"root","nodeId":"root","type":"Elsa.Flowchart","activities":[{{{rootActivities}}}]}}
          """;
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void Add(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }
}

public sealed class VersionSelectionTests
{
    [Fact]
    public void Latest_published_version_converts_and_others_are_listed()
    {
        var items = Items(("a", 1, true, null), ("a", 2, true, null), ("a", 3, false, null));

        var result = VersionSelection.Select(items, allVersions: false);

        Assert.Equal(["a-v2"], result.Selected.Select(item => item.Definition.Id));
        Assert.Equal([("a", 1), ("a", 3)], result.NotConverted.Select(item => (item.DefinitionId, item.Version)));
        Assert.Empty(result.NoPublishedVersion);
    }

    [Fact]
    public void Definition_without_published_version_converts_its_latest()
    {
        var result = VersionSelection.Select(Items(("a", 1, false, null), ("a", 2, false, null)), allVersions: false);

        Assert.Equal(["a-v2"], result.Selected.Select(item => item.Definition.Id));
        Assert.Equal(["a-v2"], result.NoPublishedVersion);
    }

    [Fact]
    public void Versions_pinned_by_reusable_references_convert_transitively()
    {
        var items = Items(
            ("lib", 1, true, null), ("lib", 2, true, null), ("lib", 3, true, null),
            ("inner", 1, true, null), ("inner", 2, true, null),
            ("app", 1, true, """{"id":"r","nodeId":"r","type":"lib","version":1}"""));
        // lib v1 pins inner v1 by its version id.
        items = items.Select(item => item.Definition.Id == "lib-v1"
            ? Parse(item.File, ExportInputTests.Definition("lib", 1, reusable: true, rootActivities: """{"id":"q","nodeId":"q","type":"Elsa.Whatever","version":1,"workflowDefinitionVersionId":"inner-v1"}"""))
            : item).ToArray();

        var result = VersionSelection.Select(items, allVersions: false);

        Assert.Equal(["app-v1", "inner-v1", "inner-v2", "lib-v1", "lib-v3"], result.Selected.Select(item => item.Definition.Id).Order(StringComparer.Ordinal));
        Assert.Equal([("lib", 2)], result.NotConverted.Select(item => (item.DefinitionId, item.Version)));
    }

    [Fact]
    public void Dispatch_targets_pin_nothing()
    {
        var items = Items(("a", 1, true, null), ("a", 2, true, null),
            ("app", 1, true, """{"id":"d","nodeId":"d","type":"Elsa.DispatchWorkflow","version":1,"workflowDefinitionId":"a"}"""));

        var result = VersionSelection.Select(items, allVersions: false);

        Assert.DoesNotContain(result.Selected, item => item.Definition.Id == "a-v1");
    }

    [Fact]
    public void Duplicates_of_a_selected_version_are_kept_for_the_duplicate_refusal()
    {
        var items = Items(("a", 1, true, null), ("a", 2, true, null), ("a", 2, true, null))
            .Append(Parse("dup.json", ExportInputTests.Definition("other", 1, versionId: "a-v2")))
            .ToArray();

        var result = VersionSelection.Select(items, allVersions: false);

        Assert.Equal(3, result.Selected.Count(item => item.Definition.Id == "a-v2"));
        Assert.Equal([("a", 1)], result.NotConverted.Select(item => (item.DefinitionId, item.Version)));
    }

    [Fact]
    public void All_versions_selects_everything()
    {
        var items = Items(("a", 1, true, null), ("a", 2, false, null));

        var result = VersionSelection.Select(items, allVersions: true);

        Assert.Equal(2, result.Selected.Count);
        Assert.Empty(result.NotConverted);
    }

    [Fact]
    public void References_the_analyzer_would_refuse_pin_nothing()
    {
        var items = Items(("plain", 1, true, null), ("plain", 2, true, null), ("lib", 1, true, null), ("lib", 2, true, null),
            ("app", 1, true, """
                {"id":"a","nodeId":"a","type":"plain","version":1},
                {"id":"b","nodeId":"b","type":"Elsa.Whatever","version":1,"workflowDefinitionId":"other","workflowDefinitionVersionId":"lib-v1"}
                """));

        var result = VersionSelection.Select(items, allVersions: false);

        Assert.Equal(["app-v1", "lib-v2", "plain-v2"], result.Selected.Select(item => item.Definition.Id).Order(StringComparer.Ordinal));
    }

    private static readonly HashSet<string> Reusable = new(StringComparer.Ordinal) { "lib", "inner" };

    private static ExportItem[] Items(params (string Id, int Version, bool Published, string? Activities)[] definitions) =>
        definitions.Select((definition, index) => Parse($"f{index}.json",
            ExportInputTests.Definition(definition.Id, definition.Version, definition.Published, rootActivities: definition.Activities ?? "", reusable: Reusable.Contains(definition.Id)))).ToArray();

    private static ExportItem Parse(string file, string json) =>
        new(file, ExportInputReader.Parse(Encoding.UTF8.GetBytes(json), out _)!.Single());
}
