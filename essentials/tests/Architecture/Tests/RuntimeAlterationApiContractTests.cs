using System.Text.Json;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Elsa.Architecture.Tests;

public sealed class RuntimeAlterationApiContractTests
{
    private static readonly string ContractPath =
        Path.Combine(AppContext.BaseDirectory, "Contracts", "runtime-alterations.openapi.yaml");

    [Fact]
    public void Plan_job_and_outcome_schemas_match_the_serialized_safe_views()
    {
        var document = LoadContract();
        var schemas = Mapping(Mapping(document, "components"), "schemas");
        var plan = Mapping(schemas, "AlterationPlan");
        var job = Mapping(schemas, "AlterationJob");
        var outcome = Mapping(schemas, "AlterationOutcome");
        var provenance = Mapping(schemas, "OperatorProvenance");

        Assert.False(job.Children.ContainsKey(new YamlScalarNode("allOf")));
        Assert.Equal(PropertyNames<WorkflowAlterationPlanView>(), PropertyNames(Mapping(plan, "properties")));
        Assert.Equal(PropertyNames<WorkflowAlterationJobView>(), PropertyNames(Mapping(job, "properties")));
        Assert.Equal(PropertyNames<WorkflowAlterationOutcomeView>(), PropertyNames(Mapping(outcome, "properties")));
        Assert.Equal(PropertyNames<WorkflowAlterationOperatorProvenanceView>(), PropertyNames(Mapping(provenance, "properties")));
        Assert.Contains("structuralMetadata", Required(outcome));

        var failure = Mapping(schemas, "SafeFailure");
        var messageTypes = Sequence(Mapping(Mapping(failure, "properties"), "message"), "type");
        Assert.Equal(["string", "null"], messageTypes);
    }

    [Fact]
    public void Every_implemented_safe_server_error_is_declared()
    {
        var document = LoadContract();
        var paths = Mapping(document, "paths");
        AssertResponses(paths, "/runtime/workflows/alteration-plans", "post", "400", "500");
        AssertResponses(paths, "/runtime/workflows/alteration-plans/{planId}", "get", "400", "500");
        AssertResponses(paths, "/runtime/workflows/alteration-plans/{planId}/jobs/page", "get", "400", "500");
        AssertResponses(paths, "/runtime/workflows/alteration-plans/{planId}/jobs/{jobId}", "get", "400", "500");
        AssertResponses(paths, "/runtime/workflows/alteration-plans/{planId}/cancel", "post", "400", "500");
    }

    private static YamlMappingNode LoadContract()
    {
        using var reader = File.OpenText(ContractPath);
        var yaml = new YamlStream();
        yaml.Load(reader);
        return Assert.IsType<YamlMappingNode>(Assert.Single(yaml.Documents).RootNode);
    }

    private static void AssertResponses(YamlMappingNode paths, string path, string method, params string[] statuses)
    {
        var responses = Mapping(Mapping(Mapping(paths, path), method), "responses");
        Assert.All(statuses, status => Assert.True(responses.Children.ContainsKey(new YamlScalarNode(status))));
    }

    private static IReadOnlyCollection<string> PropertyNames<T>() =>
        typeof(T).GetProperties()
            .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyCollection<string> PropertyNames(YamlMappingNode properties) =>
        properties.Children.Keys
            .Cast<YamlScalarNode>()
            .Select(key => key.Value!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyCollection<string> Required(YamlMappingNode schema) =>
        Sequence(schema, "required");

    private static IReadOnlyCollection<string> Sequence(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlSequenceNode>(parent.Children[new YamlScalarNode(key)]).Children
            .Cast<YamlScalarNode>()
            .Select(item => item.Value!)
            .ToArray();

    private static YamlMappingNode Mapping(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlMappingNode>(parent.Children[new YamlScalarNode(key)]);
}
