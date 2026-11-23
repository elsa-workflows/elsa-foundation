using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Etl.Core;
using Elsa.Etl.Design;

namespace Elsa.Etl.Tests;

/// <summary>Spec 1192: the optional continuous run policy and its validation.</summary>
public sealed class EtlContinuousRunPolicyTests
{
    private static readonly string[] FiniteRootMembers = ["source", "lookup", "routes", "mapping", "filters", "limits", "http"];

    [Fact]
    public void A_finite_pipeline_keeps_its_exact_v2_members_and_hash()
    {
        var document = EtlArtifactV2Json.Write(EtlV2TestPipelines.Create());

        Assert.Equal(FiniteRootMembers, document.EnumerateObject().Select(property => property.Name));
        Assert.True(FiniteHash == EtlArtifactJson.Hash(document), "First-stable hash freeze: changes require an explicit identity/migration decision.");
    }

    [Fact]
    public void A_continuous_policy_round_trips_and_an_explicit_null_reads_as_finite()
    {
        var continuous = EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(30) };

        var document = EtlArtifactV2Json.Write(continuous);
        var restored = EtlArtifactV2Json.Read(document);

        Assert.Equal(30, document.GetProperty("continuous").GetProperty("pollIntervalSeconds").GetInt32());
        Assert.Equal(continuous.Continuous, restored.Continuous);
        Assert.Equal(document.GetRawText(), EtlArtifactV2Json.Write(restored).GetRawText());

        var node = JsonNode.Parse(EtlArtifactV2Json.Write(EtlV2TestPipelines.Create()).GetRawText())!;
        node["continuous"] = null;
        var finite = EtlArtifactV2Json.Read(JsonDocument.Parse(node.ToJsonString()).RootElement);
        Assert.Null(finite.Continuous);
        Assert.True(FiniteHash == EtlArtifactJson.Hash(EtlArtifactV2Json.Write(finite)), "First-stable hash freeze: changes require an explicit identity/migration decision.");
    }

    [Fact]
    public void The_policy_requires_its_interval()
    {
        var node = JsonNode.Parse(EtlArtifactV2Json.Write(EtlV2TestPipelines.Create()).GetRawText())!;
        node["continuous"] = new JsonObject();

        Assert.ThrowsAny<JsonException>(() => EtlArtifactV2Json.Read(JsonDocument.Parse(node.ToJsonString()).RootElement));
    }

    [Theory]
    [MemberData(nameof(Violations))]
    public void Unsupported_continuous_pipelines_are_refused_by_the_shared_rule(string variant, string code) =>
        Assert.Equal(code, EtlContinuousRunPolicy.FindViolation(Variant(variant)));

    [Theory]
    [InlineData("token", 1)]
    [InlineData("nextLink", 86_400)]
    public void Cursor_paginated_http_pipelines_can_run_continuously(string pagination, int seconds)
    {
        var pipeline = EtlV2TestPipelines.Create();
        var source = (EtlHttpJsonSource)pipeline.Source;
        pipeline = pipeline with
        {
            Source = pagination == "token" ? source : source with { Pagination = new EtlNextLinkPagination("/next") },
            Continuous = new EtlContinuousRunPolicy(seconds)
        };

        Assert.Null(EtlContinuousRunPolicy.FindViolation(pipeline));
        Assert.Empty(new EtlArtifactCompiler().Compile("orders", 1, pipeline).Diagnostics);
    }

    [Theory]
    [MemberData(nameof(Violations))]
    public void The_design_compiler_reports_each_violation(string variant, string code)
    {
        var result = new EtlArtifactCompiler().Compile("orders", 1, Variant(variant));

        Assert.Null(result.Artifact);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code["etl.".Length..] && diagnostic.Path == "continuous");
    }

    public static TheoryData<string, string> Violations() => new()
    {
        { "csv-source", "etl.continuous-source-unsupported" },
        { "single-page", "etl.continuous-pagination-unsupported" },
        { "zero-interval", "etl.continuous-poll-interval-invalid" },
        { "long-interval", "etl.continuous-poll-interval-invalid" }
    };

    internal static EtlPipelineV2 Variant(string variant)
    {
        var pipeline = EtlV2TestPipelines.Create() with { Continuous = new EtlContinuousRunPolicy(30) };
        var source = (EtlHttpJsonSource)pipeline.Source;
        return variant switch
        {
            "csv-source" => pipeline with
            {
                Source = new EtlCsvSource("input", [new EtlColumn("name", EtlColumnType.String, true)]),
                Routes = new EtlRoutes(new EtlJsonlDestination("output"), new EtlJsonlDestination("errors"))
            },
            "single-page" => pipeline with { Source = source with { Pagination = new EtlSinglePagePagination() } },
            "zero-interval" => pipeline with { Continuous = new EtlContinuousRunPolicy(0) },
            "long-interval" => pipeline with { Continuous = new EtlContinuousRunPolicy(86_401) },
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
    }

    // The baseline pipeline's content hash from the finite writer; a serialized null policy would change it.
    private const string FiniteHash = "962310e12e298598a58e2846826db07a1369ec563821b67327d98896fb66f2cc";
}
