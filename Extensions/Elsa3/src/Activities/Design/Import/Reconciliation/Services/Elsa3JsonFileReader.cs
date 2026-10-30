using System.Text.Json;
using Elsa3.Activities.Design.Import.Reconciliation.Exceptions;
using Elsa3.Activities.Design.Import.Reconciliation.Options;
using Elsa3.Models;
using Microsoft.Extensions.Logging;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>One Elsa 3 workflow definition read from a configured file.</summary>
internal sealed record Elsa3InputItem(string FilePath, Elsa3WorkflowDefinition Definition);

/// <summary>
/// Reads the configured files into <see cref="Elsa3InputItem"/>s. A file holds one Elsa 3 workflow definition (an Elsa
/// 3 Studio export) or a JSON array of them, read with the options the collection upload uses. The upload's size and
/// count limits do not apply: the files are host configuration (spec 235 FR-003).
/// </summary>
internal sealed class Elsa3JsonFileReader(ILogger<Elsa3JsonFileReader> logger)
{
    // The collection upload reads with the same options (ReusableActivityImportOperationService).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<Elsa3InputItem>> ReadAsync(Elsa3JsonReconciliationOptions options, CancellationToken cancellationToken)
    {
        var items = new List<Elsa3InputItem>();
        foreach (var path in EffectiveFiles(options))
            items.AddRange((await ReadFileAsync(path, cancellationToken)).Select(definition => new Elsa3InputItem(path, definition)));
        return items;
    }

    private IEnumerable<string> EffectiveFiles(Elsa3JsonReconciliationOptions options)
    {
        if (options.Files.Any())
            return options.Files.OrderBy(file => file.Order).Select(file => file.FilePath);

        if (!string.IsNullOrWhiteSpace(options.FilePath))
            return [options.FilePath];

        var folderPath = options.FolderPath!;
        if (!Directory.Exists(folderPath))
            throw new Elsa3ReconciliationFailedException($"The Elsa 3 import folder '{folderPath}' does not exist.");

        var files = Directory.EnumerateFiles(folderPath, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
            logger.LogInformation("The Elsa 3 import folder '{FolderPath}' holds no *.json files; nothing is imported.", folderPath);
        return files;
    }

    private static async Task<IReadOnlyList<Elsa3WorkflowDefinition>> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            Elsa3WorkflowDefinition?[] definitions = document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.Deserialize<Elsa3WorkflowDefinition?[]>(Json) ?? [],
                JsonValueKind.Object => [document.RootElement.Deserialize<Elsa3WorkflowDefinition>(Json)],
                _ => throw new Elsa3ReconciliationFailedException(
                    $"The Elsa 3 import file '{path}' must hold one workflow definition or an array of them.")
            };
            if (definitions.Any(definition => definition is null))
                throw new Elsa3ReconciliationFailedException($"The Elsa 3 import file '{path}' holds a null entry where a workflow definition belongs.");
            return definitions!;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Elsa3ReconciliationFailedException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new Elsa3ReconciliationFailedException($"The Elsa 3 import file '{path}' could not be read: {exception.Message}", exception);
        }
    }
}
