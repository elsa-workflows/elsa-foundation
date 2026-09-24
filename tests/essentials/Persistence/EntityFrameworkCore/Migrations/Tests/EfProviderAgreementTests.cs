using CShells;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Workflows.Dashboard.Persistence.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The per-feature provider-agreement check and the <c>from-host</c> selection (spec 171 FR-035–FR-039,
/// FR-028; ADR 0076 D4), driven through <see cref="EfToolingHost"/> against the real feature classes.
/// </summary>
/// <remarks>
/// The comparison is per feature, never per module, and these tests are written to fail if that is ever
/// collapsed: <c>Workflows.Runtime</c> is backed by eight separately configured features, so a check that
/// took one feature's provider for the module's would pass the "two features disagree" case while a real
/// host would refuse to start. Both directions are exercised — a disagreement that must be reported, and
/// the two shapes (a not-enabled feature; a feature with no <c>Provider</c> setting) that must not be.
/// </remarks>
public sealed class EfProviderAgreementTests
{
    private const string Runtime = "WorkflowsRuntimeEntityFrameworkCore";
    private const string Bookmarks = "WorkflowsRuntimeBookmarksEntityFrameworkCorePersistence";
    private const string Artifacts = "WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence";
    private const string Dashboard = "WorkflowsDashboardEntityFrameworkCore";
    private const string Secrets = "SecretsEntityFrameworkCore";

    private static readonly Assembly[] Closure =
    [
        .. ModuleContextCatalog.Modules,
        typeof(AspNetCoreIdentityEntityFrameworkCoreFeature).Assembly,
        typeof(WorkflowsDashboardEntityFrameworkCoreFeature).Assembly
    ];

    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public async Task Every_enabled_feature_of_a_selected_module_agreeing_is_a_clean_run()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "PostgreSql"), (Bookmarks, "PostgreSql")));

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    [Fact]
    public void Shared_resource_materialization_agrees_with_tooling_for_every_selected_runtime_feature()
    {
        var participants = EfPersistenceParticipantCatalog.Discover(Closure)
            .Where(participant => participant.ModuleNames.Contains("Workflows.Runtime", StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var featureIds = participants.Select(participant => participant.FeatureId).ToArray();
        Assert.Equal(8, featureIds.Length);
        var featureTypes = EfProviderAgreement.Discover(Closure)
            .ToDictionary(usage => usage.Feature, usage => usage.FeatureType, StringComparer.OrdinalIgnoreCase);
        var context = new ShellSettingsPreparationContext(new ShellId("default"),
            new Dictionary<string, string?>(), featureIds, [], [],
            participants.Select(participant => new ShellFeaturePreparationDescriptor(
                participant.FeatureId, [], featureTypes[participant.FeatureId], false)).ToArray(),
            featureIds, [], []);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:DefaultResource"] = "primary",
            ["Elsa:Persistence:Resources:primary:Provider"] = "PostgreSql",
            ["Elsa:Persistence:Resources:primary:ConnectionName"] = "Shared",
            ["ConnectionStrings:Shared"] = "Host=localhost;Database=elsa;Username=test;Password=secret-canary"
        }).Build();

        var prepared = EfPersistencePreparation.Prepare(context, configuration);

        Assert.Empty(prepared.RefusalCodes);
        Assert.Equal(featureIds.Length * 2, prepared.Patch.ConfigurationData.Count);
        Assert.All(featureIds, feature =>
        {
            Assert.Equal("PostgreSql", prepared.Patch.ConfigurationData[$"{feature}:Provider"]);
            Assert.Equal("Shared", prepared.Patch.ConfigurationData[$"{feature}:ConnectionName"]);
        });

        var enabled = featureIds.Select(feature =>
            (Shell: "default", Feature: feature, Provider: prepared.Patch.ConfigurationData[$"{feature}:Provider"]));
        var disagreements = EfProviderAgreement.Check(enabled,
            EfProviderAgreement.Discover(Closure), ["Workflows.Runtime"], "PostgreSql");

        Assert.Empty(disagreements);
        Assert.DoesNotContain("secret-canary", System.Text.Json.JsonSerializer.Serialize(prepared));
    }

    /// <summary>User Story 1 scenario 5, and the first of the two disagreement edge cases.</summary>
    [Fact]
    public async Task One_feature_configured_for_another_provider_is_refused_by_name_module_and_value()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "PostgreSql"), (Bookmarks, "Sqlite")));

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        Assert.Equal("provider-disagreement", Code(run.Response));
        var offender = Assert.Single(Details(run.Response));
        Assert.Contains(Bookmarks, offender, StringComparison.Ordinal);
        Assert.Contains("'Workflows.Runtime'", offender, StringComparison.Ordinal);
        Assert.Contains("'Sqlite'", offender, StringComparison.Ordinal);
        Assert.DoesNotContain(Runtime, offender, StringComparison.Ordinal);
    }

    /// <summary>
    /// The second edge case, and the one a per-module check gets wrong: the agreeing feature must not
    /// speak for the module. An unset <c>Provider</c> on a feature that declares one is <c>Sqlite</c>
    /// (FR-037), so it disagrees with <c>--provider PostgreSql</c> exactly as an explicit <c>Sqlite</c> does.
    /// </summary>
    [Fact]
    public async Task Agreement_by_one_feature_of_a_module_never_masks_disagreement_by_another()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "PostgreSql"), (Bookmarks, null), (Artifacts, "Sqlite")));

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        var offenders = Details(run.Response);
        Assert.Equal(2, offenders.Length);
        Assert.Contains(offenders, offender => offender.Contains(Artifacts, StringComparison.Ordinal));

        var unset = Assert.Single(offenders, offender => offender.Contains(Bookmarks, StringComparison.Ordinal));
        Assert.Contains("'Sqlite'", unset, StringComparison.Ordinal);
        Assert.Contains("its Provider setting is unset", unset, StringComparison.Ordinal);
    }

    /// <summary>
    /// The no-<c>Provider</c>-setting edge case. The dashboard feature is mapped to two selected modules and
    /// is still skipped entirely — neither compared nor defaulted to <c>Sqlite</c> — because the provider of
    /// the contexts it reads is decided by the features that register their migrations.
    /// </summary>
    [Fact]
    public async Task A_mapped_feature_with_no_provider_setting_is_skipped_rather_than_defaulted()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime", "Workflows.Design"], Enabled((Dashboard, null)));

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    [Fact]
    public async Task A_feature_that_is_not_enabled_is_ignored()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Dashboard, null), (Secrets, "Sqlite")));

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    /// <summary>A feature is only compared against the modules actually selected.</summary>
    [Fact]
    public async Task A_disagreeing_feature_of_an_unselected_module_is_ignored()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Design"], Enabled((Runtime, "Sqlite"), (Secrets, "Sqlite")));

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    /// <summary>Found but enabling nothing is still a check that ran, and it refuses nothing.</summary>
    [Fact]
    public async Task Shell_configuration_that_enables_nothing_compares_nothing()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], []);

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    [Fact]
    public async Task Offenders_in_several_shells_are_all_reported_in_shell_then_feature_order()
    {
        var shells = new ShellFeatureBody[]
        {
            new() { Shell = "tenant-b", Feature = Runtime, Provider = "MySql" },
            new() { Shell = "tenant-a", Feature = Bookmarks, Provider = "Sqlite" },
            new() { Shell = "tenant-a", Feature = Runtime, Provider = "PostgreSql" }
        };

        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], shells);

        var offenders = Details(run.Response);
        Assert.Equal(2, offenders.Length);
        Assert.Contains("tenant-a", offenders[0], StringComparison.Ordinal);
        Assert.Contains(Bookmarks, offenders[0], StringComparison.Ordinal);
        Assert.Contains("tenant-b", offenders[1], StringComparison.Ordinal);
        Assert.Contains("'MySql'", offenders[1], StringComparison.Ordinal);
    }

    /// <summary>A provider spelling the binding table accepts is agreement, not a disagreement on spelling.</summary>
    [Fact]
    public async Task A_configured_provider_alias_agrees_with_its_canonical_name()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "postgres")));

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    /// <summary>A configured provider no host could bind is a disagreement, reported with what was configured.</summary>
    [Fact]
    public async Task A_configured_provider_that_is_not_a_provider_at_all_is_an_offender()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "Oracle")));

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("'Oracle'", Assert.Single(Details(run.Response)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task From_host_selects_every_module_the_enabled_features_map_to()
    {
        var run = await RunAsync(new ListRequestBody
        {
            Selection = new() { Kind = "from-host" },
            Shells = Enabled((Dashboard, null), (Secrets, "Sqlite"))
        });

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
        Assert.Equal(
            ["Secrets", "Workflows.Design", "Workflows.Runtime"],
            run.Response.GetProperty("list").GetProperty("modules").EnumerateArray()
                .Select(module => module.GetProperty("module").GetString())
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task From_host_refuses_when_no_enabled_feature_maps_to_a_module()
    {
        var run = await RunAsync(new ListRequestBody
        {
            Selection = new() { Kind = "from-host" },
            Shells = Enabled(("SomeFeatureThatIsNotAnEfFeature", null))
        });

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        Assert.Equal("from-host-selected-nothing", Code(run.Response));
    }

    [Fact]
    public async Task From_host_refuses_without_shell_configuration_rather_than_selecting_nothing()
    {
        var run = await RunAsync(new ListRequestBody { Selection = new() { Kind = "from-host" } });

        Assert.Equal(EfToolingExitCode.Refusal, run.ExitCode);
        Assert.Contains("'shells' is required by 'list'.", Details(run.Response));
    }

    /// <summary>
    /// The second offender source (spec 172 FR-004, ADR 0076 D4): the engine the host's package closure
    /// selects. <c>--provider</c> stays authoritative, so a selection that does not contain it is reported
    /// rather than taken for the provider.
    /// </summary>
    [Fact]
    public async Task An_engine_selection_that_does_not_contain_the_provider_is_an_offender()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "PostgreSql")), ["Sqlite"]);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        Assert.Equal("provider-disagreement", Code(run.Response));
        var offender = Assert.Single(Details(run.Response));
        Assert.Contains("'Sqlite'", offender, StringComparison.Ordinal);
        Assert.Contains(EfProviderAgreement.CapabilityKey, offender, StringComparison.Ordinal);
    }

    /// <summary>
    /// One refusal, both sources. The feature offenders and the selection are listed together rather than
    /// the first one found short-circuiting the rest, for the same reason one feature never speaks for
    /// another: an operator fixing one and re-running must not discover the second only then.
    /// </summary>
    [Fact]
    public async Task Both_offender_sources_are_listed_in_one_refusal()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Bookmarks, "Sqlite")), ["MySql"]);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        var offenders = Details(run.Response);
        Assert.Equal(2, offenders.Length);
        Assert.Contains(offenders, offender => offender.Contains(Bookmarks, StringComparison.Ordinal));
        Assert.Contains(offenders, offender => offender.Contains(EfProviderAgreement.CapabilityKey, StringComparison.Ordinal));
    }

    /// <summary>
    /// The direction that must not be mistaken for the one above. A multi-engine host selects several
    /// options on purpose, and the rule is containment, not equality — an alias spelling included, because
    /// the option names go through the same binding table every other provider name does.
    /// </summary>
    [Theory]
    [InlineData("PostgreSql")]
    [InlineData("Sqlite", "PostgreSql")]
    [InlineData("postgres")]
    public async Task A_selection_that_contains_the_provider_agrees(params string[] selection)
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "PostgreSql")), selection);

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    /// <summary>
    /// No selection is not an agreement and not a disagreement: a host that sets no such key gets its
    /// engine from a <c>&lt;PackageReference&gt;</c> or an explicitly named root, and there is nothing to
    /// compare. Absent and empty read the same way, so neither can silently refuse a correct host.
    /// </summary>
    [Fact]
    public async Task An_absent_selection_compares_nothing()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "PostgreSql")));

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    [Fact]
    public async Task An_empty_selection_compares_nothing()
    {
        var run = await PlanAsync("PostgreSql", ["Workflows.Runtime"], Enabled((Runtime, "PostgreSql")), []);

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
    }

    /// <summary>
    /// The selection is a fact about the host's package closure, not about its shells, so it is compared on
    /// a host that carries no shell configuration at all — the one case the per-feature check reports as
    /// <c>not-checked</c>. A check gated on <c>shells</c> would pass exactly this host in silence.
    /// </summary>
    [Fact]
    public async Task A_selection_is_compared_on_a_host_with_no_shell_configuration()
    {
        var run = await RunAsync(new PlanRequestBody
        {
            Provider = "PostgreSql",
            Selection = new() { Kind = "modules", Modules = ["Workflows.Runtime"] },
            CapabilitySelection = ["Sqlite"]
        });

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        Assert.Equal("provider-disagreement", Code(run.Response));
        Assert.Contains(EfProviderAgreement.CapabilityKey, Assert.Single(Details(run.Response)), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>list</c> asks for no provider, so it has nothing to compare a selection with — and the contract is
    /// closed in both directions, so it says so rather than ignoring the field.
    /// </summary>
    [Fact]
    public async Task List_refuses_a_selection_it_could_not_compare()
    {
        var run = await RunAsync(new ListRequestBody { Selection = new() { Kind = "all" }, CapabilitySelection = ["Sqlite"] });

        Assert.Equal(EfToolingExitCode.Refusal, run.ExitCode);
        Assert.Contains("'capabilitySelection' is not accepted by 'list'.", Details(run.Response));
    }

    private static ShellFeatureBody[] Enabled(params (string Feature, string? Provider)[] features) =>
        [.. features.Select(feature => new ShellFeatureBody { Shell = "default", Feature = feature.Feature, Provider = feature.Provider })];

    private static Task<Run> PlanAsync(string provider, string[] modules, ShellFeatureBody[] shells, string[]? capabilitySelection = null) =>
        RunAsync(new PlanRequestBody
        {
            Provider = provider,
            Selection = new() { Kind = "modules", Modules = modules },
            Shells = shells,
            CapabilitySelection = capabilitySelection
        });

    private static async Task<Run> RunAsync(object request)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, RequestJson)));
        using var output = new MemoryStream();
        var exitCode = await EfToolingHost.RunAsync(input, output, Closure);
        using var response = JsonDocument.Parse(output.ToArray());
        return new(exitCode, response.RootElement.Clone());
    }

    private static string Code(JsonElement response) => response.GetProperty("error").GetProperty("code").GetString()!;

    private static string[] Details(JsonElement response) =>
        [.. response.GetProperty("error").GetProperty("details").EnumerateArray().Select(detail => detail.GetString()!)];

    private sealed record Run(int ExitCode, JsonElement Response);

    private sealed record PlanRequestBody
    {
        public int Version { get; init; } = 1;
        public string Command { get; init; } = "plan";
        public string? Provider { get; init; }
        public SelectionBody? Selection { get; init; }
        public IReadOnlyList<ShellFeatureBody>? Shells { get; init; }
        public IReadOnlyList<string>? CapabilitySelection { get; init; }
    }

    private sealed record ListRequestBody
    {
        public int Version { get; init; } = 1;
        public string Command { get; init; } = "list";
        public SelectionBody? Selection { get; init; }
        public IReadOnlyList<ShellFeatureBody>? Shells { get; init; }
        public IReadOnlyList<string>? CapabilitySelection { get; init; }
    }

    private sealed record SelectionBody
    {
        public string? Kind { get; init; }
        public IReadOnlyList<string>? Modules { get; init; }
    }

    private sealed record ShellFeatureBody
    {
        public string? Shell { get; init; }
        public string? Feature { get; init; }
        public string? Provider { get; init; }
    }
}
