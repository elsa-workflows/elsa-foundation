using Elsa3.Activities.Design.Import.Reconciliation.Exceptions;
using Elsa3.Activities.Design.Import.Reconciliation.Options;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>Spec 235 FR-002, FR-003 and the configuration edge cases: what the file reader reads and refuses.</summary>
public sealed class Elsa3JsonFileReaderTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("elsa3-reader-");
    private readonly Elsa3JsonFileReader _reader = new(NullLogger<Elsa3JsonFileReader>.Instance);

    [Fact]
    public async Task A_folder_is_read_top_level_in_ordinal_file_name_order_one_definition_or_an_array_per_file()
    {
        Write("b.json", """[{ "id": "b-v1", "definitionId": "b", "version": 1 }, { "id": "b-v2", "definitionId": "b", "version": 2 }]""");
        Write("a.json", """{ "id": "a-v1", "definitionId": "a", "version": 1 }""");
        Write("notes.txt", "ignored");
        Directory.CreateDirectory(Path.Join(_folder.FullName, "nested"));
        File.WriteAllText(Path.Join(_folder.FullName, "nested", "c.json"), """{ "id": "c-v1", "definitionId": "c", "version": 1 }""");

        var items = await _reader.ReadAsync(new Elsa3JsonReconciliationOptions { SourceId = "test", FolderPath = _folder.FullName }, CancellationToken.None);

        Assert.Equal(["a-v1", "b-v1", "b-v2"], items.Select(item => item.Definition.Id));
        Assert.Equal(Path.Join(_folder.FullName, "a.json"), items[0].FilePath);
    }

    [Fact]
    public async Task An_empty_folder_reads_nothing()
    {
        var items = await _reader.ReadAsync(new Elsa3JsonReconciliationOptions { SourceId = "test", FolderPath = _folder.FullName }, CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task A_missing_folder_fails_naming_the_path()
    {
        var missing = Path.Join(_folder.FullName, "missing");

        var exception = await Assert.ThrowsAsync<Elsa3ReconciliationFailedException>(() =>
            _reader.ReadAsync(new Elsa3JsonReconciliationOptions { SourceId = "test", FolderPath = missing }, CancellationToken.None));

        Assert.Contains(missing, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("\"a string\"")]
    [InlineData("""[{ "id": "a-v1", "definitionId": "a", "version": 1 }, null]""")]
    public async Task A_file_that_is_not_a_definition_or_an_array_of_them_fails_naming_the_path(string content)
    {
        var path = Write("bad.json", content);

        var exception = await Assert.ThrowsAsync<Elsa3ReconciliationFailedException>(() =>
            _reader.ReadAsync(new Elsa3JsonReconciliationOptions { SourceId = "test", FilePath = path }, CancellationToken.None));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Files_are_read_in_ascending_order()
    {
        var second = Write("second.json", """{ "id": "second-v1", "definitionId": "second", "version": 1 }""");
        var first = Write("first.json", """{ "id": "first-v1", "definitionId": "first", "version": 1 }""");

        var items = await _reader.ReadAsync(new Elsa3JsonReconciliationOptions
        {
            SourceId = "test",
            Files = [new Elsa3JsonFileOption(2, second), new Elsa3JsonFileOption(1, first)]
        }, CancellationToken.None);

        Assert.Equal(["first-v1", "second-v1"], items.Select(item => item.Definition.Id));
    }

    private string Write(string fileName, string content)
    {
        var path = Path.Join(_folder.FullName, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            _folder.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Cleaned with the temp folder later.
        }
    }
}
