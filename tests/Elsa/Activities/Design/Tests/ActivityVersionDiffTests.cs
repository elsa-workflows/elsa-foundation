using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityVersionDiffTests
{
    public static TheoryData<string, ActivityContract, ActivityContract, ActivityVersionCompatibility, ActivityVersionBump> CompatibilityMatrix => new()
    {
        { "no change", Contract(), Contract(), ActivityVersionCompatibility.Identical, ActivityVersionBump.None },
        { "optional input added", Contract(), Contract(inputs: [Input("added")]), ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor },
        { "required input with default added", Contract(), Contract(inputs: [Input("added", required: true, @default: Default("1"))]), ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor },
        { "required input without default added", Contract(), Contract(inputs: [Input("added", required: true)]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "optional output added", Contract(), Contract(outputs: [Output("added")]), ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor },
        { "required output added", Contract(), Contract(outputs: [Output("added", required: true)]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "optional output becomes required", Contract(outputs: [Output("value")]), Contract(outputs: [Output("value", required: true)]), ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor },
        { "required output becomes optional", Contract(outputs: [Output("value", required: true)]), Contract(outputs: [Output("value")]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "non-emitted outcome added", Contract(), Contract(outcomes: [Outcome("added")]), ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor },
        { "emitted outcome added", Contract(), Contract(outcomes: [Outcome("added", emitted: true)]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "input removed", Contract(inputs: [Input("value")]), Contract(), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "input renamed", Contract(inputs: [Input("value", name: "Before")]), Contract(inputs: [Input("value", name: "After")]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "input type changed", Contract(inputs: [Input("value")]), Contract(inputs: [Input("value", alias: "number")]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "optional input becomes required", Contract(inputs: [Input("value")]), Contract(inputs: [Input("value", required: true)]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "input nullability tightened", Contract(inputs: [Input("value", nullable: true)]), Contract(inputs: [Input("value")]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "input nullability relaxed", Contract(inputs: [Input("value")]), Contract(inputs: [Input("value", nullable: true)]), ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor },
        { "output nullability tightened", Contract(outputs: [Output("value", nullable: true)]), Contract(outputs: [Output("value")]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "output nullability relaxed", Contract(outputs: [Output("value")]), Contract(outputs: [Output("value", nullable: true)]), ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor },
        { "default added", Contract(inputs: [Input("value")]), Contract(inputs: [Input("value", @default: Default("1"))]), ActivityVersionCompatibility.Compatible, ActivityVersionBump.Minor },
        { "default removed", Contract(inputs: [Input("value", @default: Default("1"))]), Contract(inputs: [Input("value")]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "default changed", Contract(inputs: [Input("value", @default: Default("1"))]), Contract(inputs: [Input("value", @default: Default("2"))]), ActivityVersionCompatibility.Breaking, ActivityVersionBump.Major },
        { "presentation changed", Contract(inputs: [Input("value", displayName: "Before")]), Contract(inputs: [Input("value", displayName: "After")]), ActivityVersionCompatibility.NonBehavioral, ActivityVersionBump.Patch }
    };

    [Theory]
    [MemberData(nameof(CompatibilityMatrix))]
    public async Task Applies_the_complete_baseline_matrix(
        string _,
        ActivityContract before,
        ActivityContract after,
        ActivityVersionCompatibility compatibility,
        ActivityVersionBump bump)
    {
        var result = await new ActivityVersionDiffer().DiffAsync(Request(before, after));

        Assert.Equal(compatibility, result.Compatibility);
        Assert.Equal(bump, result.RequiredBump);
    }

    [Fact]
    public async Task Nullability_changes_include_exact_member_projections()
    {
        var result = await new ActivityVersionDiffer().DiffAsync(Request(
            Contract(inputs: [Input("value", nullable: true)]),
            Contract(inputs: [Input("value")])));

        var change = Assert.Single(result.Changes);
        Assert.Equal("NullabilityChanged", change.Kind);
        Assert.True(change.Before!.Value.GetProperty("isNullable").GetBoolean());
        Assert.False(change.After!.Value.GetProperty("isNullable").GetBoolean());
    }

    [Fact]
    public async Task Provider_rules_can_strengthen_but_never_weaken_platform_changes()
    {
        var platformId = "contract:input:added:member-added";
        var providerChanges = new[]
        {
            Change(platformId, ActivityVersionChangeImpact.NonBehavioral, ActivityVersionBump.Patch, "Provider attempted to weaken."),
            Change("provider:acme.script:signature", ActivityVersionChangeImpact.Breaking, ActivityVersionBump.Major, "Provider strengthened compatibility.")
        };

        var result = await new ActivityVersionDiffer().DiffAsync(Request(
            Contract(),
            Contract(inputs: [Input("added")]),
            providerChanges: providerChanges));

        Assert.Equal(ActivityVersionBump.Minor, result.Changes.Single(x => x.ChangeId == platformId).RequiredBump);
        Assert.Equal(ActivityVersionBump.Major, result.RequiredBump);
        Assert.Equal(ActivityVersionCompatibility.Breaking, result.Compatibility);
    }

    [Fact]
    public async Task Change_ids_and_order_are_deterministic_and_projections_never_include_provider_payloads()
    {
        var fromProvider = Manifest("{\"secret\":\"from-secret\"}");
        var toProvider = new ActivityProviderManifest("target.provider", "2", Json("{\"secret\":\"to-secret\"}"));
        var request = Request(
            Contract(inputs: [Input("z"), Input("a")]),
            Contract(inputs: [Input("z", required: true), Input("a", alias: "number")]),
            fromProvider,
            toProvider);

        var first = await new ActivityVersionDiffer().DiffAsync(request);
        var second = await new ActivityVersionDiffer().DiffAsync(request);
        var serialized = JsonSerializer.Serialize(first);

        Assert.Equal(first.Changes.Select(x => x.ChangeId), second.Changes.Select(x => x.ChangeId));
        Assert.Equal(first.Changes.Select(x => x.ChangeId).Order(StringComparer.Ordinal), first.Changes.Select(x => x.ChangeId));
        Assert.DoesNotContain("from-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("to-secret", serialized, StringComparison.Ordinal);
        Assert.All(first.Changes, x => Assert.Matches("^[a-z0-9.:-]+$", x.ChangeId));
    }

    [Fact]
    public async Task Template_hash_changes_are_an_explicit_behavior_signal_requiring_a_minor_bump()
    {
        var result = await new ActivityVersionDiffer().DiffAsync(Request(
            Contract(),
            Contract(),
            from: Identity("sha256-before"),
            to: Identity("sha256-after", kind: "ActivityDraft")));

        Assert.True(result.BehaviorChanged);
        Assert.Equal(ActivityVersionBump.Minor, result.RequiredBump);
        Assert.Equal(ActivityVersionCompatibility.Compatible, result.Compatibility);
        Assert.Contains(result.Changes, x => x.Kind == "ImplementationChanged");
    }

    [Fact]
    public async Task Provider_and_runtime_behavior_changes_can_never_be_non_behavioral_patch_changes()
    {
        var fromFacts = new ActivityVersionImplementationFacts(RuntimeRequirements: []);
        var toFacts = new ActivityVersionImplementationFacts(RuntimeRequirements: [new("elsa.graph-activity", "1")]);
        var request = Request(Contract(), Contract(), toProvider: new("target.provider", "2", Json("{}"))) with
        {
            FromImplementation = fromFacts,
            ToImplementation = toFacts
        };

        var result = await new ActivityVersionDiffer().DiffAsync(request);

        Assert.True(result.BehaviorChanged);
        Assert.Equal(ActivityVersionCompatibility.Compatible, result.Compatibility);
        Assert.Equal(ActivityVersionBump.Minor, result.RequiredBump);
        Assert.Contains(result.Changes, x => x.Kind == "ProviderChanged" && x.Impact == ActivityVersionChangeImpact.Additive);
        Assert.Contains(result.Changes, x => x.Kind == "RuntimeRequirementAdded" && x.RequiredBump == ActivityVersionBump.Minor);
    }

    [Fact]
    public async Task Runtime_requirement_removal_or_schema_change_is_breaking()
    {
        var request = Request(Contract(), Contract()) with
        {
            FromImplementation = new(RuntimeRequirements: [new("elsa.graph-activity", "1"), new("obsolete", "1")]),
            ToImplementation = new(RuntimeRequirements: [new("elsa.graph-activity", "2")])
        };

        var result = await new ActivityVersionDiffer().DiffAsync(request);

        Assert.Equal(ActivityVersionBump.Major, result.RequiredBump);
        Assert.Contains(result.Changes, x => x.Kind == "RuntimeRequirementRemoved" && x.Impact == ActivityVersionChangeImpact.Breaking);
        Assert.Contains(result.Changes, x => x.Kind == "RuntimeRequirementSchemaChanged" && x.Impact == ActivityVersionChangeImpact.Breaking);
    }

    [Fact]
    public async Task Multiple_schema_versions_for_one_runtime_consumer_are_compared_as_a_set()
    {
        var additiveRequest = Request(Contract(), Contract()) with
        {
            FromImplementation = new(RuntimeRequirements: [new("shared-consumer", "1")]),
            ToImplementation = new(RuntimeRequirements: [new("shared-consumer", "1"), new("shared-consumer", "2")])
        };
        var breakingRequest = additiveRequest with
        {
            FromImplementation = additiveRequest.ToImplementation,
            ToImplementation = additiveRequest.FromImplementation
        };

        var additive = await new ActivityVersionDiffer().DiffAsync(additiveRequest);
        var breaking = await new ActivityVersionDiffer().DiffAsync(breakingRequest);

        var additiveChange = Assert.Single(additive.Changes, x => x.Kind == "RuntimeRequirementSchemaSetChanged");
        Assert.Equal(ActivityVersionChangeImpact.Additive, additiveChange.Impact);
        Assert.Equal(ActivityVersionBump.Minor, additive.RequiredBump);
        var breakingChange = Assert.Single(breaking.Changes, x => x.Kind == "RuntimeRequirementSchemaSetChanged");
        Assert.Equal(ActivityVersionChangeImpact.Breaking, breakingChange.Impact);
        Assert.Equal(ActivityVersionBump.Major, breaking.RequiredBump);
    }

    [Fact]
    public async Task Layout_only_change_is_patch_nonbehavioral()
    {
        var request = Request(Contract(), Contract()) with
        {
            FromImplementation = new(LayoutHash: "layout-before", LayoutCount: 1),
            ToImplementation = new(LayoutHash: "layout-after", LayoutCount: 2)
        };

        var result = await new ActivityVersionDiffer().DiffAsync(request);

        Assert.False(result.BehaviorChanged);
        Assert.Equal(ActivityVersionCompatibility.NonBehavioral, result.Compatibility);
        Assert.Equal(ActivityVersionBump.Patch, result.RequiredBump);
        Assert.Equal("LayoutChanged", Assert.Single(result.Changes).Kind);
    }

    [Fact]
    public async Task Semantically_equal_default_json_does_not_create_a_false_change()
    {
        var before = new ActivityInputDefault("Literal", Json("{\"z\":2,\"a\":1}"));
        var after = new ActivityInputDefault("Literal", Json("{\"a\":1,\"z\":2}"));

        var result = await new ActivityVersionDiffer().DiffAsync(Request(
            Contract(inputs: [Input("value", @default: before)]),
            Contract(inputs: [Input("value", @default: after)])));

        Assert.Equal(ActivityVersionCompatibility.Identical, result.Compatibility);
    }

    private static ActivityVersionDiffRequest Request(
        ActivityContract before,
        ActivityContract after,
        ActivityProviderManifest? fromProvider = null,
        ActivityProviderManifest? toProvider = null,
        ActivityVersionIdentity? from = null,
        ActivityVersionIdentity? to = null,
        IReadOnlyList<ActivityVersionChange>? providerChanges = null) => new(
        from ?? Identity("sha256-same"),
        to ?? Identity("sha256-same", kind: "ActivityDraft"),
        before,
        after,
        fromProvider ?? Manifest("{}"),
        toProvider ?? Manifest("{}"),
        [],
        [],
        providerChanges ?? []);

    private static ActivityVersionIdentity Identity(string? hash, string kind = "ActivityVersion") =>
        kind == "ActivityVersion"
            ? new(kind, "definition-1", VersionId: "version-1", Version: "1.0.0", TemplateHash: hash)
            : new(kind, "definition-1", DraftId: "draft-1", Revision: 3, TemplateHash: hash);

    private static ActivityContract Contract(
        IReadOnlyList<ActivityInputContract>? inputs = null,
        IReadOnlyList<ActivityOutputContract>? outputs = null,
        IReadOnlyList<ActivityOutcomeContract>? outcomes = null) => new("1", inputs ?? [], outputs ?? [], outcomes ?? []);

    private static ActivityInputContract Input(
        string key,
        bool required = false,
        ActivityInputDefault? @default = null,
        string alias = "string",
        string? name = null,
        string? displayName = null,
        bool nullable = false) => new(
        key,
        name ?? key,
        new(alias, CollectionKind.Single),
        required,
        nullable,
        @default,
        "elsa.json",
        DisplayName: displayName);

    private static ActivityOutputContract Output(string key, bool required = false, bool nullable = false) =>
        new(key, key, new("string", CollectionKind.Single), required, nullable, "elsa.json");

    private static ActivityOutcomeContract Outcome(string key, bool emitted = false) => new(key, key, emitted);

    private static ActivityInputDefault Default(string json) => new("Literal", Json(json));

    private static ActivityProviderManifest Manifest(string json) => new("elsa.activity-graph", "1", Json(json));

    private static ActivityVersionChange Change(
        string id,
        ActivityVersionChangeImpact impact,
        ActivityVersionBump bump,
        string message) => new(
        id,
        ActivityVersionChangeArea.Provider,
        "acme.script.Compatibility",
        new(),
        Json("{\"secret\":\"must-not-pass-through\"}"),
        null,
        impact,
        bump,
        message);

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
