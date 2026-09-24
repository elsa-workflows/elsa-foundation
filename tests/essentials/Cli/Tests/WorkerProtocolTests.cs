using System.Reflection;
using System.Text;
using System.Text.Json;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class WorkerProtocolTests
{
    [Theory]
    [InlineData("{\"version\":2,\"command\":\"list\",\"future\":true}")]
    [InlineData("{\"version\":2,\"command\":\"list\",\"command\":\"plan\"}")]
    [InlineData("{\"version\":2,\"command\":\"list\",\"selection\":{\"kind\":\"all\",\"kind\":\"modules\"}}")]
    [InlineData("{\"command\":\"list\"}")]
    public async Task Private_request_refuses_unknown_and_duplicate_fields(string json)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAsync<JsonException>(() => WorkerContract.ReadRequestAsync(input, CancellationToken.None));
    }

    [Fact]
    public void Private_version_two_transports_only_context_metadata()
    {
        Assert.Equal(2, WorkerContract.Version);
        var request = new WorkerRequest
        {
            Command = WorkerCommands.List,
            ContextVersion = 1,
            ContextSource = WorkerContextSources.WorkbenchJson,
            Resource = "primary",
            Shell = "default"
        };

        var json = JsonSerializer.Serialize(request, WorkerContract.Json);

        Assert.Contains("\"contextSource\":\"workbench-json-v1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"resource\":\"primary\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("connectionString", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Context_request_rejects_a_legacy_feature_projection_before_host_access()
    {
        var response = await WorkerRunner.RunAsync(new WorkerRequest
        {
            Command = WorkerCommands.List,
            HostDirectory = "/not-a-host",
            HostName = "NotAHost",
            DepsFile = "/not-a-host/NotAHost.deps.json",
            Environment = "Production",
            ContextVersion = 1,
            ContextSource = WorkerContextSources.WorkbenchJson,
            Shell = "default",
            Shells = []
        }, CancellationToken.None);

        Assert.Equal(ToolExitCode.Refusal, response.ExitCode);
        Assert.Equal("invalid-request", response.Error?.Code);
    }

    [Theory]
    [InlineData("{\"version\":1,\"exitCode\":0,\"tooling\":{}}", 0)]
    [InlineData("{\"exitCode\":0,\"tooling\":{}}", 0)]
    [InlineData("{\"version\":2,\"tooling\":{}}", 0)]
    [InlineData("{\"version\":2,\"exitCode\":0,\"tooling\":{},\"tooling\":{}}", 0)]
    [InlineData("{\"version\":2,\"exitCode\":0,\"error\":{\"code\":\"x\",\"message\":\"y\"},\"tooling\":{}}", 0)]
    [InlineData("secret-canary-not-json", 3)]
    public void Front_end_refuses_invalid_worker_responses_without_echoing_them(string response, int exitCode)
    {
        var parse = typeof(WorkerProcess).GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static)!;
        var invocation = Assert.Throws<TargetInvocationException>(() => parse.Invoke(null, [response, exitCode]));
        var refusal = Assert.IsType<CliRefusal>(invocation.InnerException);

        Assert.Equal("worker-response-invalid", refusal.Code);
        Assert.DoesNotContain("secret-canary", refusal.ToString(), StringComparison.Ordinal);
    }
}
