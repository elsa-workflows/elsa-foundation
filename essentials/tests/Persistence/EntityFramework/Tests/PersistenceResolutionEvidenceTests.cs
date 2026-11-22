using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Persistence.EntityFramework.ResourceResolution;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

public sealed class PersistenceResolutionEvidenceTests
{
    private const string FeatureId = "WorkflowsRuntimeEntityFrameworkCore";
    private static readonly PersistenceSourceProvenance Root = new("root", "json", false, true);
    private static readonly PersistenceSourceProvenance Shell = new("shell", "json", false, true);
    private static readonly EnrolledPersistenceParticipant Participant =
        new(FeatureId, ["Workflows.Runtime"], "RuntimeDbContext", true, false);

    [Fact]
    public void Evidence_reports_checked_sources_and_unverified_prerequisites_without_claiming_readiness()
    {
        var checkedSources = new[] { Root, Shell, new PersistenceSourceProvenance("environment", "environment", true, true) };
        var input = Input(
            bindings: new Dictionary<string, PersistenceAuthoredValue>
            {
                ["UnknownStore"] = Value("UnenrolledResource", Shell)
            },
            context: new PersistenceConfigurationContext(
                PersistenceContextMode.ToolingFileEnvironment, "default", "Production", checkedSources,
                IncludesExternalEnvironment: true, IsFrozenSnapshot: true));

        var result = new PersistenceResourceResolver().Resolve(input);

        Assert.False(result.IsRefused);
        Assert.Equal(checkedSources, result.Evidence.Sources);
        Assert.Equal(checkedSources, result.Evidence.Context.CheckedSources);
        Assert.Equal(PersistenceContextMode.ToolingFileEnvironment, result.Evidence.Context.Mode);
        Assert.True(result.Evidence.Context.IncludesExternalEnvironment);
        Assert.True(result.Evidence.Context.IsFrozenSnapshot);
        Assert.Equal(["resource-participant-unenrolled"], result.Evidence.UnverifiedPrerequisites);
        Assert.Equal(PersistenceSelectionKind.Legacy, Assert.Single(result.Participants).Selection);
    }

    [Theory]
    [InlineData("invalid-selection", "resource-selection-invalid")]
    [InlineData("missing-resource", "resource-not-found")]
    [InlineData("invalid-definition", "resource-definition-invalid")]
    public void Refusals_have_stable_codes_and_redacted_evidence(string scenario, string expectedCode)
    {
        const string canary = "/Users/private/Host=resolution-canary.internal;Password=secret-value";
        var input = scenario switch
        {
            "invalid-selection" => Input(rootDefault: new(PersistencePresence.Blank, canary, Root)),
            "missing-resource" => Input(rootDefault: Value("MissingResource", Root)),
            "invalid-definition" => Input(
                rootDefault: Value("primary", Root),
                resources: new Dictionary<string, PersistenceResourceDefinition>
                {
                    ["primary"] = new("primary", Value("PostgreSql", Root),
                        new(PersistencePresence.Blank, canary, Root), Root)
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
        };

        var result = new PersistenceResourceResolver().Resolve(input);
        var code = Assert.Single(result.Refusals).Code;
        var safeEvidence = JsonSerializer.Serialize(new { result.Refusals, result.Evidence });
        var canaryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canary)));

        Assert.True(result.IsRefused);
        Assert.Equal(expectedCode, code);
        Assert.DoesNotContain(canary, safeEvidence, StringComparison.Ordinal);
        Assert.DoesNotContain(canaryHash, safeEvidence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/Users/", safeEvidence, StringComparison.Ordinal);
    }

    private static PersistenceResolutionInput Input(
        PersistenceAuthoredValue? rootDefault = null,
        IReadOnlyDictionary<string, PersistenceResourceDefinition>? resources = null,
        IReadOnlyDictionary<string, PersistenceAuthoredValue>? bindings = null,
        PersistenceConfigurationContext? context = null)
    {
        var sourceContext = context ?? new PersistenceConfigurationContext(
            PersistenceContextMode.Runtime, "default", "Development", [Root, Shell], false, true);
        var absent = new PersistenceAuthoredValue(PersistencePresence.Absent, null, Root);
        var legacyField = new PersistenceLegacyFieldPresence(PersistencePresence.Absent, Shell);
        return new PersistenceResolutionInput(
            new PersistenceResourceCatalog(resources ?? new Dictionary<string, PersistenceResourceDefinition>(), rootDefault ?? absent),
            new PersistenceShellSelection("default", absent,
                bindings ?? new Dictionary<string, PersistenceAuthoredValue>()),
            [Participant],
            new Dictionary<string, PersistenceLegacyTargetPresence>
            {
                [FeatureId] = new(FeatureId, true, false, legacyField, legacyField, legacyField)
            },
            sourceContext);
    }

    private static PersistenceAuthoredValue Value(string value, PersistenceSourceProvenance source) =>
        new(PersistencePresence.Value, value, source);
}
