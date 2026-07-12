using Elsa.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Documents.Store;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Tests;

public sealed class SqliteGroundworkRuntimePersistenceShellFeatureTests
{
    private const string CacheEnabledProperty = "CacheWorkflowExecutables";
    private const string CacheCapacityProperty = "WorkflowExecutableCacheCapacity";

    [Fact]
    public void FeatureType_IsInheritable()
    {
        Assert.False(typeof(SqliteGroundworkRuntimePersistenceShellFeature).IsSealed);
        Assert.True(typeof(SqliteGroundworkRuntimePersistenceShellFeature)
            .GetMethod(nameof(SqliteGroundworkRuntimePersistenceShellFeature.ConfigureServices))!
            .IsVirtual);
    }

    [Fact]
    public void RematerializeOnStartup_IsAnOperatorSetting_DefaultingToFalse()
    {
        var featureType = typeof(SqliteGroundworkRuntimePersistenceShellFeature);
        var property = featureType.GetProperty("RematerializeOnStartup");

        Assert.NotNull(property);
        Assert.Equal(typeof(bool), property!.PropertyType);
        Assert.False(Assert.IsType<bool>(property.GetValue(Activator.CreateInstance(featureType))));
        Assert.Contains(property.CustomAttributes, attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
    }

    [Fact]
    public async Task ConfiguredRematerializationSetting_IsThreadedToInitializerAndRepairsProjection()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-groundwork-feature-{Guid.NewGuid():N}.db");

        try
        {
            await InitializeFeatureStoreAsync(databasePath, rematerializeOnStartup: false, async store =>
            {
                await store.SaveAsync(new SaveDocumentRequest(
                    ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
                    "artifact-1",
                    "1.0.0",
                    $$"""{"collection":"{{ElsaRuntimeStorageManifest.WorkflowExecutableCollection}}"}"""));
            });
            await DeleteExecutableIndexProjectionAsync(databasePath);

            await InitializeFeatureStoreAsync(databasePath, rematerializeOnStartup: true, async store =>
            {
                var results = await store.QueryAsync(new DocumentStoreQuery(
                    ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind,
                    ElsaRuntimeStorageManifest.WorkflowExecutableByCollection,
                    ElsaRuntimeStorageManifest.WorkflowExecutableCollection));
                Assert.Single(results);
            });
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Fact]
    public void ExecutableCacheSettings_ArePublicManifestSettings_WithDurableDefaults()
    {
        var feature = new SqliteGroundworkRuntimePersistenceShellFeature();

        AssertSetting(feature, CacheEnabledProperty, expectedDefault: true);
        AssertSetting(feature, CacheCapacityProperty, expectedDefault: 256);
    }

    [Fact]
    public void DisabledExecutableCacheSetting_IsThreadedToRuntimeRegistration()
    {
        var feature = new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = "Data Source=:memory:" };
        SetSetting(feature, CacheEnabledProperty, false);
        SetSetting(feature, CacheCapacityProperty, 17);
        var services = new ServiceCollection();
        feature.ConfigureServices(services);

        var options = Assert.IsType<WorkflowExecutableCacheOptions>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(WorkflowExecutableCacheOptions)).ImplementationInstance);
        Assert.False(options.Enabled);
        Assert.Equal(17, options.Capacity);
    }

    private static async Task InitializeFeatureStoreAsync(
        string databasePath,
        bool rematerializeOnStartup,
        Func<IDocumentStore, Task> assertion)
    {
        var feature = new SqliteGroundworkRuntimePersistenceShellFeature { ConnectionString = $"Data Source={databasePath}" };
        var setting = feature.GetType().GetProperty("RematerializeOnStartup");
        Assert.NotNull(setting);
        setting!.SetValue(feature, rematerializeOnStartup);

        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<SqliteGroundworkDocumentStoreInitializer>().InitializeAsync();
        await assertion(provider.GetRequiredService<IDocumentStore>());
    }

    private static async Task DeleteExecutableIndexProjectionAsync(string databasePath)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM groundwork_document_indexes
            WHERE document_kind = $kind AND index_name = $index;
            """;
        command.Parameters.AddWithValue("$kind", ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind);
        command.Parameters.AddWithValue("$index", ElsaRuntimeStorageManifest.WorkflowExecutableByCollection);
        await command.ExecuteNonQueryAsync();
    }

    private static void DeleteDatabase(string databasePath)
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void AssertSetting(object feature, string propertyName, object expectedDefault)
    {
        var property = feature.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedDefault, property!.GetValue(feature));
        Assert.Contains(property.CustomAttributes, attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
    }

    private static void SetSetting(object feature, string propertyName, object value)
    {
        var property = feature.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        property!.SetValue(feature, value);
    }
}
