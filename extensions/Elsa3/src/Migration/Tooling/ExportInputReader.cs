using System.IO.Compression;
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa3.Migration.Tooling.Report;
using Elsa3.Models;

namespace Elsa3.Migration.Tooling;

/// <summary>One Elsa 3 definition read from the export, with the file it came from relative to the export root.</summary>
internal sealed record ExportItem(string File, Elsa3WorkflowDefinition Definition);

/// <summary>What <see cref="ExportInputReader"/> read: the definitions and the findings of files it could not use.</summary>
internal sealed record ExportInput(IReadOnlyList<ExportItem> Items, IReadOnlyList<CollectedInputFinding> InputFindings);

/// <summary>
/// Reads an Elsa 3 export (spec 1237 FR-011, edge cases): a folder searched recursively for <c>*.json</c>, or a zip.
/// Files are read in ordinal order of their relative path ('/' separators; a zip entry path as it is). Each file holds one
/// definition or an array of them. A file that is not valid JSON, or valid JSON that holds no Elsa 3 definition, becomes
/// an input finding and the run continues. It needs no shell, so its refusals come before composition.
/// </summary>
internal static class ExportInputReader
{
    public static (ExportInput? Input, MigrationRefusal? Refusal) Read(string exportPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPath);

        IReadOnlyList<(string File, byte[] Content)> files;
        if (Directory.Exists(exportPath))
        {
            files = ReadFolder(exportPath);
        }
        else if (File.Exists(exportPath) && string.Equals(Path.GetExtension(exportPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                files = ReadZip(exportPath);
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                return (null, new(MigrationRefusal.InputUnreadable, "The export zip cannot be opened."));
            }
        }
        else
        {
            return (null, new(MigrationRefusal.InputMissing, "The export path does not exist, or is neither a folder nor a .zip file."));
        }

        var items = new List<ExportItem>();
        var findings = new List<CollectedInputFinding>();
        foreach (var (file, content) in files)
        {
            var definitions = Parse(content, out var code);
            if (definitions is null)
                findings.Add(new(file, InputFinding(code!)));
            else
                items.AddRange(definitions.Select(definition => new ExportItem(file, definition)));
        }

        return items.Count == 0
            ? (null, new(MigrationRefusal.InputEmpty, "No file in the export holds an Elsa 3 workflow definition."))
            : (new ExportInput(items, findings), null);
    }

    /// <summary>Parses one file; returns null with the finding code when the file holds no usable definition.</summary>
    internal static IReadOnlyList<Elsa3WorkflowDefinition>? Parse(byte[] content, out string? findingCode)
    {
        findingCode = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            findingCode = Elsa3MigrationToolingCodes.InputUnreadable;
            return null;
        }

        using (document)
        {
            var elements = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().ToArray(),
                JsonValueKind.Object => [document.RootElement],
                _ => []
            };

            if (elements.Length == 0 || !elements.All(IsDefinition))
            {
                findingCode = Elsa3MigrationDiagnosticCodes.UnsupportedInputKind;
                return null;
            }

            try
            {
                return elements.Select(element => element.Deserialize<Elsa3WorkflowDefinition>(JsonBaseline.Options)!).ToArray();
            }
            catch (JsonException)
            {
                findingCode = Elsa3MigrationDiagnosticCodes.UnsupportedInputKind;
                return null;
            }
        }
    }

    /// <summary>An Elsa 3 definition export carries a definition id and a root activity.</summary>
    private static bool IsDefinition(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("definitionId", out var definitionId) && definitionId.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(definitionId.GetString())
        && element.TryGetProperty("root", out var root) && root.ValueKind == JsonValueKind.Object;

    private static CollectedFinding InputFinding(string code) =>
        // The text comes from the code table's fixed text; nothing of the file's content is kept.
        new(code, FindingSeverity.Error, FindingStage.Input, null, null, code, null);

    private static IReadOnlyList<(string File, byte[] Content)> ReadFolder(string root) =>
        Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
            .Select(path => (File: Path.GetRelativePath(root, path).Replace('\\', '/'), Path: path))
            .OrderBy(file => file.File, StringComparer.Ordinal)
            .Select(file => (file.File, File.ReadAllBytes(file.Path)))
            .ToArray();

    private static IReadOnlyList<(string File, byte[] Content)> ReadZip(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return archive.Entries
            .Where(entry => !entry.FullName.EndsWith('/') && entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
            .Select(entry =>
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return (entry.FullName, buffer.ToArray());
            })
            .ToArray();
    }
}
