using System.Text;
using System.Xml.Serialization;
using Elsa.Http.Core.Models;
using Elsa.Http.Services;
using Xunit;

namespace Elsa.Http.Tests;

public class XmlHttpContentParserTests
{
    private readonly XmlHttpContentParser parser = new();

    [Fact]
    public async Task ReadAsync_WithTypedReturnType_DeserializesXmlContent()
    {
        var context = CreateContext(SerializeToXml(new TestPayload { Name = "Alice", Age = 42 }), typeof(TestPayload));

        var result = await parser.ReadAsync(context);

        var payload = Assert.IsType<TestPayload>(result);
        Assert.Equal("Alice", payload.Name);
        Assert.Equal(42, payload.Age);
    }

    [Fact]
    public async Task ReadAsync_WithStringReturnType_ReturnsRawXml()
    {
        var xml = SerializeToXml(new TestPayload { Name = "Bob", Age = 7 });
        var context = CreateContext(xml, typeof(string));

        var result = await parser.ReadAsync(context);

        Assert.Equal(xml, result);
    }

    [Fact]
    public async Task ReadAsync_WithNullReturnType_ReturnsRawXml()
    {
        var xml = SerializeToXml(new TestPayload { Name = "Carol", Age = 21 });
        var context = CreateContext(xml, null);

        var result = await parser.ReadAsync(context);

        Assert.Equal(xml, result);
    }

    private static HttpResponseParserContext CreateContext(string xml, Type? returnType) =>
        new(
            new MemoryStream(Encoding.UTF8.GetBytes(xml)),
            "application/xml",
            returnType,
            new Dictionary<string, string[]>(),
            CancellationToken.None);

    private static string SerializeToXml(TestPayload payload)
    {
        var serializer = new XmlSerializer(typeof(TestPayload));
        using var writer = new StringWriter();
        serializer.Serialize(writer, payload);
        return writer.ToString();
    }

    public class TestPayload
    {
        public string? Name { get; set; }
        public int Age { get; set; }
    }
}
