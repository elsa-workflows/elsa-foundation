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
    /// <param name="exportPath">A folder or a .zip file.</param>
    /// <param name="excludedFolder">
    /// A folder inside an export folder that is not read: the run's own output folder, so a run whose <c>--out</c> lies
    /// inside the export does not read its previous report and migrated content as input.
    /// </param>
    public static (ExportInput? Input, MigrationRefusal? Refusal) Read(string exportPath, string? excludedFolder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPath);

        IReadOnlyList<(string File, byte[] Content)> files;
        if (Directory.Exists(exportPath))
        {
            try
            {
                files = ReadFolder(exportPath, excludedFolder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return (null, new(MigrationRefusal.InputUnreadable, "A file or folder in the export folder cannot be read."));
            }
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
            if (code is not null)
                findings.Add(new(file, InputFinding(code)));
            items.AddRange(definitions.Select(definition => new ExportItem(file, definition)));
        }

        return items.Count == 0
            ? (null, new(MigrationRefusal.InputEmpty, "No file in the export holds an Elsa 3 workflow definition."))
            : (new ExportInput(items, findings), null);
    }

    /// <summary>
    /// Parses one file into the definitions it holds. A file that is not valid JSON yields none and
    /// <see cref="Elsa3MigrationToolingCodes.InputUnreadable"/>; an element that is no Elsa 3 definition is left out, and
    /// the file then carries one <see cref="Elsa3MigrationDiagnosticCodes.UnsupportedInputKind"/> finding, while its other
    /// definitions are still read. A UTF-8 byte order mark is accepted, as the startup import accepts it.
    /// </summary>
    internal static IReadOnlyList<Elsa3WorkflowDefinition> Parse(byte[] content, out string? findingCode)
    {
        findingCode = null;
        var json = content.AsMemory();
        if (json.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            json = json[3..];

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            findingCode = Elsa3MigrationToolingCodes.InputUnreadable;
            return [];
        }

        using (document)
        {
            var elements = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().ToArray(),
                JsonValueKind.Object => [document.RootElement],
                _ => []
            };

            var definitions = new List<Elsa3WorkflowDefinition>();
            foreach (var element in elements)
            {
                if (TryRead(element) is { } definition)
                    definitions.Add(definition);
                else
                    findingCode = Elsa3MigrationDiagnosticCodes.UnsupportedInputKind;
            }

            if (elements.Length == 0)
                findingCode = Elsa3MigrationDiagnosticCodes.UnsupportedInputKind;
            return definitions;
        }
    }

    private static Elsa3WorkflowDefinition? TryRead(JsonElement element)
    {
        if (!IsDefinition(element))
            return null;
        try
        {
            return element.Deserialize<Elsa3WorkflowDefinition>(JsonBaseline.Options);
        }
        catch (JsonException)
        {
            return null;
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

    // *.json in any letter case on every platform, as the zip path matches it, so folder input reads the same files
    // on Windows and Linux (FR-022). Unreadable entries throw, and the caller refuses the run.
    private static readonly EnumerationOptions JsonFiles = new()
    {
        RecurseSubdirectories = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

    private static IReadOnlyList<(string File, byte[] Content)> ReadFolder(string root, string? excludedFolder)
    {
        var excluded = excludedFolder is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(excludedFolder)) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(root, "*.json", JsonFiles)
            .Where(path => excluded is null || !Path.GetFullPath(path).StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            .Select(path => (File: Path.GetRelativePath(root, path).Replace('\\', '/'), Path: path))
            .OrderBy(file => file.File, StringComparer.Ordinal)
            .Select(file => (file.File, File.ReadAllBytes(file.Path)))
            .ToArray();
    }

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
