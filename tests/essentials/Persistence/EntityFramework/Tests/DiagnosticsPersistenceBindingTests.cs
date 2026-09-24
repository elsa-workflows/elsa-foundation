using System.Text;
using System.Text.Json;
using CShells;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class DiagnosticsPersistenceBindingTests
{
    private const string StructuredLogs = "DiagnosticsStructuredLogsEntityFrameworkCore";
    private const string OpenTelemetry = "DiagnosticsOpenTelemetryEntityFrameworkCore";
    private const string Runtime = "WorkflowsRuntimeEntityFrameworkCore";

    private static readonly IReadOnlyList<EnrolledPersistenceParticipant> Participants =
    [
        new(StructuredLogs, ["Diagnostics.StructuredLogs"], "StructuredLogsDbContext", true, false),
        new(OpenTelemetry, ["Diagnostics.OpenTelemetry"], "EfOpenTelemetryDbContext", true, false),
        new(Runtime, ["Workflows.Runtime"], "RuntimeDbContext", true, false)
    ];

    [Fact]
    public void Both_diagnostics_bindings_select_the_secondary_resource_and_other_participants_keep_the_shell_default()
    {
        var result = Resolve(ReadConfiguration());

        Assert.False(result.IsRefused);
        AssertSelection(result, StructuredLogs, "diagnostics", PersistenceSelectionKind.ShellBinding, "Diagnostics");
        AssertSelection(result, OpenTelemetry, "diagnostics", PersistenceSelectionKind.ShellBinding, "Diagnostics");
        AssertSelection(result, Runtime, "primary", PersistenceSelectionKind.ShellDefault, "Shared");
    }

    [Fact]
    public void Removing_diagnostics_bindings_restores_inheritance_and_keeps_unrelated_authored_settings()
    {
        var withBindings = ReadConfiguration();
        var withoutBindings = ReadConfiguration(new Dictionary<string, object?>());
        var before = Resolve(withBindings);
        var after = Resolve(withoutBindings);

        AssertSelection(before, StructuredLogs, "diagnostics", PersistenceSelectionKind.ShellBinding, "Diagnostics");
        AssertSelection(before, OpenTelemetry, "diagnostics", PersistenceSelectionKind.ShellBinding, "Diagnostics");
        AssertSelection(after, StructuredLogs, "primary", PersistenceSelectionKind.ShellDefault, "Shared");
        AssertSelection(after, OpenTelemetry, "primary", PersistenceSelectionKind.ShellDefault, "Shared");
        AssertSelection(after, Runtime, "primary", PersistenceSelectionKind.ShellDefault, "Shared");
        Assert.Equal("keep-this-setting", withBindings.Context.ConfigurationData["Unrelated:Value"]);
        Assert.Equal("keep-this-setting", withoutBindings.Context.ConfigurationData["Unrelated:Value"]);

        var privateBinding = Resolve(ReadConfiguration(new Dictionary<string, object?> { ["PrivateDiagnosticsStore"] = "primary" }));
        Assert.Contains("resource-participant-unenrolled", privateBinding.Evidence.UnverifiedPrerequisites);
        Assert.DoesNotContain(privateBinding.Participants, item => item.Participant.FeatureId == "PrivateDiagnosticsStore");
    }

    [Theory]
    [InlineData(StructuredLogs, null, "resource-selection-invalid")]
    [InlineData(StructuredLogs, "", "resource-selection-invalid")]
    [InlineData(StructuredLogs, "missing;Password=secret-canary", "resource-selection-invalid")]
    [InlineData(OpenTelemetry, null, "resource-selection-invalid")]
    [InlineData(OpenTelemetry, "", "resource-selection-invalid")]
    [InlineData(OpenTelemetry, "missing;Password=secret-canary", "resource-selection-invalid")]
    public void Invalid_binding_refuses_instead_of_inheriting_and_redacts_its_value(
        string featureId,
        string? authoredValue,
        string refusalCode)
    {
        var result = Resolve(ReadConfiguration(new Dictionary<string, object?> { [featureId] = authoredValue }));

        Assert.True(result.IsRefused);
        Assert.Equal(refusalCode, Assert.Single(result.Refusals).Code);
        Assert.Equal(PersistenceSelectionKind.ShellBinding,
            Assert.Single(result.Participants, item => item.Participant.FeatureId == featureId).Selection);
        Assert.DoesNotContain("secret-canary", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    private static PersistenceResolutionResult Resolve((ShellSettingsPreparationContext Context, IConfiguration Configuration) configuration)
    {
        var input = PersistenceConfigurationAdapter.Read(configuration.Context, configuration.Configuration, Participants).Input;
        return new PersistenceResourceResolver().Resolve(input);
    }

    private static void AssertSelection(
        PersistenceResolutionResult result,
        string featureId,
        string resource,
        PersistenceSelectionKind selection,
        string connectionName)
    {
        var participant = Assert.Single(result.Participants, item => item.Participant.FeatureId == featureId);
        Assert.Equal(selection, participant.Selection);
        Assert.Equal(resource, participant.ResourceName);
        Assert.Equal("PostgreSql", participant.Provider);
        Assert.Equal(connectionName, participant.ConnectionName);
    }

    private static (ShellSettingsPreparationContext Context, IConfiguration Configuration) ReadConfiguration(
        IReadOnlyDictionary<string, object?>? bindings = null)
    {
        bindings ??= new Dictionary<string, object?>
        {
            [StructuredLogs] = "diagnostics",
            [OpenTelemetry] = "diagnostics"
        };
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["Elsa"] = new Dictionary<string, object?>
            {
                ["Persistence"] = new Dictionary<string, object?>
                {
                    ["Resources"] = new Dictionary<string, object?>
                    {
                        ["primary"] = new Dictionary<string, string>
                        {
                            ["Provider"] = "PostgreSql", ["ConnectionName"] = "Shared"
                        },
                        ["diagnostics"] = new Dictionary<string, string>
                        {
                            ["Provider"] = "PostgreSql", ["ConnectionName"] = "Diagnostics"
                        }
                    },
                    ["DefaultResource"] = "primary"
                }
            },
            ["CShells"] = new Dictionary<string, object?>
            {
                ["Shells"] = new Dictionary<string, object?>
                {
                    ["default"] = new Dictionary<string, object?>
                    {
                        ["Configuration"] = new Dictionary<string, object?>
                        {
                            ["Elsa"] = new Dictionary<string, object?>
                            {
                                ["Persistence"] = new Dictionary<string, object?>
                                {
                                    ["DefaultResource"] = "primary",
                                    ["Bindings"] = bindings
                                }
                            }
                        }
                    }
                }
            }
        });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var featureIds = Participants.Select(participant => participant.FeatureId).ToArray();
        var context = new ShellSettingsPreparationContext(
            new ShellId("default"),
            new Dictionary<string, string?> { ["Unrelated:Value"] = "keep-this-setting" },
            featureIds,
            [],
            [],
            Participants.Select(participant => new ShellFeaturePreparationDescriptor(participant.FeatureId, [], null, false)).ToArray(),
            featureIds,
            [],
            []);
        return (context, configuration);
    }
}
