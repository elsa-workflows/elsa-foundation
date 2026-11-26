using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Primitives.Diagnostics;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityProviderDiagnosticGuidanceTests
{
    private static readonly ActivityProviderDiagnosticDescriptor Guidance = new(
        "acme.model.required", "Choose a model connection.", "Select an authorized model in Designer.", "/model", "Choose model connection");

    [Fact]
    public void Existing_binary_entry_point_keeps_the_current_non_disclosing_fallback()
    {
        var legacy = typeof(ActivityProviderDiagnosticSanitizer).GetMethod(nameof(ActivityProviderDiagnosticSanitizer.Sanitize),
            [typeof(IEnumerable<ActivityDiagnostic>), typeof(string)]);
        Assert.NotNull(legacy);
        var results = Assert.IsAssignableFrom<IReadOnlyList<ActivityDiagnostic>>(legacy.Invoke(null, [new[] { PrivateDiagnostic() }, "acme"]));
        var result = Assert.Single(results);
        var expected = Assert.Single(ActivityProviderDiagnosticSanitizer.Sanitize([PrivateDiagnostic()], "acme", null));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(result));
        Assert.Equal("The activity provider reported an issue.", result.Message);
        Assert.Contains("administrator", result.Remediation);
        Assert.Empty(result.Metadata);
        Assert.Null(result.Location!.JsonPointer);
        Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Declared_guidance_replaces_all_untrusted_runtime_details()
    {
        var result = Assert.Single(ActivityProviderDiagnosticSanitizer.Sanitize([PrivateDiagnostic()], "acme", [Guidance]));
        Assert.Equal(Guidance.Message, result.Message);
        Assert.Equal(Guidance.Remediation, result.Remediation);
        Assert.Equal(new ActivityDiagnosticLocation("acme", "/model"), result.Location);
        Assert.Equal(Guidance.ActionLabel, Assert.Single(result.Metadata).Value);
        Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public void Undeclared_codes_keep_the_non_disclosing_fallback()
    {
        var result = Assert.Single(ActivityProviderDiagnosticSanitizer.Sanitize([PrivateDiagnostic() with { Code = "acme.unknown" }], "acme", [Guidance]));
        Assert.Equal("acme.unknown", result.Code);
        Assert.DoesNotContain(result.Code, result.Message);
        Assert.Contains("administrator", result.Remediation);
        Assert.Null(result.Location!.JsonPointer);
        Assert.Empty(result.Metadata);
        Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("long-message")]
    [InlineData("invalid-path")]
    public void Invalid_or_ambiguous_declarations_do_not_select_public_guidance(string mode)
    {
        var declarations = mode switch
        {
            "duplicate" => new[] { Guidance, Guidance with { Message = "Other meaning" } },
            "long-message" => [Guidance with { Message = new string('x', 513) }],
            _ => [Guidance with { JsonPointer = "PRIVATE/not-a-pointer" }]
        };
        var result = Assert.Single(ActivityProviderDiagnosticSanitizer.Sanitize([PrivateDiagnostic()], "acme", declarations));
        Assert.Equal("The activity provider reported an issue.", result.Message);
        Assert.Null(result.Location!.JsonPointer);
        Assert.Empty(result.Metadata);
    }

    [Fact]
    public async Task Design_registry_snapshots_the_public_catalog_at_registration()
    {
        var declarations = new List<ActivityProviderDiagnosticDescriptor> { Guidance };
        var provider = new Provider(declarations);
        var registry = new ActivityProviderRegistry([provider]);
        declarations[0] = Guidance with { Message = "PRIVATE changed after registration" };
        var result = await registry.Resolve("acme", "1").ValidateAsync(new("acme", "1", System.Text.Json.JsonSerializer.SerializeToElement(new { })), new("1", [], [], []));
        Assert.Equal(Guidance.Message, Assert.Single(result).Message);
        Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("/variables/0/initialValue", true)]
    [InlineData("/variables/9999/initialValue", true)]
    [InlineData("/variables/01/initialValue", false)]
    [InlineData("/variables/10000/initialValue", false)]
    [InlineData("/variables/PRIVATE/initialValue", false)]
    [InlineData("/variables/0/initialValue/PRIVATE", false)]
    [InlineData("/variables/0~1PRIVATE/initialValue", false)]
    public void Declared_numeric_location_patterns_only_preserve_matching_bounded_indices(string pointer, bool expected)
    {
        var declaration = Guidance with { JsonPointer = "/variables", JsonPointerPattern = "/variables/{index}/initialValue" };
        var result = Assert.Single(ActivityProviderDiagnosticSanitizer.Sanitize([
            PrivateDiagnostic() with { Location = new("PRIVATE", pointer, "PRIVATE", NodeOrigin: [new("PRIVATE", "PRIVATE")]) }
        ], "acme", [declaration]));
        Assert.Equal(expected ? pointer : "/variables", result.Location!.JsonPointer);
        Assert.Equal("acme", result.Location.ProviderKey);
        Assert.Null(result.Location.ReferenceKey);
        Assert.Null(result.Location.NodeOrigin);
        Assert.DoesNotContain("PRIVATE", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("/variables/{index}/{index}")]
    [InlineData("/variables/../{index}")]
    [InlineData("/variables/{name}/initialValue")]
    public void Invalid_location_patterns_fail_closed(string pattern)
    {
        var result = Assert.Single(ActivityProviderDiagnosticSanitizer.Sanitize([PrivateDiagnostic()], "acme",
            [Guidance with { JsonPointerPattern = pattern }]));
        Assert.Null(result.Location!.JsonPointer);
        Assert.Empty(result.Metadata);
    }

    private static ActivityDiagnostic PrivateDiagnostic() => new(
        Guidance.Code, ActivityDiagnosticSeverity.Error, "PRIVATE payload and credential", new("PRIVATE", "PRIVATE"),
        new("PRIVATE", "/PRIVATE"), "PRIVATE endpoint", new Dictionary<string, string> { ["actionLabel"] = "PRIVATE", ["secret"] = "PRIVATE" });

    private sealed class Provider(IReadOnlyList<ActivityProviderDiagnosticDescriptor> declarations) : IActivityProvider, IActivityProviderDiagnosticCatalog
    {
        public string ProviderKey => "acme";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new("Acme", [new("1", true, new HashSet<string> { "1" })], new([]));
        public IReadOnlyList<ActivityProviderDiagnosticDescriptor> DiagnosticDescriptors => declarations;
        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([PrivateDiagnostic()]);
        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityContractProposal([], [PrivateDiagnostic()]));
        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(new ActivityManifestMigration(null, [PrivateDiagnostic()]));
    }
}
