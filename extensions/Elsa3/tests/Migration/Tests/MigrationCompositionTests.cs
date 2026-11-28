using CShells.Lifecycle;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Elsa3.Migration.Tests.Support;
using Elsa3.Migration.Tooling;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa3.Migration.Tests;

/// <summary>Spec 1237 T009, T015 and T026: the migration host's composition, built in process for one run.</summary>
[Collection(nameof(CompositionCollection))]
public sealed class MigrationCompositionTests
{
    private const string Fixture = "AcmeElsa3MigrationFixture";

    [Fact]
    public async Task Inventory_composes_the_host_and_the_addition_list_with_the_import_run_in_the_shell()
    {
        using var host = new TestHostFolder();

        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Inventory, host.Wiring, CancellationToken.None);

        Assert.Null(refusal);
        await using (composition!)
        {
            await using var scope = composition.Shell.BeginScope();
            Assert.NotNull(scope.ServiceProvider.GetService<Elsa3ImportRun>());
            // Startup tasks only: no background or recurring task starts (T009a).
            Assert.True(StartupTasksOnly(scope.ServiceProvider));
            foreach (var feature in new[] { "Elsa3Mapping", "Elsa3ImportJsonActivities", "Elsa3ImportActivitiesEntityFrameworkCore", "Elsa3ImportActivitiesPublishing", "Elsa3MigrationImportRun", "JsonWorkflowReconciliation", "Serialization" })
                Assert.Contains(feature, composition.Facts.Features);
            Assert.DoesNotContain(Fixture, composition.Facts.Features);
            Assert.True(composition.Facts.PersistenceOverridden);
            Assert.Contains("ConnectionStrings:Elsa", composition.Facts.SettingKeys);
        }

        Assert.False(Directory.Exists(Path.Combine(host.HostFolder, "data")));
    }

    [Fact]
    public async Task Inventory_ignores_the_local_sources_that_assess_applies()
    {
        using var host = new TestHostFolder();
        host.WriteLocalSettings($$"""{ "CShells": { "Shells": { "default": { "Features": { "{{Fixture}}": {} } } } } }""");
        var variable = $"CShells__Shells__default__Features__{Fixture}__Probe";
        Environment.SetEnvironmentVariable(variable, "from-the-environment");
        try
        {
            var (inventory, _) = await MigrationComposition.ComposeAsync(MigrationCommand.Inventory, host.Wiring, CancellationToken.None);
            await using (inventory!)
            {
                Assert.DoesNotContain(Fixture, inventory.Facts.Features);
                Assert.DoesNotContain(inventory.Facts.SettingKeys, key => key.Contains(Fixture, StringComparison.Ordinal));
            }

            var (assess, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Assess, host.Wiring, CancellationToken.None);
            Assert.Null(refusal);
            await using (assess!)
            {
                Assert.Contains(Fixture, assess.Facts.Features);
                Assert.Contains($"CShells:Shells:default:Features:{Fixture}:Probe", assess.Facts.SettingKeys);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public async Task A_local_source_that_adds_reconciliation_inputs_neither_loads_them_nor_refuses()
    {
        using var host = new TestHostFolder();
        var missing = Path.Combine(host.Root, "no-such-file.json");
        host.WriteLocalSettings($$"""
            {
              "CShells": { "Shells": { "default": { "Features": {
                "JsonWorkflowReconciliation": { "Options": {
                  "Files": [ { "Path": "{{missing.Replace("\\", "\\\\")}}" } ],
                  "AdditionalSources": [ { "FolderPath": "{{host.Root.Replace("\\", "\\\\")}}" } ] } },
                "JsonActivityReconciliation": { "Options": { "Files": [ { "Path": "{{missing.Replace("\\", "\\\\")}}" } ] } }
              } } } }
            }
            """);

        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Assess, host.Wiring, CancellationToken.None);

        Assert.Null(refusal);
        await composition!.DisposeAsync();
    }

    [Fact]
    public async Task A_malformed_local_source_refuses_assess_with_composition_failed_and_no_value()
    {
        using var host = new TestHostFolder();
        host.WriteLocalSettings("""{ "Elsa": { "DataProtection": { "EntityFrameworkCore": { "Enabled": "SENTINEL-1237" } } } }""");

        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Assess, host.Wiring, CancellationToken.None);

        Assert.Null(composition);
        Assert.Equal(MigrationRefusal.CompositionFailed, refusal?.Reason);
        Assert.DoesNotContain("SENTINEL-1237", refusal!.Sentence);
    }

    [Fact]
    public void A_reconciliation_feature_whose_only_settings_are_filtered_stays_listed()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FilePath"] = "x.json",
            ["CShells:Shells:default:Features:Serialization"] = null
        }).Build();

        var filtered = MigrationComposition.Filtered(configuration);

        var features = filtered.GetSection("CShells:Shells:default:Features").GetChildren().Select(feature => feature.Key).ToArray();
        Assert.Contains("JsonWorkflowReconciliation", features);
        Assert.Contains("Serialization", features);
        Assert.Null(filtered["CShells:Shells:default:Features:JsonWorkflowReconciliation:Options:FilePath"]);
    }

    [Fact]
    public async Task No_environment_variable_or_current_directory_setting_reaches_inventory()
    {
        using var host = new TestHostFolder();
        var variables = new Dictionary<string, string?>
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            [$"CShells__Shells__default__Features__{Fixture}"] = "enabled"
        };
        var previous = variables.Keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        foreach (var (key, value) in variables)
            Environment.SetEnvironmentVariable(key, value);
        try
        {
            // In Production the host's identity features refuse their development-only settings; Development is fixed.
            var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Inventory, host.Wiring, CancellationToken.None);

            Assert.Null(refusal);
            await using (composition!)
                Assert.DoesNotContain(Fixture, composition.Facts.Features);
        }
        finally
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    [Fact]
    public async Task Assess_resolves_an_addition_list_feature_and_a_feature_listed_locally()
    {
        using var host = new TestHostFolder();
        host.WriteLocalSettings($$"""{ "CShells": { "Shells": { "default": { "Features": { "{{Fixture}}": {} } } } } }""");

        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Assess, host.Wiring, CancellationToken.None);

        Assert.Null(refusal);
        await using (composition!)
        {
            Assert.Contains(Fixture, composition.Facts.Features);
            Assert.Contains("Elsa3MigrationImportRun", composition.Facts.Features);
        }
    }

    [Fact]
    public async Task A_listed_feature_no_project_provides_refuses_with_composition_failed_naming_it()
    {
        using var host = new TestHostFolder();
        host.AddToAdditionList("NoSuchFeature1237");

        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Inventory, host.Wiring, CancellationToken.None);

        Assert.Null(composition);
        Assert.Equal(MigrationRefusal.CompositionFailed, refusal?.Reason);
        Assert.Contains("NoSuchFeature1237", refusal!.Sentence);
    }

    [Fact]
    public async Task A_feature_that_throws_while_composed_refuses_naming_the_feature_and_no_value()
    {
        using var host = new TestHostFolder();
        // The fixture feature refuses a Throw setting while its services are configured (T003).
        host.WriteLocalSettings($$"""{ "CShells": { "Shells": { "default": { "Features": { "{{Fixture}}": { "Throw": "SENTINEL-1237" } } } } } }""");

        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Assess, host.Wiring, CancellationToken.None);

        Assert.Null(composition);
        Assert.Equal(MigrationRefusal.CompositionFailed, refusal?.Reason);
        Assert.Contains(Fixture, refusal!.Sentence);
        Assert.DoesNotContain("SENTINEL-1237", refusal.Sentence);
    }

    [Fact]
    public async Task A_composition_failed_run_leaves_a_missing_out_missing_and_an_existing_one_untouched()
    {
        using var host = new TestHostFolder();
        host.AddToAdditionList("NoSuchFeature1237");
        var existing = Path.Combine(host.Root, "existing");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "report.json"), "previous");

        var (missingRun, missingOut) = await FixtureRun.RunAsync(host, output: Path.Combine(host.Root, "missing"));
        var (existingRun, _) = await FixtureRun.RunAsync(host, output: existing);

        Assert.Equal(MigrationRefusal.CompositionFailed, missingRun.Refusal?.Reason);
        Assert.False(Directory.Exists(missingOut));
        Assert.Equal(MigrationRefusal.CompositionFailed, existingRun.Refusal?.Reason);
        Assert.Equal(["report.json"], Directory.EnumerateFileSystemEntries(existing).Select(Path.GetFileName));
        Assert.Equal("previous", File.ReadAllText(Path.Combine(existing, "report.json")));
    }

    /// <summary>
    /// R-5 parity (T015): the descriptors the host's <c>AddHostServices</c> adds are the same in a baseline empty builder
    /// with the same settings and in the CLI's builder, apart from the Data Protection the ephemeral key ring replaces.
    /// </summary>
    [Fact]
    public async Task The_host_services_the_CLI_composes_equal_a_baseline_builders()
    {
        using var host = new TestHostFolder();
        IReadOnlyList<ServiceDescriptor> cliAdded = [];
        var (composition, refusal) = await MigrationComposition.ComposeAsync(MigrationCommand.Inventory, host.Wiring, CancellationToken.None,
            (before, after) => cliAdded = Added(before, after));
        Assert.Null(refusal);
        await composition!.DisposeAsync();

        var baseline = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = host.HostFolder,
            EnvironmentName = MigrationComposition.Environment,
            DisableDefaults = true
        });
        host.Wiring.AddConfiguration(baseline.Configuration, host.HostFolder, MigrationComposition.Environment, false);
        var before = baseline.Services.ToArray();
        host.Wiring.AddServices(baseline);
        var baselineAdded = Added(before, baseline.Services.ToArray());

        Assert.Equal(Keys(baselineAdded), Keys(cliAdded));
    }

    /// <summary>The Tasks feature's internal start options, read by name: the CLI sets <c>StartupTasksOnly</c> (T009a).</summary>
    private static bool StartupTasksOnly(IServiceProvider services)
    {
        var optionsType = Type.GetType("Elsa.Tasks.Options.TaskStartOptions, Elsa.Tasks", throwOnError: true)!;
        var options = services.GetRequiredService(typeof(IOptions<>).MakeGenericType(optionsType));
        var value = options.GetType().GetProperty(nameof(IOptions<object>.Value))!.GetValue(options)!;
        return (bool)optionsType.GetProperty("StartupTasksOnly")!.GetValue(value)!;
    }

    private static IReadOnlyList<ServiceDescriptor> Added(IReadOnlyList<ServiceDescriptor> before, IReadOnlyList<ServiceDescriptor> after)
    {
        var existing = before.ToHashSet();
        return after.Where(descriptor => !existing.Contains(descriptor)).ToArray();
    }

    /// <summary>The descriptors as a sorted multiset of keys, without the Data Protection the CLI replaces.</summary>
    private static string[] Keys(IEnumerable<ServiceDescriptor> descriptors) =>
        descriptors
            .Where(descriptor => descriptor.ServiceType != typeof(IDataProtectionProvider)
                                 && descriptor.ServiceType != typeof(IKeyManager)
                                 && descriptor.ServiceType != typeof(IConfigureOptions<KeyManagementOptions>)
                                 && !(descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Name == "DataProtectionStartupCheck"))
            .Select(descriptor =>
            {
                var implementation = descriptor.IsKeyedService
                    ? descriptor.KeyedImplementationType?.FullName ?? (descriptor.KeyedImplementationInstance is not null ? "instance" : "factory")
                    : descriptor.ImplementationType?.FullName ?? (descriptor.ImplementationInstance is not null ? "instance" : "factory");
                return $"{descriptor.ServiceType.FullName}|{descriptor.ServiceKey}|{descriptor.Lifetime}|{implementation}";
            })
            .Order(StringComparer.Ordinal)
            .ToArray();
}

/// <summary>Compositions run one at a time: each holds a shared in-memory database, and some tests set environment variables.</summary>
[CollectionDefinition(nameof(CompositionCollection), DisableParallelization = true)]
public sealed class CompositionCollection;
