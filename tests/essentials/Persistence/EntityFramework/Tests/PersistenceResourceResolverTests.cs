using System.Text.Json;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class PersistenceResourceResolverTests
{
    private const string FeatureId = "WorkflowsRuntimeEntityFrameworkCore";
    private static readonly PersistenceSourceProvenance Root = new("root", "json", false, true);
    private static readonly PersistenceSourceProvenance Shell = new("shell", "composed", false, true);
    private static readonly PersistenceAuthoredValue Missing = new(PersistencePresence.Absent, null, Root);
    private static readonly EnrolledPersistenceParticipant Participant =
        new(FeatureId, ["Workflows.Runtime"], "RuntimeDbContext", true, false);
    private readonly PersistenceResourceResolver _resolver = new();

    [Fact]
    public void Binding_wins_shell_and_root_defaults_without_combining_resource_fields()
    {
        var input = Input(
            resources: new Dictionary<string, PersistenceResourceDefinition>
            {
                ["root"] = Resource("root", "Sqlite", "Root"),
                ["shell"] = Resource("shell", "SqlServer", "Shell"),
                ["primary"] = Resource("primary", "PostgreSql", "Shared")
            },
            rootDefault: Value("root", Root),
            shellDefault: Value("shell", Shell),
            bindings: new Dictionary<string, PersistenceAuthoredValue> { [FeatureId.ToUpperInvariant()] = Value("PRIMARY", Shell) });

        var result = _resolver.Resolve(input);

        Assert.False(result.IsRefused);
        var resolved = Assert.Single(result.Participants);
        Assert.Equal(PersistenceSelectionKind.ShellBinding, resolved.Selection);
        Assert.Equal("primary", resolved.ResourceName);
        Assert.Equal("PostgreSql", resolved.Provider);
        Assert.Equal("Shared", resolved.ConnectionName);
        Assert.Same(Shell, resolved.Source);
    }

    [Fact]
    public void Removing_an_override_reveals_shell_then_root_then_legacy()
    {
        var resources = new Dictionary<string, PersistenceResourceDefinition>
        {
            ["primary"] = Resource("primary", "PostgreSql", "Shared"),
            ["shell"] = Resource("shell", "SqlServer", "Shell")
        };

        var shell = _resolver.Resolve(Input(resources, Value("primary", Root), Value("shell", Shell)));
        var root = _resolver.Resolve(Input(resources, Value("primary", Root)));
        var legacy = _resolver.Resolve(Input(resources));

        Assert.Equal(PersistenceSelectionKind.ShellDefault, Assert.Single(shell.Participants).Selection);
        Assert.Equal("Shell", Assert.Single(shell.Participants).ConnectionName);
        Assert.Equal(PersistenceSelectionKind.RootDefault, Assert.Single(root.Participants).Selection);
        Assert.Equal("Shared", Assert.Single(root.Participants).ConnectionName);
        var legacyResolution = Assert.Single(legacy.Participants);
        Assert.Equal(PersistenceSelectionKind.Legacy, legacyResolution.Selection);
        Assert.Null(legacyResolution.Provider);
        Assert.Null(legacyResolution.ConnectionName);
        Assert.False(legacy.IsRefused);
    }

    [Theory]
    [InlineData("Null")]
    [InlineData("Blank")]
    [InlineData("WrongType")]
    public void Present_invalid_binding_refuses_without_falling_back(string presence)
    {
        var result = _resolver.Resolve(Input(
            rootDefault: Value("primary", Root),
            bindings: new Dictionary<string, PersistenceAuthoredValue>
            {
                [FeatureId] = new(Enum.Parse<PersistencePresence>(presence), null, Shell)
            }));

        Assert.True(result.IsRefused);
        Assert.Equal("resource-selection-invalid", Assert.Single(result.Refusals).Code);
        Assert.Equal(PersistenceSelectionKind.ShellBinding, Assert.Single(result.Participants).Selection);
        Assert.Null(Assert.Single(result.Participants).Provider);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_or_blank_selected_name_refuses_without_falling_back(string name)
    {
        var result = _resolver.Resolve(Input(
            rootDefault: Value("primary", Root),
            shellDefault: Value(name, Shell)));

        Assert.Equal("resource-selection-invalid", Assert.Single(result.Refusals).Code);
        Assert.Equal(PersistenceSelectionKind.ShellDefault, Assert.Single(result.Participants).Selection);
    }

    [Fact]
    public void Missing_or_incomplete_resource_never_returns_a_half_target()
    {
        const string canary = "Host=secret-canary;Password=never-export";
        var missing = _resolver.Resolve(Input(rootDefault: Value(canary, Root)));
        Assert.Equal("resource-not-found", Assert.Single(missing.Refusals).Code);
        Assert.Null(Assert.Single(missing.Participants).ResourceName);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(missing));

        var incomplete = _resolver.Resolve(Input(
            resources: new Dictionary<string, PersistenceResourceDefinition>
            {
                ["primary"] = Resource("primary", "PostgreSql", "", connectionPresence: PersistencePresence.Blank)
            },
            rootDefault: Value("primary", Root)));
        Assert.Equal("resource-definition-invalid", Assert.Single(incomplete.Refusals).Code);
        Assert.Null(Assert.Single(incomplete.Participants).Provider);
        Assert.Null(Assert.Single(incomplete.Participants).ConnectionName);
    }

    [Theory]
    [InlineData("Provider")]
    [InlineData("ConnectionName")]
    public void Null_resource_field_refuses_an_atomic_target(string field)
    {
        var resource = new PersistenceResourceDefinition("primary",
            field == "Provider" ? new(PersistencePresence.Null, null, Root) : Value("PostgreSql", Root),
            field == "ConnectionName" ? new(PersistencePresence.Null, null, Root) : Value("Shared", Root),
            Root);
        var result = _resolver.Resolve(Input(
            resources: new Dictionary<string, PersistenceResourceDefinition> { ["primary"] = resource },
            rootDefault: Value("primary", Root)));

        Assert.Equal(field, Assert.Single(result.Refusals).FieldName);
        Assert.Null(Assert.Single(result.Participants).Provider);
        Assert.Null(Assert.Single(result.Participants).ConnectionName);
    }

    [Theory]
    [InlineData("Provider")]
    [InlineData("ConnectionName")]
    [InlineData("ConnectionString")]
    public void Any_effective_authored_legacy_target_conflicts_even_when_null(string field)
    {
        var legacy = LegacyTarget(field, PersistencePresence.Null);
        var result = _resolver.Resolve(Input(rootDefault: Value("primary", Root), legacy: legacy));

        Assert.Equal("resource-legacy-conflict", Assert.Single(result.Refusals).Code);
        Assert.Equal(field, Assert.Single(result.Refusals).FieldName);

        var reset = _resolver.Resolve(Input(rootDefault: Value("primary", Root), legacy: legacy with { IsReset = true }));
        Assert.False(reset.IsRefused);
        Assert.Equal("PostgreSql", Assert.Single(reset.Participants).Provider);
    }

    [Fact]
    public void Opaque_configurator_and_disabled_required_feature_refuse_before_materialization()
    {
        var opaque = Participant with { HasOpaqueConfigurator = true };
        var legacy = LegacyTarget("Provider", PersistencePresence.Absent) with { IsEnabled = false };
        var result = _resolver.Resolve(Input(rootDefault: Value("primary", Root), participant: opaque, legacy: legacy));

        Assert.Equal(
            ["resource-configurator-unsupported", "resource-required-feature-disabled"],
            result.Refusals.Select(x => x.Code));
    }

    [Fact]
    public void Unenrolled_binding_is_unresolved_without_enabling_or_leaking_its_value()
    {
        const string canary = "Host=secret-canary;Password=never-export";
        var result = _resolver.Resolve(Input(bindings: new Dictionary<string, PersistenceAuthoredValue>
        {
            ["UnknownStore"] = Value(canary, Shell)
        }));

        Assert.False(result.IsRefused);
        Assert.Equal(["resource-participant-unenrolled"], result.Evidence.UnverifiedPrerequisites);
        Assert.Equal(PersistenceSelectionKind.Legacy, Assert.Single(result.Participants).Selection);
        Assert.DoesNotContain(canary, JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Binding_for_known_inactive_participant_is_inert()
    {
        var input = Input(bindings: new Dictionary<string, PersistenceAuthoredValue>
        {
            [FeatureId] = Value("primary", Shell)
        });
        input = input with { Participants = [] };

        var result = _resolver.Resolve(input);

        Assert.Empty(result.Participants);
        Assert.Empty(result.Refusals);
        Assert.Empty(result.Evidence.UnverifiedPrerequisites);
    }

    private static PersistenceResolutionInput Input(
        IReadOnlyDictionary<string, PersistenceResourceDefinition>? resources = null,
        PersistenceAuthoredValue? rootDefault = null,
        PersistenceAuthoredValue? shellDefault = null,
        IReadOnlyDictionary<string, PersistenceAuthoredValue>? bindings = null,
        EnrolledPersistenceParticipant? participant = null,
        PersistenceLegacyTargetPresence? legacy = null)
    {
        var selected = participant ?? Participant;
        var known = new Dictionary<string, PersistenceLegacyTargetPresence>
        {
            [selected.FeatureId] = legacy ?? LegacyTarget("Provider", PersistencePresence.Absent)
        };
        return new PersistenceResolutionInput(
            new PersistenceResourceCatalog(resources ?? new Dictionary<string, PersistenceResourceDefinition>
            {
                ["primary"] = Resource("primary", "PostgreSql", "Shared")
            }, rootDefault ?? Missing),
            new PersistenceShellSelection("default", shellDefault ?? Missing,
                bindings ?? new Dictionary<string, PersistenceAuthoredValue>()),
            [selected],
            known,
            new PersistenceConfigurationContext(PersistenceContextMode.Runtime, "default", "Development", [Root, Shell], false, true));
    }

    private static PersistenceResourceDefinition Resource(
        string name,
        string provider,
        string connection,
        PersistencePresence connectionPresence = PersistencePresence.Value) =>
        new(name, Value(provider, Root), new(connectionPresence, connection, Root), Root);

    private static PersistenceAuthoredValue Value(string value, PersistenceSourceProvenance source) =>
        new(PersistencePresence.Value, value, source);

    private static PersistenceLegacyTargetPresence LegacyTarget(string field, PersistencePresence presence)
    {
        var absent = new PersistenceLegacyFieldPresence(PersistencePresence.Absent, Shell);
        var authored = new PersistenceLegacyFieldPresence(presence, Shell);
        return new PersistenceLegacyTargetPresence(FeatureId, true, false,
            field == "Provider" ? authored : absent,
            field == "ConnectionName" ? authored : absent,
            field == "ConnectionString" ? authored : absent);
    }
}
