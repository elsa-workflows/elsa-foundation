using Elsa.Activities.Http.Activities;
using Elsa.Contracts.Generator;
using Xunit;

namespace Elsa.Contracts.Tests;

/// <summary>
/// Per-surface projection tests over real product assemblies copied into the test output
/// (spec 149 US1/US2, §2.23.2). The projector must produce the same facts the served catalog carries —
/// the equivalence tests prove that end to end; these prove the individual surfaces.
/// </summary>
public sealed class FragmentProjectorTests
{
    private static readonly string BaseDirectory = AppContext.BaseDirectory;
    private static readonly string[] ReferencePaths = Directory.EnumerateFiles(BaseDirectory, "*.dll").ToArray();

    private static ContractFragment Project(string assemblyName)
    {
        TargetAssembly.AddProbeDirectory(BaseDirectory);
        var diagnostics = new Diagnostics();
        var index = FeatureIndex.Build(
            new[]
            {
                Path.Combine(BaseDirectory, "Elsa.Expressions.JavaScript.Rendering.dll"),
                Path.Combine(BaseDirectory, "Elsa.Http.JavaScript.dll"),
                Path.Combine(BaseDirectory, assemblyName + ".dll")
            }.Distinct().Where(File.Exists),
            diagnostics);
        var fragment = new FragmentProjector(diagnostics, index).Project(Path.Combine(BaseDirectory, assemblyName + ".dll"), ReferencePaths);
        Assert.False(diagnostics.HasErrors, string.Join(Environment.NewLine, diagnostics.Errors));
        return fragment;
    }

    [Fact]
    public void Http_fragment_carries_feature_metadata_with_structural_depends_on()
    {
        var fragment = Project("Elsa.Activities.Http");

        var feature = Assert.Single(fragment.Features, f => f.Id == "ActivitiesHttp");
        Assert.Contains("Http", feature.DependsOn);
        Assert.Contains("WorkflowsRuntimeHttp", feature.DependsOn);
        var basePath = Assert.Single(feature.Options, option => option.Name == "BasePath");
        Assert.Equal("/workflows/http", basePath.DefaultValue?.GetString());
    }

    [Fact]
    public void HttpEndpoint_contract_fixes_the_G1_and_G2_repros()
    {
        var fragment = Project("Elsa.Activities.Http");

        var endpoint = Assert.Single(fragment.Activities, activity => activity.ActivityTypeKey == typeof(HttpEndpoint).FullName);
        Assert.Equal("ActivitiesHttp", endpoint.FeatureId);

        var responseMode = Assert.Single(endpoint.Inputs, input => input.Name == nameof(HttpEndpoint.ResponseMode));
        Assert.True(responseMode.HasStaticDefault);
        Assert.Equal("async", responseMode.DefaultValue?.GetString());

        var outputs = endpoint.Outputs.ToDictionary(output => output.ReferenceKey, StringComparer.Ordinal);
        Assert.True(outputs["Request"].IsRequired);
        Assert.True(outputs["RouteData"].IsRequired);
        Assert.False(outputs["ParsedContent"].IsRequired);

        Assert.NotEmpty(endpoint.ContentHash);
    }

    [Fact]
    public void Sequence_fragment_carries_its_structure_kind_with_payload_schema()
    {
        var fragment = Project("Elsa.Activities.Sequence");

        var structure = Assert.Single(fragment.Structures, s => s.Kind == "elsa.sequence.structure");
        Assert.Equal("1.0.0", structure.SchemaVersion);
        Assert.NotNull(structure.PayloadSchema);
    }

    [Fact]
    public void Jint_fragment_carries_the_declared_sandbox_surface()
    {
        var fragment = Project("Elsa.Expressions.JavaScript.Jint");

        Assert.NotNull(fragment.Expressions);
        var sandbox = fragment.Expressions!.ScriptSandbox;
        Assert.Contains(sandbox, entry => entry is { Name: "getVariable", Kind: "function" });
        Assert.Contains(sandbox, entry => entry is { Name: "Math.random", Kind: "disabledIntrinsic" });
    }

    [Fact]
    public void HttpJavaScript_fragment_captures_declaration_contributions_via_the_depends_on_closure()
    {
        var fragment = Project("Elsa.Http.JavaScript");

        Assert.NotNull(fragment.Expressions);
        var contribution = Assert.Single(fragment.Expressions!.JavaScriptDeclarations);
        Assert.Contains("HttpTypeDeclarationContributor", contribution.Contributor, StringComparison.Ordinal);
        Assert.NotEmpty(contribution.Types);
    }

    [Fact]
    public void DesignApi_fragment_publishes_the_engine_intrinsics_with_stable_ids()
    {
        var fragment = Project("Elsa.Activities.Design.Api");

        Assert.Equal(5, fragment.Intrinsics.Count);
        var setVariable = Assert.Single(fragment.Intrinsics, intrinsic => intrinsic.DescriptorId == "elsa.intrinsic.set@1");
        Assert.Equal("Elsa.SetVariable", setVariable.ActivityTypeKey);
        Assert.Equal("Set", setVariable.Intrinsic.Kind);
    }

    [Fact]
    public void Assembly_without_contributions_yields_no_fragment_content()
    {
        // Elsa.Primitives: no features, activities, structures, expressions, or intrinsics.
        var fragment = Project("Elsa.Primitives");

        Assert.False(fragment.HasContributions);
    }

    [Fact]
    public void Fragment_self_declares_its_schema_version()
    {
        var fragment = Project("Elsa.Activities.Http");

        Assert.Equal(ContractFragment.CurrentSchemaVersion, fragment.SchemaVersion);
    }

    [Fact]
    public void Fragment_projection_is_deterministic_across_runs()
    {
        var first = DeterministicJson.SerializeToBytes(Project("Elsa.Activities.Http"));
        var second = DeterministicJson.SerializeToBytes(Project("Elsa.Activities.Http"));

        Assert.Equal(first, second);
    }
}
