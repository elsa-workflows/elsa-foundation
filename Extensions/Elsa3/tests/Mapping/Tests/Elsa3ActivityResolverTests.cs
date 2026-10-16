using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3ActivityResolverTests
{
    private const string WriteLineKey = "Elsa.Activities.Primitives.Activities.WriteLine";

    private static readonly IReadOnlyDictionary<string, Elsa3ActivityExactReplacement> NoReplacements =
        new Dictionary<string, Elsa3ActivityExactReplacement>(StringComparer.Ordinal);

    [Fact]
    public async Task Built_in_entry_resolves_to_the_catalog_version_of_its_target()
    {
        var catalog = RealActivityCatalog.Create();
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, catalog);

        var resolution = await resolver.ResolveAsync(Node("Elsa.WriteLine"), NoReplacements, default);

        Assert.Null(resolution.Failure);
        Assert.Equal(Elsa3ResolvedSource.BuiltInMap, resolution.Activity!.Source);
        Assert.Equal(WriteLineKey, resolution.Activity.ActivityTypeKey);
        Assert.Equal(catalog.VersionIdOf(WriteLineKey), resolution.Activity.ActivityVersionId);
        Assert.Contains(resolution.Activity.Inputs, input => input.ReferenceKey == "text");
    }

    [Fact]
    public async Task Highest_valid_semver_wins_and_the_elsa3_version_is_ignored()
    {
        var catalog = RealActivityCatalog.Create()
            .WithVersion(WriteLineKey, "99.0.0")
            .WithVersion(WriteLineKey, "99.1.0+abc")
            .WithInvalidVersion(WriteLineKey, "not-semver");
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, catalog);
        var node = Node("Elsa.WriteLine");
        node.Version = 99;

        var resolution = await resolver.ResolveAsync(node, NoReplacements, default);

        Assert.Equal(
            Elsa.Activities.Design.Core.Reconciliation.ActivityCatalogStableIds.VersionId(WriteLineKey, "99.1.0+abc"),
            resolution.Activity!.ActivityVersionId);
    }

    [Fact]
    public async Task A_target_with_no_valid_version_is_version_unavailable()
    {
        var catalog = RealActivityCatalog.Create().WithoutVersions(WriteLineKey);
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, catalog);

        var resolution = await resolver.ResolveAsync(Node("Elsa.WriteLine"), NoReplacements, default);

        Assert.Equal(Elsa3ActivityDiagnosticCodes.VersionUnavailable, resolution.Failure!.Code);
        Assert.Contains(WriteLineKey, resolution.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_replacement_wins_over_every_other_step()
    {
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, RealActivityCatalog.Create());
        var input = new InputDefinition("Message", "Message", new TypeReference("String"), null, "Message", null, true);
        var replacements = new Dictionary<string, Elsa3ActivityExactReplacement>(StringComparer.Ordinal)
        {
            ["node-1"] = new("reusable-version", [input], new HashSet<string>(["Result"], StringComparer.Ordinal))
        };

        var resolution = await resolver.ResolveAsync(Node("Elsa.WriteLine"), replacements, default);

        Assert.Equal(Elsa3ResolvedSource.Replacement, resolution.Activity!.Source);
        Assert.Equal("reusable-version", resolution.Activity.ActivityVersionId);
        Assert.Same(input, Assert.Single(resolution.Activity.Inputs));
        Assert.Equal("Result", Assert.Single(resolution.Activity.Outputs).ReferenceKey);
    }

    [Fact]
    public async Task An_intrinsic_resolves_without_the_catalog()
    {
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, RealActivityCatalog.Create());

        var resolution = await resolver.ResolveAsync(Node("Elsa.Correlate"), NoReplacements, default);

        Assert.Equal(Elsa3ResolvedSource.Intrinsic, resolution.Activity!.Source);
        Assert.Equal(Elsa3IntrinsicContracts.SetCorrelationId, resolution.Activity.ActivityVersionId);
        Assert.True(Assert.Single(resolution.Activity.Inputs).IsRequired);
    }

    [Theory]
    [InlineData("Elsa.FlowFork", Elsa3ActivityDiagnosticCodes.GraphConversionPending)]
    [InlineData("Elsa.StateMachine", Elsa3ActivityDiagnosticCodes.NoCounterpart)]
    [InlineData("Acme.Unknown", Elsa3ActivityDiagnosticCodes.UnmappedType)]
    public async Task Unresolvable_types_fail_with_their_code(string type, string code)
    {
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, RealActivityCatalog.Create());

        var resolution = await resolver.ResolveAsync(Node(type), NoReplacements, default);

        Assert.Null(resolution.Activity);
        Assert.Equal(code, resolution.Failure!.Code);
    }

    [Fact]
    public async Task A_full_elsa4_type_key_resolves_verbatim()
    {
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, RealActivityCatalog.Create());

        var resolution = await resolver.ResolveAsync(Node(WriteLineKey), NoReplacements, default);

        Assert.Equal(Elsa3ResolvedSource.Verbatim, resolution.Activity!.Source);
        Assert.Null(resolution.Activity.Entry);
    }

    [Fact]
    public async Task Resolutions_are_cached_per_type_for_the_scope()
    {
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, RealActivityCatalog.Create());

        var first = await resolver.ResolveAsync(Node("Elsa.WriteLine", "a"), NoReplacements, default);
        var second = await resolver.ResolveAsync(Node("Elsa.WriteLine", "b"), NoReplacements, default);

        Assert.Same(first, second);
    }

    // Spec 239 R-3: a resolved activity carries the outcomes graph conversion routes connections by.

    [Theory]
    [InlineData("Elsa.If", new[] { "Break", "False", "True" })]
    [InlineData("Elsa.WriteLine", new[] { "Done" })]
    [InlineData("Elsa.HttpEndpoint", new[] { "Done" })]
    [InlineData("Elsa.SetVariable", new[] { "Done" })]
    public async Task A_resolved_activity_carries_its_static_outcomes(string type, string[] expected)
    {
        var resolver = new Elsa3ActivityResolver(new Elsa3BuiltInActivityMap(), RealActivityCatalog.Create());

        var resolution = await resolver.ResolveAsync(Node(type), NoReplacements, default);

        Assert.Equal(expected, resolution.Activity!.Outcomes.Ports.Order(StringComparer.Ordinal));
        Assert.Null(resolution.Activity.Outcomes.Dynamic);
    }

    [Theory]
    [InlineData("Elsa.FlowSendHttpRequest", "ExpectedStatusCodes", "Unmatched status code")]
    [InlineData("Elsa.RunJavaScript", "possibleOutcomes", "Unmatched")]
    public async Task Outcomes_named_by_an_input_carry_their_rule(string type, string sourceInput, string unmatched)
    {
        var resolver = new Elsa3ActivityResolver(new Elsa3BuiltInActivityMap(), RealActivityCatalog.Create());

        var resolution = await resolver.ResolveAsync(Node(type), NoReplacements, default);

        var dynamic = resolution.Activity!.Outcomes.Dynamic!;
        Assert.Equal(sourceInput, dynamic.SourceInputKey);
        Assert.Equal(unmatched, dynamic.UnmatchedOutcome);
    }

    [Fact]
    public async Task A_replacement_carries_the_reusable_contract_outcomes()
    {
        var replacements = new Dictionary<string, Elsa3ActivityExactReplacement>(StringComparer.Ordinal)
        {
            ["node-1"] = new("reusable@1", [], new HashSet<string>(), new HashSet<string>(StringComparer.Ordinal) { "succeed", "Done" })
        };
        var resolver = new Elsa3ActivityResolver(TestMap.WriteLine, RealActivityCatalog.Create());

        var resolution = await resolver.ResolveAsync(Node("Acme.Sub"), replacements, default);

        Assert.Equal(["Done", "succeed"], resolution.Activity!.Outcomes.Ports);
    }

    [Theory]
    [InlineData("Elsa.FlowFork", Elsa3GraphConstructKind.Fork)]
    [InlineData("Elsa.FlowJoin", Elsa3GraphConstructKind.Join)]
    [InlineData("Elsa.Start", Elsa3GraphConstructKind.Start)]
    public async Task Flowchart_constructs_resolve_without_the_catalog(string type, Elsa3GraphConstructKind kind)
    {
        var resolver = new Elsa3ActivityResolver(new Elsa3BuiltInActivityMap(), RealActivityCatalog.Create());

        var resolution = await resolver.ResolveAsync(Node(type), NoReplacements, default);

        Assert.Equal(Elsa3ResolvedSource.GraphConstruct, resolution.Activity!.Source);
        Assert.Equal(kind, Assert.IsType<Elsa3ActivityTarget.GraphConstruct>(resolution.Activity.Entry!.Target).Kind);
        Assert.Empty(resolution.Activity.Outcomes.Ports);
    }

    private static Elsa3Activity Node(string type, string nodeId = "node-1") =>
        new() { Id = nodeId, NodeId = nodeId, Name = nodeId, Type = type };

    private sealed class TestMap(params Elsa3ActivityMapEntry[] entries) : IElsa3ActivityMap
    {
        public static readonly TestMap WriteLine = new(
            new Elsa3ActivityMapEntry("Elsa.WriteLine", new Elsa3ActivityTarget.CatalogActivity(WriteLineKey), [], []),
            new Elsa3ActivityMapEntry("Elsa.Correlate", new Elsa3ActivityTarget.Intrinsic(Elsa3IntrinsicContracts.SetCorrelationId), [], []),
            new Elsa3ActivityMapEntry("Elsa.FlowFork", new Elsa3ActivityTarget.GraphLevel("fork", "#2705"), [], []),
            new Elsa3ActivityMapEntry("Elsa.StateMachine", new Elsa3ActivityTarget.NoCounterpart("none"), [], []));

        public IReadOnlyList<Elsa3ActivityMapEntry> Entries => entries;

        public Elsa3ActivityMapEntry? Find(string elsa3TypeName) =>
            entries.FirstOrDefault(entry => entry.Elsa3TypeName == elsa3TypeName);
    }
}
