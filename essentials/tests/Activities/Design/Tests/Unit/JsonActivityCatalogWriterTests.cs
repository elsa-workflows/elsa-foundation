using System.Text.Json;
using Elsa.Activities.Design.Core.Reconciliation.Models;
using Elsa.Activities.Design.Reconciliation.Json.Services;
using Elsa.Serialization.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// <see cref="JsonActivityCatalogWriter"/> writes what <see cref="JsonActivityCatalogReader"/> reads:
/// read(write(x)) = x, the opaque descriptor included, and the same models always give the same indented bytes.
/// </summary>
public sealed class JsonActivityCatalogWriterTests : IDisposable
{
    private const string FixtureJson =
        """
        [
          {
            "id": null,
            "version": "1.2.3",
            "activityTypeKey": "Acme.Activities.SendEmail",
            "displayName": "Send Email <é>",
            "category": "Communication",
            "description": "Sends an email to a recipient.",
            "providerKey": "elsa.activity-graph",
            "providerSchemaVersion": "1",
            "consumerKey": "elsa.activity-graph",
            "consumerSchemaVersion": "1",
            "descriptor": { "zeta": 1.50, "alpha": [ true, null, "x" ] },
            "inputs": [],
            "outputs": [],
            "designFacets": []
          },
          {
            "version": "2.0.0",
            "activityTypeKey": "Acme.Activities.Log",
            "providerKey": "elsa.clr-activity",
            "providerSchemaVersion": "1",
            "consumerKey": "elsa.clr-activity",
            "consumerSchemaVersion": "1",
            "descriptor": { "typeAlias": "Acme.Activities.Log" },
            "inputs": [],
            "outputs": [],
            "designFacets": []
          }
        ]
        """;

    private readonly string _folder = Path.Join(Path.GetTempPath(), $"activity-catalog-writer-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Read_of_written_catalog_returns_the_written_models()
    {
        var models = ReadFixture();

        var path = Path.Join(_folder, "nested", "activities.json");
        CreateWriter().Write(path, models, CancellationToken.None);
        var readBack = CreateReader().Read(path, CancellationToken.None);

        Assert.Equal(2, readBack.Count);
        Assert.Equal(["Acme.Activities.SendEmail", "Acme.Activities.Log"], readBack.Select(model => model.ActivityTypeKey));
        Assert.Equal("Send Email <é>", readBack[0].DisplayName);
        var descriptor = Assert.IsType<JsonElement>(readBack[0].Descriptor);
        Assert.Equal("1.50", descriptor.GetProperty("zeta").GetRawText());
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

    private IReadOnlyList<ActivityVersionReconciliationModel> ReadFixture()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Join(_folder, "fixture.json");
        File.WriteAllText(path, FixtureJson);
        return CreateReader().Read(path, CancellationToken.None);
    }

    private static JsonActivityCatalogReader CreateReader() =>
        new(SerializationServices.PayloadSerializer(), NullLogger<JsonActivityCatalogReader>.Instance);

    private static JsonActivityCatalogWriter CreateWriter() =>
        new(SerializationServices.PayloadSerializer(), NullLogger<JsonActivityCatalogWriter>.Instance);
}
