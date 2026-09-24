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
}
