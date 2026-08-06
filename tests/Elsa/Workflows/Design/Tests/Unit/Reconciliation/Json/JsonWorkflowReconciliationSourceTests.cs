using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Reconciliation.Json.Contracts;
using Elsa.Workflows.Design.Reconciliation.Json.Exceptions;
using Elsa.Workflows.Design.Reconciliation.Json.Options;
using Elsa.Workflows.Design.Reconciliation.Json.Services;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Json;

/// <summary>
/// Behavior of the JSON workflow reconciliation source (file selection + concatenation) and its catalog
/// reader (deserialize via <see cref="IPayloadSerializer"/>; every IO/parse fault becomes an actionable
/// <see cref="InvalidWorkflowCatalogJsonException"/>).
/// </summary>
public sealed class JsonWorkflowReconciliationSourceTests
{
    private static WorkflowVersionReconciliationModel Model(string id, string version) =>
        new(id, id, null, version, WorkflowDefinitionState.Empty);

    [Fact]
    public async Task Read_WithSingleFilePath_ReadsThatFile()
    {
        var reader = new StubReader { ["only.json"] = [Model("a", "1.0.0")] };
        var source = NewSource(reader, new() { SourceId = "s", FilePath = "only.json" });

        var result = (await source.Read(CancellationToken.None)).ToArray();

        Assert.Single(result);
        Assert.Equal("a", result[0].DefinitionId);
        Assert.Equal(new[] { "only.json" }, reader.ReadPaths);
    }

    [Fact]
    public async Task Read_WithFilesList_ReadsInOrderAndConcatenates()
    {
        var reader = new StubReader
        {
            ["first.json"] = [Model("a", "1.0.0")],
            ["second.json"] = [Model("b", "1.0.0"), Model("c", "1.0.0")],
        };
        var options = new JsonWorkflowReconciliationOptions
        {
            SourceId = "s",
            Files =
            [
                new JsonWorkflowReconciliationFileOption(2, "second.json"),
                new JsonWorkflowReconciliationFileOption(1, "first.json"),
            ],
        };
        var source = NewSource(reader, options);

        var result = (await source.Read(CancellationToken.None)).ToArray();

        Assert.Equal(new[] { "a", "b", "c" }, result.Select(m => m.DefinitionId));
        Assert.Equal(new[] { "first.json", "second.json" }, reader.ReadPaths); // ordered by Order, not declaration
    }

    [Fact]
    public async Task Read_WithFolderPath_ScansTopLevelJsonInOrdinalNameOrder()
    {
        var folder = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), $"wf-defs-{Guid.NewGuid():N}")).FullName;
        try
        {
            // Ordinal name order: "10.json" < "2.json" ('1' < '2'), then "a.json" < "b.json".
            File.WriteAllText(Path.Join(folder, "b.json"), "[]");
            File.WriteAllText(Path.Join(folder, "10.json"), "[]");
            File.WriteAllText(Path.Join(folder, "a.json"), "[]");
            File.WriteAllText(Path.Join(folder, "2.json"), "[]");
            File.WriteAllText(Path.Join(folder, "notes.txt"), "ignored — not *.json");
            var nested = Directory.CreateDirectory(Path.Join(folder, "nested")).FullName;
            File.WriteAllText(Path.Join(nested, "sub.json"), "[]"); // ignored — scan is non-recursive

            var reader = new StubReader();
            var source = NewSource(reader, new() { SourceId = "s", FolderPath = folder });

            await source.Read(CancellationToken.None);

            Assert.Equal(
                new[] { "10.json", "2.json", "a.json", "b.json" },
                reader.ReadPaths.Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Read_WithEmptyFolder_ContributesNothingWithoutThrowing()
    {
        var folder = Directory.CreateDirectory(Path.Join(Path.GetTempPath(), $"wf-defs-{Guid.NewGuid():N}")).FullName;
        try
        {
            var reader = new StubReader();
            var source = NewSource(reader, new() { SourceId = "s", FolderPath = folder });

            var result = await source.Read(CancellationToken.None);

            Assert.Empty(result);
            Assert.Empty(reader.ReadPaths);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task Read_WithMissingFolder_ThrowsInvalidWorkflowCatalogJson()
    {
        var missing = Path.Join(Path.GetTempPath(), $"wf-defs-nope-{Guid.NewGuid():N}");
        var source = NewSource(new StubReader(), new() { SourceId = "s", FolderPath = missing });

        var ex = await Assert.ThrowsAsync<InvalidWorkflowCatalogJsonException>(
            async () => await source.Read(CancellationToken.None));
        Assert.Equal(missing, ex.FilePath);
    }

    [Fact]
    public async Task Read_EntryWithoutDefinitionId_LogsAPinYourIdWarning()
    {
        // An omitted definitionId mints a fresh random id per restart → duplicate definitions. The
        // import proceeds, but the source must surface the footgun (spec 147 edge case).
        var reader = new StubReader { ["only.json"] = [Model(null!, "1.0.0") with { Name = "anon" }] };
        var logger = new CapturingLogger<JsonWorkflowReconciliationSource>();
        var source = new JsonWorkflowReconciliationSource(reader, Opts(new() { SourceId = "s", FilePath = "only.json" }), logger);

        var result = (await source.Read(CancellationToken.None)).ToArray();

        Assert.Single(result); // still imported
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("definitionId") && e.Message.Contains("anon"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestsPublication_MirrorsThePublishOnReconcileOption(bool publishOnReconcile)
    {
        var source = NewSource(new StubReader(), new()
        {
            SourceId = "s",
            FilePath = "only.json",
            PublishOnReconcile = publishOnReconcile,
        });

        Assert.Equal(publishOnReconcile, source.RequestsPublication);
    }

    [Fact]
    public void Reader_MissingFile_ThrowsInvalidWorkflowCatalogJson()
    {
        var reader = new JsonWorkflowCatalogReader(new ThrowingSerializer(), NullLogger<JsonWorkflowCatalogReader>.Instance);
        var missing = Path.Join(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json");

        var ex = Assert.Throws<InvalidWorkflowCatalogJsonException>(() => reader.Read(missing, CancellationToken.None));
        Assert.Equal(missing, ex.FilePath);
    }

    [Fact]
    public void Reader_ValidFile_ReturnsDeserializedModels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "[]"); // content is irrelevant; the stub serializer decides the result
        try
        {
            var models = new[] { Model("a", "1.0.0"), Model("b", "2.0.0") };
            var reader = new JsonWorkflowCatalogReader(new StubSerializer(models), NullLogger<JsonWorkflowCatalogReader>.Instance);

            var result = reader.Read(path, CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.Equal(new[] { "a", "b" }, result.Select(m => m.DefinitionId));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reader_NullDeserialization_Throws()
    {
        var path = Path.Join(Path.GetTempPath(), $"catalog-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "null");
        try
        {
            var reader = new JsonWorkflowCatalogReader(new StubSerializer(null), NullLogger<JsonWorkflowCatalogReader>.Instance);
            Assert.Throws<InvalidWorkflowCatalogJsonException>(() => reader.Read(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static IOptions<JsonWorkflowReconciliationOptions> Opts(JsonWorkflowReconciliationOptions o) => Options.Create(o);

    private static JsonWorkflowReconciliationSource NewSource(IJsonWorkflowCatalogReader reader, JsonWorkflowReconciliationOptions options) =>
        new(reader, Opts(options), NullLogger<JsonWorkflowReconciliationSource>.Instance);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }

    private sealed class StubReader : IJsonWorkflowCatalogReader
    {
        private readonly Dictionary<string, IReadOnlyList<WorkflowVersionReconciliationModel>> _byPath = new(StringComparer.Ordinal);
        public List<string> ReadPaths { get; } = [];
        public IReadOnlyList<WorkflowVersionReconciliationModel> this[string path] { set => _byPath[path] = value; }

        public IReadOnlyList<WorkflowVersionReconciliationModel> Read(string filePath, CancellationToken cancellationToken)
        {
            ReadPaths.Add(filePath);
            return _byPath.TryGetValue(filePath, out var models) ? models : [];
        }
    }

    private sealed class StubSerializer(WorkflowVersionReconciliationModel[]? result) : IPayloadSerializer
    {
        public T Deserialize<T>(string serializedData) => (T)(object)result!;
        public string Serialize(object payload) => JsonSerializer.Serialize(payload);
        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();
        public object Deserialize(string serializedData) => throw new NotSupportedException();
        public object Deserialize(string serializedData, Type type) => throw new NotSupportedException();
        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();
        public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException();
        public JsonSerializerOptions GetOptions() => throw new NotSupportedException();
    }

    private sealed class ThrowingSerializer : IPayloadSerializer
    {
        public T Deserialize<T>(string serializedData) => throw new JsonException("should not be reached for a missing file");
        public string Serialize(object payload) => throw new NotSupportedException();
        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();
        public object Deserialize(string serializedData) => throw new NotSupportedException();
        public object Deserialize(string serializedData, Type type) => throw new NotSupportedException();
        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();
        public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException();
        public JsonSerializerOptions GetOptions() => throw new NotSupportedException();
    }
}
