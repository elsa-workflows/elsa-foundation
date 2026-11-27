using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Services;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class HttpTestRunDriverTests
{
    private readonly HttpTestRunDriver _driver = new();

    [Fact]
    public void Supports_OnlyTheExactHttpEndpointBinding()
    {
        var context = Context("orders/{id}", "post");

        Assert.True(_driver.Supports(context));
        Assert.False(_driver.Supports(context with { ProviderId = "Elsa.Event" }));
        Assert.False(_driver.Supports(context with { Binding = context.Binding with { StimulusType = "Event" } }));
    }

    [Fact]
    public void Describe_ExposesOneHttpRequestModeWithSafeAuthoredRouteDetails()
    {
        var mode = Assert.Single(_driver.Describe(Context("orders/{id}", "post")));

        Assert.Equal("http-request", mode.ModeId);
        Assert.True(mode.IsDefault);
        Assert.Equal(1, mode.ConfigurationVersion);
        Assert.Equal("orders/{id}", mode.Configuration!.Value.GetProperty("template").GetString());
        Assert.Equal("POST", mode.Configuration.Value.GetProperty("method").GetString());
        Assert.Equal(JsonValueKind.Object, mode.ConfigurationSchema!.Value.ValueKind);
    }

    [Fact]
    public async Task Prepare_ReturnsAnIsolatedRealRequestActionWithoutEchoingRequestSecrets()
    {
        var context = Context("orders/{id}", "post");
        var scope = new WorkflowTestScope("run-1", DateTimeOffset.UtcNow.AddMinutes(5), "tenant-1", new("partition-1"));
        using var data = JsonDocument.Parse("""
            {
              "routeValues": { "id": "42" },
              "query": { "tag": ["one", "two"] },
              "headers": { "X-Private": ["secret-value"] },
              "body": "private-body"
            }
            """);
        var prepareContext = new TestRunTriggerPrepareContext(
            context, scope, "run-1", "http-request", data.RootElement, "target-1");

        var preparation = await _driver.PrepareAsync(prepareContext);

        Assert.Equal(TestRunPreparationKind.ExternalAction, preparation.Kind);
        Assert.Equal("http-request", preparation.Action!.Kind);
        Assert.Equal("POST", preparation.Action.Method);
        Assert.Equal("/workflows/test-runs/run-1/http/orders/42", preparation.Action.RelativeUrl);
        var serializedPreparation = JsonSerializer.Serialize(new
        {
            PreparationDetails = preparation.Details,
            ActionDetails = preparation.Action.Details
        });
        Assert.DoesNotContain("secret-value", serializedPreparation, StringComparison.Ordinal);
        Assert.DoesNotContain("private-body", serializedPreparation, StringComparison.Ordinal);
        Assert.Null(preparation.DriverState);
    }

    [Fact]
    public async Task Prepare_IncludesTheCurrentDeploymentPathBase()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.PathBase = new PathString("/tenant-host");
        var driver = new HttpTestRunDriver(new HttpContextAccessor { HttpContext = httpContext });
        using var data = JsonDocument.Parse("""{ "routeValues": { "id": "42" } }""");
        var scope = new WorkflowTestScope("run-1", DateTimeOffset.UtcNow.AddMinutes(5), "tenant-1", new("partition-1"));
        var prepareContext = new TestRunTriggerPrepareContext(
            Context("orders/{id}", "post"), scope, "run-1", "http-request", data.RootElement, "target-1");

        var preparation = await driver.PrepareAsync(prepareContext);

        Assert.Equal("/tenant-host/workflows/test-runs/run-1/http/orders/42", preparation.Action!.RelativeUrl);
    }

    [Theory]
    [InlineData("other-mode", "{}")]
    [InlineData("http-request", "{\"routeValues\":{\"other\":\"42\"}}")]
    [InlineData("http-request", "{\"routeValues\":{\"id\":\"a/b\"}}")]
    [InlineData("http-request", "{\"headers\":{\"bad name\":[\"value\"]}}")]
    public void Validate_RejectsUnsupportedOrUnsafeRequestConfiguration(string modeId, string json)
    {
        using var data = JsonDocument.Parse(json);

        var result = _driver.Validate(Context("orders/{id}", "post"), modeId, data.RootElement);

        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public void Validate_AcceptsRepeatedQueryAndHeadersForTheAuthoredRoute()
    {
        using var data = JsonDocument.Parse("""
            {
              "routeValues": { "id": "42" },
              "query": { "tag": ["one", "two"] },
              "headers": { "X-Trace": ["one", "two"] },
              "body": "payload"
            }
            """);

        var result = _driver.Validate(Context("orders/{id}", "post"), "http-request", data.RootElement);

        Assert.True(result.IsValid);
    }

    private static TestRunTriggerContext Context(string template, string method)
    {
        const string artifactId = "artifact-1";
        var identity = new WorkflowExecutableIdentity(artifactId, "definition-1", "version-1", "1", "hash-1");
        var node = new ExecutableNode(
            "node-1", "authored-1", HttpEndpoint.ActivityType, "1", "test", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var executable = new WorkflowExecutable(
            identity,
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HttpEndpointRouting.TemplateMetadataKey] = template,
            [HttpEndpointRouting.MethodMetadataKey] = method
        };
        var binding = new WorkflowTriggerBinding(
            "binding-1", artifactId, identity.DefinitionId, identity.ArtifactVersion, identity.ArtifactHash,
            node.ExecutableNodeId, HttpEndpointStimulus.StimulusType, HttpEndpointStimulus.Hash(template, method),
            null, metadata, DateTimeOffset.UtcNow, Cardinality: TriggerCardinality.Exclusive);

        return new TestRunTriggerContext(executable, "Elsa.HttpEndpoint", node, binding);
    }
}
