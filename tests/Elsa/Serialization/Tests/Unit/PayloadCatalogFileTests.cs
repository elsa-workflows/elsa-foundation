using System.Text.Json;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Serialization.Tests.Unit;

public sealed class PayloadCatalogFileTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public void ReadArray_MissingPath_ThrowsFactoryException()
    {
        var path = Path.Join(Path.GetTempPath(), "payload-catalog-missing-" + Guid.NewGuid().ToString("N") + ".json");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PayloadCatalogFile.ReadArray<string>(path, new StubSerializer(), Factory));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("the file does not exist.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadArray_EmptyPath_ThrowsFactoryException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PayloadCatalogFile.ReadArray<string>("", new StubSerializer(), Factory));

        Assert.Contains("no file path was configured.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadArray_InvalidJson_ThrowsFactoryException()
    {
        var path = WriteTemp("{ this is not valid json");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PayloadCatalogFile.ReadArray<string>(path, new StubSerializer(), Factory));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("the file is not a valid JSON array of reconciliation models.", exception.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void ReadArray_NullArray_ThrowsFactoryException()
    {
        var path = WriteTemp("null");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PayloadCatalogFile.ReadArray<string>(path, new StubSerializer(), Factory));

        Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        Assert.Contains("the file deserialized to null.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadArray_ValidArray_ReturnsModels()
    {
        var path = WriteTemp("""["alpha","beta"]""");

        var models = PayloadCatalogFile.ReadArray<string>(path, new StubSerializer(), Factory);

        Assert.Equal(["alpha", "beta"], models);
    }

    private static Exception Factory(string path, string reason, Exception? inner) =>
        new InvalidOperationException($"{path}: {reason}", inner);

    private string WriteTemp(string content)
    {
        var fileName = "payload-catalog-" + Guid.NewGuid().ToString("N") + ".json";
        var path = Path.Join(Path.GetTempPath(), fileName);
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); }
            catch (IOException) { /* best-effort */ }
        }
    }

    private sealed class StubSerializer : IPayloadSerializer
    {
        public string Serialize(object payload) => throw new NotSupportedException();
        public JsonElement SerializeToElement(object payload) => throw new NotSupportedException();
        public object Deserialize(string serializedData) => throw new NotSupportedException();
        public object Deserialize(string serializedData, Type type) => throw new NotSupportedException();
        public object Deserialize(JsonElement serializedData) => throw new NotSupportedException();
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData)!;
        public T Deserialize<T>(JsonElement serializedData) => throw new NotSupportedException();
        public JsonSerializerOptions GetOptions() => throw new NotSupportedException();
    }
}
