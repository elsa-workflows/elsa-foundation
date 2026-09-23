using System.Text;
using CShells;
using CShells.Lifecycle;
using CShells.Lifecycle.Blueprints;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class PersistenceConfigurationAdapterTests
{
    private const string Feature = "WorkflowsRuntimeEntityFrameworkCore";
    private static readonly EnrolledPersistenceParticipant Participant =
        new(Feature, ["Workflows.Runtime"], "RuntimeDbContext", true, false);

    [Fact]
    public void Binding_overrides_shell_and_root_defaults_and_keeps_pair_atomic()
    {
        var configuration = Json("""
            { "Elsa": { "Persistence": { "Resources": {
                "root": { "Provider": "Sqlite", "ConnectionName": "Root" },
                "shell": { "Provider": "SqlServer", "ConnectionName": "Shell" },
                "primary": { "Provider": "PostgreSql", "ConnectionName": "Shared" }
            }, "DefaultResource": "root" } } }
            """);
        var context = Context(new Dictionary<string, string?>
        {
            ["Elsa:Persistence:DefaultResource"] = "shell",
            [$"Elsa:Persistence:Bindings:{Feature}"] = "primary",
            ["Unrelated:Value"] = "keep"
        });

        var result = Resolve(context, configuration);

        Assert.False(result.Resolution.IsRefused);
        Assert.Equal(PersistenceSelectionKind.ShellBinding, Assert.Single(result.Resolution.Participants).Selection);
        Assert.Equal("PostgreSql", Assert.Single(result.Resolution.Participants).Provider);
        Assert.Equal("Shared", Assert.Single(result.Resolution.Participants).ConnectionName);
        Assert.Equal("keep", context.ConfigurationData["Unrelated:Value"]);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("array-direct")]
    [InlineData("array-wrapper")]
    public async Task Raw_null_legacy_field_survives_cshells_feature_flattening(string shape)
    {
        var featureJson = shape switch
        {
            "object" => "\"Features\": { \"WorkflowsRuntimeEntityFrameworkCore\": { \"Provider\": null } }",
            "array-direct" => "\"Features\": [ { \"Name\": \"WorkflowsRuntimeEntityFrameworkCore\", \"Provider\": null } ]",
            _ => "\"Features\": [ { \"Name\": \"WorkflowsRuntimeEntityFrameworkCore\", \"Settings\": { \"Provider\": null } } ]"
        };
        var configuration = Json($$"""
            { "Elsa": { "Persistence": { "Resources": { "primary": { "Provider": "PostgreSql", "ConnectionName": "Shared" } }, "DefaultResource": "primary" } },
              "CShells": { "Shells": { "default": { {{featureJson}} } } } }
            """);

        var settings = await new ConfigurationShellBlueprint(
            "default", configuration.GetSection("CShells:Shells:default")).ComposeAsync();
        var context = Context(settings.ConfigurationData.ToDictionary(
            pair => pair.Key, pair => pair.Value?.ToString(), StringComparer.OrdinalIgnoreCase));
        Assert.DoesNotContain($"{Feature}:Provider", context.ConfigurationData.Keys);

        var result = Resolve(context, configuration);

        Assert.Equal("resource-legacy-conflict", Assert.Single(result.Resolution.Refusals).Code);
        Assert.Equal("Provider", Assert.Single(result.Resolution.Refusals).FieldName);
    }

    [Theory]
    [InlineData("False")]
    [InlineData("0")]
    [InlineData("")]
    public void Non_null_final_legacy_value_counts_as_authored_even_when_false_zero_or_empty(string value)
    {
        var configuration = RootDefault();
        var result = Resolve(Context(new Dictionary<string, string?> { [$"{Feature}:Provider"] = value }), configuration);

        Assert.Equal("resource-legacy-conflict", Assert.Single(result.Resolution.Refusals).Code);
    }

    [Fact]
    public void Reset_suppresses_raw_null_and_preserves_other_fields()
    {
        var configuration = Json("""
            { "Elsa": { "Persistence": { "Resources": { "primary": { "Provider": "PostgreSql", "ConnectionName": "Shared" } }, "DefaultResource": "primary" } },
              "CShells": { "Shells": { "default": { "Features": { "WorkflowsRuntimeEntityFrameworkCore": { "ConnectionString": null } } } } } }
            """);
        var context = Context(new Dictionary<string, string?> { ["Unrelated:Value"] = "keep" }, reset: true);

        var result = Resolve(context, configuration);

        Assert.False(result.Resolution.IsRefused);
        Assert.Equal(PersistencePresence.Absent, result.Input.LegacyTargets[Feature].ConnectionString.Presence);
        Assert.Equal("keep", context.ConfigurationData["Unrelated:Value"]);
    }

    [Fact]
    public void Reset_does_not_hide_a_final_code_configured_target()
    {
        var context = Context(new Dictionary<string, string?> { [$"{Feature}:Provider"] = "Sqlite" }, reset: true);

        var result = Resolve(context, RootDefault());

        Assert.Equal(PersistencePresence.Value, result.Input.LegacyTargets[Feature].Provider.Presence);
        Assert.Equal("resource-legacy-conflict", Assert.Single(result.Resolution.Refusals).Code);
    }

    [Fact]
    public void Explicit_null_shell_default_refuses_instead_of_inheriting_root_default()
    {
        var configuration = Json("""
            { "Elsa": { "Persistence": { "Resources": { "primary": { "Provider": "PostgreSql", "ConnectionName": "Shared" } }, "DefaultResource": "primary" } },
              "CShells": { "Shells": { "default": { "Configuration": { "Elsa": { "Persistence": { "DefaultResource": null } } } } } } }
            """);

        var result = Resolve(Context(), configuration);

        Assert.Equal(PersistencePresence.Null, result.Input.ShellSelection.ShellDefault.Presence);
        Assert.Equal("resource-selection-invalid", Assert.Single(result.Resolution.Refusals).Code);
    }

    [Fact]
    public void Unknown_binding_is_unresolved_and_unsupported_scope_is_not_applied()
    {
        var configuration = Json("""
            { "Elsa": { "Persistence": { "Bindings": { "Other": "primary" }, "Resources": { "primary": { "Provider": "PostgreSql", "ConnectionName": "Shared" } } } },
              "CShells": { "Shells": { "default": { "Configuration": { "Elsa": { "Persistence": { "Bindings": { "Unknown": "primary" }, "Resources": { "local": { "Provider": "Sqlite" } } } } } } } } }
            """);

        var result = Resolve(Context(), configuration);

        Assert.False(result.Resolution.IsRefused);
        Assert.Equal(PersistenceSelectionKind.Legacy, Assert.Single(result.Resolution.Participants).Selection);
        Assert.Contains("resource-participant-unenrolled", result.Resolution.Evidence.UnverifiedPrerequisites);
        Assert.Contains("resource-scope-unsupported", PersistenceConfigurationAdapter.Read(Context(), configuration, [Participant]).UnresolvedCodes);
    }

    [Fact]
    public void Binding_for_known_inactive_participant_is_inert()
    {
        var configuration = Json("""
            { "CShells": { "Shells": { "default": { "Configuration": { "Elsa": { "Persistence": { "Bindings": { "KnownInactive": "primary" } } } } } } } }
            """);

        var read = PersistenceConfigurationAdapter.Read(Context(), configuration, [Participant], [Feature, "KnownInactive"]);
        var resolved = new PersistenceResourceResolver().Resolve(read.Input);

        Assert.Empty(resolved.Refusals);
        Assert.Empty(resolved.Evidence.UnverifiedPrerequisites);
        Assert.Equal(PersistenceSelectionKind.Legacy, Assert.Single(resolved.Participants).Selection);
    }

    [Fact]
    public void Wrong_type_binding_refuses_without_using_root_default()
    {
        var configuration = Json("""
            { "Elsa": { "Persistence": { "Resources": { "primary": { "Provider": "PostgreSql", "ConnectionName": "Shared" } }, "DefaultResource": "primary" } },
              "CShells": { "Shells": { "default": { "Configuration": { "Elsa": { "Persistence": { "Bindings": { "WorkflowsRuntimeEntityFrameworkCore": { "Unexpected": "value" } } } } } } } } }
            """);

        var result = Resolve(Context(), configuration);

        Assert.Equal(PersistencePresence.WrongType, result.Input.ShellSelection.FeatureBindings[Feature].Presence);
        Assert.Equal("resource-selection-invalid", Assert.Single(result.Resolution.Refusals).Code);
    }

    private static (PersistenceResolutionInput Input, PersistenceResolutionResult Resolution) Resolve(
        ShellSettingsPreparationContext context,
        IConfiguration configuration)
    {
        var input = PersistenceConfigurationAdapter.Read(context, configuration, [Participant]).Input;
        return (input, new PersistenceResourceResolver().Resolve(input));
    }

    private static ShellSettingsPreparationContext Context(
        IReadOnlyDictionary<string, string?>? values = null,
        bool reset = false) =>
        new(new ShellId("default"), values ?? new Dictionary<string, string?>(), [Feature], [],
            reset ? [Feature] : [], [new ShellFeaturePreparationDescriptor(Feature, [], null, false)],
            [Feature], [], []);

    private static IConfiguration RootDefault() => Json("""
        { "Elsa": { "Persistence": { "Resources": { "primary": { "Provider": "PostgreSql", "ConnectionName": "Shared" } }, "DefaultResource": "primary" } } }
        """);

    private static IConfiguration Json(string json) =>
        new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json))).Build();
}
