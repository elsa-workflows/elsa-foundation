using System.Text;
using CShells;
using CShells.Lifecycle;
using Elsa.Modularity.EntityFramework;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Modularity.EntityFramework.Tests;

public sealed class PersistenceResourceReloadTests
{
    private const string FeatureId = "WorkflowsRuntimeEntityFrameworkCore";

    [Fact]
    public async Task Authored_file_reload_changes_the_next_preparation_without_mutating_the_prior_patch()
    {
        var directory = Directory.CreateTempSubdirectory("elsa-persistence-reload-").FullName;
        var path = Path.Combine(directory, "appsettings.json");
        try
        {
            WriteResource(path, "Sqlite", "First");
            using var root = (ConfigurationRoot)new ConfigurationBuilder()
                .AddJsonFile(path, optional: false, reloadOnChange: true).Build();
            var preparer = new EfPersistenceShellSettingsPreparer(root);
            var context = Context();

            var first = await preparer.PrepareAsync(context);
            Assert.Equal("First", first.ConfigurationData[$"{FeatureId}:ConnectionName"]);

            await ChangeAndWaitAsync(root, path, "Sqlite", "Second");
            var second = await preparer.PrepareAsync(context);
            Assert.Equal("Second", second.ConfigurationData[$"{FeatureId}:ConnectionName"]);
            Assert.Equal("First", first.ConfigurationData[$"{FeatureId}:ConnectionName"]);

            await ChangeAndWaitAsync(root, path, "Oracle", "Second");
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => preparer.PrepareAsync(context));
            Assert.Contains("resource-", failure.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("source-value-canary", failure.ToString(), StringComparison.Ordinal);
            Assert.Equal("Second", second.ConfigurationData[$"{FeatureId}:ConnectionName"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Source_change_during_snapshot_capture_refuses_before_returning_a_patch()
    {
        var root = new ConfigurationBuilder().AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("Elsa:Persistence:DefaultResource", "primary"),
            new KeyValuePair<string, string?>("Elsa:Persistence:Resources:primary:Provider", "Sqlite"),
            new KeyValuePair<string, string?>("Elsa:Persistence:Resources:primary:ConnectionName", "First"),
            new KeyValuePair<string, string?>("ConnectionStrings:First", "Data Source=first.db;Password=source-value-canary")
        ]).Build();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfPersistenceShellSettingsPreparer(new ReloadDuringReadConfiguration(root)).PrepareAsync(Context()));

        Assert.Contains("configuration-changed", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("source-value-canary", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unexpected_configuration_error_is_redacted_before_it_reaches_shell_management()
    {
        var root = new ConfigurationBuilder().Build();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfPersistenceShellSettingsPreparer(new ThrowingConfiguration(root)).PrepareAsync(Context()));

        Assert.Equal("EF persistence preparation refused: configuration-invalid", failure.Message);
        Assert.DoesNotContain("source-value-canary", failure.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Layered_resource_override_and_explicit_null_keep_their_meaning_in_the_snapshot()
    {
        using var authored = Json("""
            { "Elsa": { "Persistence": {
                "DefaultResource": "primary",
                "Resources": { "primary": { "Provider": "Sqlite", "ConnectionName": "First" } }
              } }, "ConnectionStrings": { "First": "Data Source=first.db;Password=source-value-canary" } }
            """);
        using var overrideFile = Json("""
            { "Elsa": { "Persistence": { "Resources": {
                "primary": { "ConnectionName": "Second" }
              } } }, "ConnectionStrings": { "Second": "Data Source=second.db;Password=source-value-canary" } }
            """);
        var root = new ConfigurationBuilder().AddJsonStream(authored).AddJsonStream(overrideFile).Build();

        var patch = await new EfPersistenceShellSettingsPreparer(root).PrepareAsync(Context());

        Assert.Equal("Sqlite", patch.ConfigurationData[$"{FeatureId}:Provider"]);
        Assert.Equal("Second", patch.ConfigurationData[$"{FeatureId}:ConnectionName"]);

        using var nullSelection = Json("""{ "Elsa": { "Persistence": { "DefaultResource": null } } }""");
        var invalid = new ConfigurationBuilder()
            .AddInMemoryCollection(root.AsEnumerable())
            .AddJsonStream(nullSelection)
            .Build();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EfPersistenceShellSettingsPreparer(invalid).PrepareAsync(Context()));
        Assert.Contains("resource-selection-invalid", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("source-value-canary", failure.ToString(), StringComparison.Ordinal);
    }

    private static async Task ChangeAndWaitAsync(IConfigurationRoot root, string path, string provider, string connectionName)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callback = root.GetReloadToken().RegisterChangeCallback(_ => changed.TrySetResult(), null);
        WriteResource(path, provider, connectionName);
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static void WriteResource(string path, string provider, string connectionName)
    {
        var content = $$"""
                        {
                          "Elsa": {
                            "Persistence": {
                              "DefaultResource": "primary",
                              "Resources": {
                                "primary": {
                                  "Provider": "{{provider}}",
                                  "ConnectionName": "{{connectionName}}"
                                }
                              }
                            }
                          },
                          "ConnectionStrings": {
                            "First": "Data Source=first.db;Password=source-value-canary",
                            "Second": "Data Source=second.db;Password=source-value-canary"
                          }
                        }
                        """;
        var candidate = path + ".candidate";
        File.WriteAllText(candidate, content);
        File.Move(candidate, path, overwrite: true);
    }

    private static ShellSettingsPreparationContext Context() =>
        new(new ShellId("default"), new Dictionary<string, string?>(), [FeatureId], [], [],
            [new ShellFeaturePreparationDescriptor(FeatureId, [], typeof(RuntimeEntityFrameworkCoreFeature), false)],
            [FeatureId], [], []);

    private static MemoryStream Json(string value) => new(Encoding.UTF8.GetBytes(value));

    private sealed class ReloadDuringReadConfiguration(IConfigurationRoot inner) : IConfiguration
    {
        public string? this[string key]
        {
            get => inner[key];
            set => inner[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren()
        {
            inner.Reload();
            return inner.GetChildren();
        }

        public IChangeToken GetReloadToken() => inner.GetReloadToken();
        public IConfigurationSection GetSection(string key) => inner.GetSection(key);
    }

    private sealed class ThrowingConfiguration(IConfigurationRoot inner) : IConfiguration
    {
        public string? this[string key]
        {
            get => inner[key];
            set => inner[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() =>
            throw new InvalidOperationException("source-value-canary");

        public IChangeToken GetReloadToken() => inner.GetReloadToken();
        public IConfigurationSection GetSection(string key) => inner.GetSection(key);
    }
}
