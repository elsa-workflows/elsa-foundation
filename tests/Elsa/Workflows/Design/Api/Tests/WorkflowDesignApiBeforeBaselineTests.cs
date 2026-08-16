using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Api.Compatibility.Testing.Baselines;
using Elsa.Api.Compatibility.Testing.Serialization;
using Elsa.Workflows.Design.Api.Tests.Support;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests;

public sealed class WorkflowDesignApiBeforeBaselineTests
{
    [Fact]
    public void FastEndpoints_before_http_fixture_covers_exactly_all_27_registrations()
    {
        var observations = WorkflowDesignCompatibilityEvidence.LoadLegacyHttp();
        Assert.Equal(27, observations.Count);
        Assert.Equal(
            WorkflowDesignCompatibilityCases.All.Select(testCase => testCase.Endpoint + "|" + testCase.Case).Order(),
            observations.Select(observation => observation.Endpoint + "|" + observation.Case).Order());
        Assert.All(observations, observation =>
        {
            Assert.Equal(401, observation.StatusCode);
            Assert.Equal("401", observation.Status);
            Assert.NotEmpty(observation.Binding);
        });
    }

    [Fact]
    public void FastEndpoints_before_openapi_fixture_covers_exactly_all_27_operations()
    {
        var operations = WorkflowDesignCompatibilityEvidence.LoadLegacyOpenApi().Operations;
        Assert.Equal(27, operations.Count);
        Assert.Equal(
            WorkflowDesignCompatibilityCases.All.Select(testCase => testCase.Endpoint.ToString()).Order(),
            operations.Select(operation => operation.Endpoint.ToString()).Order());
        Assert.All(operations, operation =>
        {
            Assert.NotEmpty(operation.Responses);
            Assert.NotEmpty(operation.Schemas);
        });
    }

    [Fact]
    public void Before_fixtures_are_byte_stable_and_have_provenance_hashes()
    {
        var directory = Path.Join(AppContext.BaseDirectory, "Baselines");
        var httpPath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.HttpFileName);
        var openApiPath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.OpenApiFileName);
        var provenancePath = Path.Join(directory, WorkflowDesignCompatibilityEvidence.ProvenanceFileName);
        using var provenance = JsonDocument.Parse(BaselineFile.Read(provenancePath));
        var root = provenance.RootElement;

        Assert.Equal(27, root.GetProperty("registrationCount").GetInt32());
        Assert.Equal(Hash(httpPath), root.GetProperty("httpSha256").GetString());
        Assert.Equal(Hash(openApiPath), root.GetProperty("openApiSha256").GetString());
        Assert.Equal("#1372", root.GetProperty("issue").GetString());
        Assert.Equal("c04d8dbbe", root.GetProperty("sourceCommit").GetString());

        var firstHttp = CompatibilityJson.Serialize(BaselineFile.Load<object>(httpPath));
        var secondHttp = CompatibilityJson.Serialize(BaselineFile.Load<object>(httpPath));
        Assert.Equal(firstHttp, secondHttp);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
