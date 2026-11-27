using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;
using Microsoft.AspNetCore.WebUtilities;

namespace Elsa3.Activities.Design.Import.Services;

/// <summary>Reads all selected exports before the operation service creates a collection.</summary>
internal sealed class Elsa3ExportFileReader(ReusableActivityImportOptions options)
{
    private readonly List<Elsa3WorkflowDefinition> _definitions = [];
    private readonly byte[] _buffer = new byte[8192];
    private long _expandedBytes;
    private int _archiveEntries;

    public IReadOnlyList<Elsa3WorkflowDefinition> Definitions => _definitions;
    public long UploadedBytes { get; private set; }

    public async Task ReadAsync(Stream body, string boundary, long? contentLength, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 70)
            throw new ReusableActivityImportPayloadException("The export upload needs a valid multipart boundary.");

        // Bound framing as well as file bytes, including requests without a Content-Length.
        var wireLimit = Math.Min(options.MaximumUploadBytes, int.MaxValue) + 64L * 1024;
        const string wireError = "The export upload exceeds the request size limit.";
        if (contentLength > wireLimit)
            throw new ReusableActivityImportPayloadException(wireError);

        using var envelope = new MemoryStream();
        await CopyAsync(body, envelope, wireLimit, wireError, cancellationToken);
        envelope.Position = 0;
        var reader = new MultipartReader(boundary, envelope);
        string source = "Export upload";
        try
        {
            while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                var file = section.AsFileSection()
                    ?? throw new ReusableActivityImportPayloadException("The export upload must contain only files.");
                source = file.FileName;
                using var content = new MemoryStream();
                await CopyAsync(section.Body, content, options.MaximumUploadBytes - UploadedBytes,
                    $"{source}: the selection exceeds the {options.MaximumUploadBytes}-byte upload limit.", cancellationToken);
                UploadedBytes += content.Length;
                content.Position = 0;

                if (source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    await ReadArchiveAsync(content, source, cancellationToken);
                else if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    await ReadJsonAsync(content, source, cancellationToken);
                else
                    throw new ReusableActivityImportPayloadException($"{source}: select a JSON or ZIP export.");
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException)
        {
            throw new ReusableActivityImportPayloadException($"{source}: the export could not be read.", e);
        }

        if (_definitions.Count == 0)
            throw new ReusableActivityImportPayloadException("Select at least one export containing a workflow definition.");
    }

    private async Task ReadArchiveAsync(MemoryStream content, string archiveName, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);
        if (DeclaredEntryCount(content) > (ulong)(options.MaximumArchiveEntries - _archiveEntries))
            throw new ReusableActivityImportPayloadException($"{archiveName}: the selection exceeds the {options.MaximumArchiveEntries}-entry ZIP limit.");
        _archiveEntries += archive.Entries.Count;
        var before = _definitions.Count;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') && entry.Length == 0)
                continue;

            var source = $"{archiveName} / {entry.FullName}";
            if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                throw new ReusableActivityImportPayloadException($"{source}: a workflow export ZIP must contain JSON files.");
            try
            {
                using var stream = entry.Open();
                await ReadJsonAsync(stream, source, cancellationToken);
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                throw new ReusableActivityImportPayloadException($"{source}: the ZIP member could not be read.", e);
            }
        }
        if (before == _definitions.Count)
            throw new ReusableActivityImportPayloadException($"{archiveName}: the ZIP contains no workflow definitions.");
    }

    // ZipArchive verifies its declared count while reading, but Entries materializes that many
    // records. Check its end-of-directory count first, including the ZIP64 replacement record.
    private static ulong DeclaredEntryCount(MemoryStream content)
    {
        var bytes = content.GetBuffer().AsSpan(0, (int)content.Length);
        const int footerSize = 22;
        if (bytes.Length < footerSize)
            throw new InvalidDataException("Missing ZIP directory footer.");
        var start = Math.Max(0, bytes.Length - footerSize - ushort.MaxValue);
        var signature = bytes[start..(bytes.Length - footerSize + 4)].LastIndexOf("PK\x05\x06"u8);
        if (signature < 0)
            throw new InvalidDataException("Missing ZIP directory footer.");
        var offset = start + signature;
        var footer = bytes[offset..];
        var count = BinaryPrimitives.ReadUInt16LittleEndian(footer[10..]);
        if (count != ushort.MaxValue && BinaryPrimitives.ReadUInt16LittleEndian(footer[4..]) != ushort.MaxValue
            && BinaryPrimitives.ReadUInt32LittleEndian(footer[16..]) != uint.MaxValue)
            return count;

        const int locatorSize = 20;
        if (offset < locatorSize || !bytes[(offset - locatorSize)..].StartsWith("PK\x06\x07"u8))
            return count; // A classic archive can contain exactly 65,535 entries.
        var position = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(offset - locatorSize + 8)..]);
        if (position > (ulong)(offset - locatorSize) || offset - locatorSize - (long)position < 56)
            throw new InvalidDataException("Invalid ZIP64 directory footer offset.");
        var zip64 = bytes[(int)position..];
        if (!zip64.StartsWith("PK\x06\x06"u8))
            throw new InvalidDataException("Missing ZIP64 directory footer.");
        return BinaryPrimitives.ReadUInt64LittleEndian(zip64[32..]);
    }

    private async Task ReadJsonAsync(Stream stream, string source, CancellationToken cancellationToken)
    {
        using var bytes = new MemoryStream();
        await CopyAsync(stream, bytes, options.MaximumExpandedBytes - _expandedBytes,
            $"{source}: the selection exceeds the {options.MaximumExpandedBytes}-byte expanded limit.", cancellationToken);
        _expandedBytes += bytes.Length;
        bytes.Position = 0;
        try
        {
            using var document = await JsonDocument.ParseAsync(bytes, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0)
                    throw new ReusableActivityImportPayloadException($"{source}: the JSON array contains no workflow definitions.");
                foreach (var item in root.EnumerateArray())
                    AddDefinition(item, source, cancellationToken);
            }
            else
                AddDefinition(root, source, cancellationToken);
        }
        catch (JsonException e)
        {
            throw new ReusableActivityImportPayloadException($"{source}: invalid workflow definition JSON.", e);
        }
    }

    private void AddDefinition(JsonElement item, string source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_definitions.Count >= options.MaximumSourceVersions)
            throw new ReusableActivityImportPayloadException($"{source}: the selection exceeds the {options.MaximumSourceVersions}-source-version limit.");
        var definition = item.ValueKind == JsonValueKind.Object
            ? item.Deserialize<Elsa3WorkflowDefinition>(JsonSerializerOptions.Web)
            : null;
        if (definition is null || string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.DefinitionId))
            throw new ReusableActivityImportPayloadException($"{source}: expected a workflow definition with an id and definitionId.");
        _definitions.Add(definition);
    }

    private async Task CopyAsync(Stream input, MemoryStream output, long limit, string error, CancellationToken cancellationToken)
    {
        int read;
        while ((read = await input.ReadAsync(_buffer, cancellationToken)) > 0)
        {
            if (read > limit - output.Length)
                throw new ReusableActivityImportPayloadException(error);
            output.Write(_buffer, 0, read);
        }
    }
}
