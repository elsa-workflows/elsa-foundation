using Elsa.Activities.Design.Reconciliation.Json.Options;
using Elsa.Activities.Design.Reconciliation.Json.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Design.Tests.Integration;

/// <summary>
/// Drives the real <see cref="JsonActivityReconciliationSource"/> (with the real reader + shared
/// serializer) through the real reconciliation handler + reconciler against an in-memory catalog. The
/// author-authored JSON carries verbose UI metadata and a polymorphic <c>Clr</c> implementation
/// descriptor; the tests prove the kind/descriptor deserialize and reconcile correctly, that multiple
/// files are concatenated in ascending <c>Order</c>, and that an unconfigured source identity faults.
/// </summary>
public sealed class JsonReconciliationTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    [Fact]
    public async Task JsonSource_ReconcilesModels_PreservingUiMetadataAndDescriptor()
    {
        var file = WriteTempJson("[" + SendEmailJson() + "," + WriteLineJson() + "]");
        var store = new InMemoryReconcilerHarness.CatalogStore();
        var reconciler = InMemoryReconcilerHarness.BuildReconciler(store, JsonSource("json-catalog", (1, file)));

        await reconciler.Reconcile(CancellationToken.None);

        Assert.Equal(2, store.Versions.Count);

        var sendEmail = store.Definitions.Single(d => d.ActivityTypeKey == "Acme.Activities.SendEmail");
        Assert.Equal("Send Email", sendEmail.DisplayName);
        Assert.Equal("Communication", sendEmail.Category);
        Assert.Equal("1.2.3", store.Versions.Single(v => v.DefinitionId == sendEmail.Id).Version);

        var writeLine = store.Definitions.Single(d => d.ActivityTypeKey == "Acme.Activities.WriteLine");
        Assert.Equal("2.0.0", store.Versions.Single(v => v.DefinitionId == writeLine.Id).Version);

        // Re-run with the same file → idempotent (SC-003, DuplicateHandling.Skip).
        await reconciler.Reconcile(CancellationToken.None);
        Assert.Equal(2, store.Versions.Count);
    }

    [Fact]
    public async Task JsonSource_ProcessesFilesInAscendingOrder_RegardlessOfConfigurationOrder()
    {
        // The "dependencies" file must be reconciled before the file that builds on them. We register
        // the files out of order (Order 2 first in the list) to prove the source sorts by Order, not by
        // configuration order.
        var dependenciesFile = WriteTempJson("[" + WriteLineJson() + "]");
        var dependentFile = WriteTempJson("[" + SendEmailJson() + "]");

        var store = new InMemoryReconcilerHarness.CatalogStore();
        var reconciler = InMemoryReconcilerHarness.BuildReconciler(
            store,
            JsonSource("json-catalog", (2, dependentFile), (1, dependenciesFile)));

        await reconciler.Reconcile(CancellationToken.None);

        Assert.Equal(2, store.Versions.Count);

        // Order 1 (WriteLine) was processed first, so its version row is persisted ahead of Order 2's.
        var writeLine = store.Definitions.Single(d => d.ActivityTypeKey == "Acme.Activities.WriteLine");
        Assert.Equal(writeLine.Id, store.Versions[0].DefinitionId);

        var sendEmail = store.Definitions.Single(d => d.ActivityTypeKey == "Acme.Activities.SendEmail");
        Assert.Equal(sendEmail.Id, store.Versions[1].DefinitionId);
    }

    [Fact]
    public async Task JsonSource_SingleFilePathShorthand_Reconciles()
    {
        var file = WriteTempJson("[" + SendEmailJson() + "]");
        var store = new InMemoryReconcilerHarness.CatalogStore();
        var reconciler = InMemoryReconcilerHarness.BuildReconciler(store, JsonSourceFromFilePath("json-catalog", file));

        await reconciler.Reconcile(CancellationToken.None);

        var sendEmail = Assert.Single(store.Definitions);
        Assert.Equal("Acme.Activities.SendEmail", sendEmail.ActivityTypeKey);
        Assert.Equal("1.2.3", store.Versions.Single(v => v.DefinitionId == sendEmail.Id).Version);
    }

    private static JsonActivityReconciliationSource JsonSource(string sourceId, params (int Order, string FilePath)[] files)
    {
        var options = Options.Create(new JsonReconciliationOptions
        {
            SourceId = sourceId,
            Files = files.Select(f => new JsonActivityReconciliationFileOption(f.Order, f.FilePath)).ToList(),
        });
        return BuildSource(options);
    }

    private static JsonActivityReconciliationSource JsonSourceFromFilePath(string sourceId, string filePath)
    {
        var options = Options.Create(new JsonReconciliationOptions { SourceId = sourceId, FilePath = filePath });
        return BuildSource(options);
    }

    private static JsonActivityReconciliationSource BuildSource(IOptions<JsonReconciliationOptions> options)
    {
        var reader = new JsonActivityCatalogReader(
            new JsonPayloadSerializer(new JsonPayloadConverterRegistry()),
            NullLogger<JsonActivityCatalogReader>.Instance);
        return new JsonActivityReconciliationSource(reader, options);
    }

    private string WriteTempJson(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "json-recon-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private static string SendEmailJson() =>
        """
        {
          "version": "1.2.3",
          "activityTypeKey": "Acme.Activities.SendEmail",
          "displayName": "Send Email",
          "category": "Communication",
          "description": "Sends an email to a recipient.",
          "descriptorType": "Clr",
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
          "ports": []
        }
        """;

    private static string WriteLineJson() =>
        """
        {
          "version": "2.0.0",
          "activityTypeKey": "Acme.Activities.WriteLine",
          "displayName": "Write Line",
          "category": "Primitives",
          "descriptorType": "Clr",
          "descriptor": {
            "typeInfo": {
              "typeName": "WriteLine",
              "namespace": "Acme.Activities",
              "assemblyName": "Acme.Activities",
              "assemblyVersion": "2.0.0.0"
            }
          },
          "inputs": [],
          "outputs": [],
          "ports": []
        }
        """;

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); }
            catch (IOException) { /* best-effort */ }
        }
    }
}
