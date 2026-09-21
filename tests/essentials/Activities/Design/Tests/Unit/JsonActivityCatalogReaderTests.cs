using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Reconciliation.Json.Exceptions;
using Elsa.Activities.Design.Reconciliation.Json.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Activities.Design.Tests.Unit;

/// <summary>
/// Unit coverage for <see cref="JsonActivityCatalogReader"/>: it deserializes an author-authored JSON
/// array into reconciliation models, preserves arbitrary UI metadata, leaves the polymorphic
/// implementation descriptor as a raw <see cref="JsonElement"/> (kind discriminator intact), and turns
/// missing/malformed files into a domain-scoped fault rather than a raw IO/JSON exception.
/// </summary>
public sealed class JsonActivityCatalogReaderTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private JsonActivityCatalogReader CreateReader() =>
        new(new JsonPayloadSerializer(new JsonPayloadConverterRegistry()), NullLogger<JsonActivityCatalogReader>.Instance);

    [Fact]
    public void Read_DeserializesModels_WithUiMetadataAndDescriptorElement()
    {
        var path = WriteTempJson(
            """
            [
              {
                "version": "1.2.3",
                "activityTypeKey": "Acme.Activities.SendEmail",
                "displayName": "Send Email",
                "category": "Communication",
                "description": "Sends an email to a recipient.",
                "providerKey": "acme.clr-activity",
                "providerSchemaVersion": "1",
                "consumerKey": "acme.clr-activity",
                "consumerSchemaVersion": "1",
                "descriptor": {
                  "typeInfo": {
                    "typeName": "SendEmail",
                    "namespace": "Acme.Activities",
                    "assemblyName": "Acme.Activities",
                    "assemblyVersion": "1.2.3.0"
                  }
                },
                "inputs": [],
                "outputs": [],
                "designFacets": []
              }
            ]
            """);

        var models = CreateReader().Read(path, CancellationToken.None);

        var model = Assert.Single(models);
        Assert.Equal("1.2.3", model.Version);
        Assert.Equal("Acme.Activities.SendEmail", model.ActivityTypeKey);
        Assert.Equal("Send Email", model.DisplayName);
        Assert.Equal("Communication", model.Category);
        Assert.Equal("Sends an email to a recipient.", model.Description);
        Assert.Equal("acme.clr-activity", model.ProviderKey);
        Assert.Equal("1", model.ProviderSchemaVersion);
        Assert.Equal("acme.clr-activity", model.ConsumerKey);
        Assert.Equal("1", model.ConsumerSchemaVersion);

        // The descriptor is left as a raw JsonElement — the runtime constructor that owns the type
        // materializes it; the design domain never deserializes it.
        var descriptor = Assert.IsType<JsonElement>(model.Descriptor);
        Assert.Equal(JsonValueKind.Object, descriptor.ValueKind);
        Assert.True(descriptor.TryGetProperty("typeInfo", out var typeInfo));
        Assert.Equal("SendEmail", typeInfo.GetProperty("typeName").GetString());
    }

    [Fact]
    public void Read_MissingFile_ThrowsDomainScopedFault()
    {
        var path = Path.Combine(Path.GetTempPath(), "json-recon-missing-" + Guid.NewGuid().ToString("N") + ".json");

        var exception = Assert.Throws<InvalidActivityCatalogJsonException>(() => CreateReader().Read(path, CancellationToken.None));
        Assert.Equal(path, exception.FilePath);
    }

    [Fact]
    public void Read_MalformedJson_ThrowsDomainScopedFault()
    {
        var path = WriteTempJson("{ this is not valid json");

        var exception = Assert.Throws<InvalidActivityCatalogJsonException>(() => CreateReader().Read(path, CancellationToken.None));
        Assert.Equal(path, exception.FilePath);
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    private string WriteTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "json-recon-" + Guid.NewGuid().ToString("N") + ".json");
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
}
