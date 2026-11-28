using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Reconciliation.Json.Services;
using Elsa.Workflows.Design.Reconciliation.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation.Json;

/// <summary>
/// <see cref="JsonWorkflowCatalogWriter"/> writes what <see cref="JsonWorkflowCatalogReader"/> reads:
/// read(write(x)) = x, and the same models always give the same indented bytes.
/// </summary>
public sealed class JsonWorkflowCatalogWriterTests : IDisposable
{
    private readonly string _folder = Path.Join(Path.GetTempPath(), $"workflow-catalog-writer-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Read_of_written_catalog_returns_the_written_models()
    {
        var models = ReadFixture();

        var path = Path.Join(_folder, "nested", "catalog.json");
        CreateWriter().Write(path, models, CancellationToken.None);
        var readBack = CreateReader().Read(path, CancellationToken.None);

        var model = Assert.Single(readBack);
        Assert.Equal("fixture-catalog", model.DefinitionId);
        Assert.Equal("Catalog sentinel", model.Name);
        Assert.Equal("1.0.0", model.Version);
        Assert.Equal("FixtureName", Assert.Single(model.State.Variables).Name);
        var serializer = SerializationServices.PayloadSerializer();
        Assert.Equal(serializer.Serialize(models.ToArray()), serializer.Serialize(readBack.ToArray()));
    }

    [Fact]
    public void Same_models_give_the_same_indented_bytes()
    {
        var models = ReadFixture();
        var first = Path.Join(_folder, "first.json");
        var second = Path.Join(_folder, "second.json");
        var again = Path.Join(_folder, "again.json");

        CreateWriter().Write(first, models, CancellationToken.None);
        CreateWriter().Write(second, models, CancellationToken.None);
        CreateWriter().Write(again, CreateReader().Read(first, CancellationToken.None), CancellationToken.None);

        var bytes = File.ReadAllBytes(first);
        Assert.Equal(bytes, File.ReadAllBytes(second));
        Assert.Equal(bytes, File.ReadAllBytes(again));
        var text = File.ReadAllText(first);
        Assert.StartsWith("[\n  {\n    \"", text, StringComparison.Ordinal);
        Assert.EndsWith("]\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', text);
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void Empty_list_writes_an_empty_array()
    {
        var path = Path.Join(_folder, "empty.json");

        CreateWriter().Write(path, [], CancellationToken.None);

        Assert.Equal("[]\n", File.ReadAllText(path));
        Assert.Empty(CreateReader().Read(path, CancellationToken.None));
    }

    private IReadOnlyList<WorkflowVersionReconciliationModel> ReadFixture()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Join(_folder, "fixture.json");
        File.WriteAllText(path, JsonWorkflowReconciliationSourceTests.CurrentCatalogFixtureJson);
        return CreateReader().Read(path, CancellationToken.None);
    }

    private static JsonWorkflowCatalogReader CreateReader() =>
        new(SerializationServices.PayloadSerializer(), NullLogger<JsonWorkflowCatalogReader>.Instance);

    private static JsonWorkflowCatalogWriter CreateWriter() =>
        new(SerializationServices.PayloadSerializer(), NullLogger<JsonWorkflowCatalogWriter>.Instance);
}
