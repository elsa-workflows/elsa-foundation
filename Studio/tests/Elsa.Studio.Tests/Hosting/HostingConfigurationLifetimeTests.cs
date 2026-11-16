using Elsa.Studio.Hosting;
using Elsa.Studio.Hosting.Nuplane;
using Elsa.Studio.Tests.Administration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Elsa.Studio.Tests.Hosting;

public sealed class HostingConfigurationLifetimeTests
{
    [Fact]
    public async Task A_composition_failure_before_Build_releases_the_fixture_builder_and_its_file_watchers()
    {
        ConfigurationManager? configuration = null;
        PhysicalFileProvider? defaults = null;
        PhysicalFileProvider? management = null;
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AdministrationHost.StartAsync(
                beforeStudio: builder =>
                {
                    configuration = builder.Configuration;
                    defaults = Assert.IsType<PhysicalFileProvider>(builder.Environment.ContentRootFileProvider);
                },
                configureNuplane: _ =>
                {
                    var provider = Assert.Single(((IConfigurationRoot)configuration!).Providers.OfType<JsonConfigurationProvider>());
                    management = Assert.IsType<PhysicalFileProvider>(provider.Source.FileProvider);
                    throw new InvalidOperationException("fixture composition failed before Build");
                }));
            Assert.Equal("fixture composition failed before Build", error.Message);
            Assert.NotNull(defaults);
            Assert.NotNull(management);
            AssertDisposed(defaults);
            AssertDisposed(management);
        }
        finally
        {
            configuration?.Dispose();
            defaults?.Dispose();
            management?.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disposing_the_composed_host_releases_its_shell_and_management_file_watchers(bool externalFiles)
    {
        await using var host = await ConfigurationHost.StartAsync(externalFiles);
        var providers = host.JsonProviders();
        Assert.Equal(2, providers.Length);
        Assert.All(providers, provider => Assert.True(provider.Source.ReloadOnChange));
        Assert.Equal("initial", host.Configuration["LifetimeProbe"]);

        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = ChangeToken.OnChange(() => ((IConfiguration)host.Configuration).GetReloadToken(), () =>
        {
            if (host.Configuration["LifetimeProbe"] == "updated") reloaded.TrySetResult();
        });
        File.Delete(host.ShellsFile);
        var shellProvider = providers.Single(provider => Path.GetFileName(provider.Source.Path) == "shells.json");
        Assert.Throws<FileNotFoundException>(() => shellProvider.Load());
        await File.WriteAllTextAsync(host.ShellsFile, """{"LifetimeProbe":"updated","CShells":{"Shells":{"Default":{"Features":{"StudioApi":{}}}}}}""");
        await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("updated", host.Configuration["LifetimeProbe"]);

        var files = providers.Select(provider => Assert.IsType<PhysicalFileProvider>(provider.Source.FileProvider)).ToArray();
        try
        {
            await host.App.StopAsync();
            await host.App.DisposeAsync();
            Assert.All(files, AssertDisposed);
            // Host shutdown and provider cleanup may both be requested more than once.
            await host.App.DisposeAsync();
            Assert.All(files, AssertDisposed);
        }
        finally
        {
            foreach (var file in files) file.Dispose();
        }
    }

    [Fact]
    public async Task Rebuilding_or_removing_configuration_sources_releases_old_watchers_and_preserves_remaining_files()
    {
        await using var host = await ConfigurationHost.StartAsync(externalFiles: true);
        await host.App.StopAsync();
        var first = host.JsonProviders().Select(provider => Assert.IsType<PhysicalFileProvider>(provider.Source.FileProvider)).ToArray();
        var rebuilt = Array.Empty<PhysicalFileProvider>();
        var remaining = Array.Empty<PhysicalFileProvider>();
        try
        {
            host.Configuration.Sources.Insert(0, new MemoryConfigurationSource());
            rebuilt = host.JsonProviders().Select(provider => Assert.IsType<PhysicalFileProvider>(provider.Source.FileProvider)).ToArray();
            Assert.All(first, AssertDisposed);
            Assert.Equal("initial", host.Configuration["LifetimeProbe"]);
            Assert.All(rebuilt, file => Assert.NotNull(file.Watch("still-live.json")));

            var shell = host.Configuration.Sources.OfType<JsonConfigurationSource>()
                .Single(source => Path.GetFileName(source.Path) == "shells.json");
            Assert.True(host.Configuration.Sources.Remove(shell));
            remaining = host.JsonProviders().Select(provider => Assert.IsType<PhysicalFileProvider>(provider.Source.FileProvider)).ToArray();
            Assert.All(rebuilt, AssertDisposed);
            Assert.Single(remaining);
            Assert.Null(host.Configuration["LifetimeProbe"]);
            Assert.NotNull(remaining[0].Watch("still-live.json"));

            host.Configuration.Sources.Clear();
            Assert.All(remaining, AssertDisposed);
        }
        finally
        {
            foreach (var file in first.Concat(rebuilt).Concat(remaining)) file.Dispose();
        }
    }

    [Fact]
    public void A_failed_initial_file_load_releases_its_watcher_before_the_provider_can_be_retained()
    {
        var root = Directory.CreateTempSubdirectory("studio-invalid-configuration-").FullName;
        WebApplicationBuilder? builder = null;
        try
        {
            var file = Path.Combine(root, "invalid-shells.json");
            File.WriteAllText(file, "{ invalid json");
            builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = root, EnvironmentName = Environments.Production });
            builder.Configuration.Sources.Clear();
            Assert.Throws<InvalidDataException>(() => builder.AddElsaStudio(options => options.ShellsFile = file));

            // Build exposes the exact provider before its first Load, the same sequence
            // ConfigurationManager uses when registration fails.
            var source = Assert.Single(builder.Configuration.Sources.OfType<JsonConfigurationSource>());
            using var provider = Assert.IsAssignableFrom<JsonConfigurationProvider>(source.Build(builder.Configuration));
            var files = Assert.IsType<PhysicalFileProvider>(provider.Source.FileProvider);
            try
            {
                Assert.Throws<InvalidDataException>(() => provider.Load());
                AssertDisposed(files);
                provider.Dispose();
                AssertDisposed(files);
            }
            finally { files.Dispose(); }
        }
        finally
        {
            builder?.Configuration.Dispose();
            (builder?.Environment.ContentRootFileProvider as IDisposable)?.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static void AssertDisposed(PhysicalFileProvider provider) =>
        Assert.Throws<ObjectDisposedException>(() => provider.Watch("lifetime-probe.json"));

    private sealed class ConfigurationHost : IAsyncDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("studio-configuration-lifetime-").FullName;
        private WebApplicationBuilder? _builder;
        public WebApplication App { get; private set; } = null!;
        public ConfigurationManager Configuration { get; private set; } = null!;
        public string ShellsFile { get; private set; } = null!;

        public static async Task<ConfigurationHost> StartAsync(bool externalFiles)
        {
            var host = new ConfigurationHost();
            try
            {
                var contentRoot = Directory.CreateDirectory(Path.Combine(host._root, "host")).FullName;
                var configRoot = Directory.CreateDirectory(Path.Combine(host._root, externalFiles ? "outside" : "host/config")).FullName;
                var shells = Path.Combine(configRoot, "shells.json");
                host.ShellsFile = shells;
                var management = Path.Combine(configRoot, "management.json");
                await File.WriteAllTextAsync(shells, """{"LifetimeProbe":"initial","CShells":{"Shells":{"Default":{"Features":{"StudioApi":{}}}}}}""");
                var builder = host._builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = contentRoot, EnvironmentName = Environments.Production });
                builder.Logging.ClearProviders();
                builder.WebHost.UseTestServer();
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Nuplane:Setup:AutomaticReconciliation"] = "false",
                    ["Nuplane:Loading:Enabled"] = "false"
                });
                host.Configuration = builder.Configuration;
                builder.AddElsaStudio(options => options.ShellsFile = externalFiles ? shells : "config/shells.json")
                    .AddElsaStudioNuplane(options => options.ManagementFile = externalFiles ? management : "config/management.json");
                host.App = builder.Build();
                host.App.UseElsaStudio();
                host.App.MapElsaStudio();
                await host.App.StartAsync();
                return host;
            }
            catch
            {
                await host.DisposeAsync();
                throw;
            }
        }

        public JsonConfigurationProvider[] JsonProviders() => ((IConfigurationRoot)Configuration).Providers.OfType<JsonConfigurationProvider>().ToArray();

        public async ValueTask DisposeAsync()
        {
            if (App is not null) await App.DisposeAsync();
            else if (_builder is not null)
            {
                _builder.Configuration.Dispose();
                (_builder.Environment.ContentRootFileProvider as IDisposable)?.Dispose();
            }
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
