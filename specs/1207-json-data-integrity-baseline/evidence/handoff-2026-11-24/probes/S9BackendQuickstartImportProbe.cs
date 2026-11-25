using System.Text;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

// Ignored acceptance probe, copied into this test project only for the exact-head T037 run.
public sealed class S9BackendQuickstartImportProbe
{
    [Fact]
    public async Task Native_run_and_real_APIs_preserve_output_and_variable_default_tokens()
    {
        var evidence = Environment.GetEnvironmentVariable("S9_BACKEND_QUICKSTART_ROOT")!;
        Directory.CreateDirectory(evidence);
        await using var host = await Elsa3ImportHost.StartAsync(withConverter: false,
            existingRoot: Directory.CreateDirectory(Path.Join(evidence, "host-store")),
            configureServices: services => services.AddSingleton<IRuntimePayloadCapturePolicy>(new QuickstartPayloadPolicy()));
        const string outputRaw = """{"CustomerId":1,"Name":"Ren\u00e9","N":1.0}""";
        const string defaultRaw = """{ "UpperKey" : "<", "N":1.0, "Name":"Ren\u00e9" }""";
        var quotedAlias = JsonSerializer.Serialize(TypeAliasConvention.AnyAlias, JsonBaseline.Options);
        var requestBody = """
        {"name":"S-9 backend quickstart","state":{
          "variables":[{"referenceKey":"retained","name":"RetainedDefault","type":{"alias":__S9_ANY_ALIAS__,"collectionKind":"Single"},"default":{"expressionType":"Literal","value":__S9_DEFAULT_RAW__}}],
          "inputs":[],"outputs":[],"strategyOptions":null,
          "rootActivity":{"nodeId":"passthrough","activityVersionId":"elsa.intrinsic.set-output@1","outputs":[],"inputs":[
            {"referenceKey":"name","value":{"expressionType":"Literal","value":"OrderTotal"}},
            {"referenceKey":"value","value":{"expressionType":"Literal","value":__S9_OUTPUT_RAW__}}
          ],"intrinsic":{"kind":"setOutput","valueType":{"alias":__S9_ANY_ALIAS__,"collectionKind":"Single"}}}
        }}
        """.Replace("__S9_ANY_ALIAS__", quotedAlias).Replace("__S9_DEFAULT_RAW__", defaultRaw).Replace("__S9_OUTPUT_RAW__", outputRaw);
        await File.WriteAllTextAsync(Path.Join(evidence, "submit-request.json"), requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, "design/workflows/definitions/submit")
        { Content = new StringContent(requestBody, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Elsa3-Import-Permissions", string.Join(' ', host.Permissions));
        using var submittedResponse = await host.Client.SendAsync(request);
        var submittedText = await submittedResponse.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(Path.Join(evidence, "submit-response.json"), submittedText);
        Assert.True(submittedResponse.IsSuccessStatusCode, submittedText);
        using var submitted = JsonDocument.Parse(submittedText);
        var versionId = submitted.RootElement.GetProperty("version").GetProperty("id").GetString()!;
        using var versionResponse = await host.Send(HttpMethod.Get, "design/workflows/versions/" + Uri.EscapeDataString(versionId));
        var versionText = await versionResponse.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(Path.Join(evidence, "version-response.json"), versionText);
        Assert.True(versionResponse.IsSuccessStatusCode, versionText);
        using var version = JsonDocument.Parse(versionText);
        var state = version.RootElement.GetProperty("state");
        Assert.Equal(defaultRaw, state.GetProperty("variables")[0].GetProperty("default").GetProperty("value").GetRawText());
        await File.WriteAllTextAsync(Path.Join(evidence, "saved-state.json"), state.GetRawText());

        var workflow = await host.PublishWorkflowAsync(versionId);
        var run = await host.RunAsync(workflow);
        Assert.True(run.Workflow?.Status == WorkflowExecutionStatus.Completed, run.Describe());
        Assert.Empty(run.Incidents);
        using var response = await host.Send(HttpMethod.Get,
            "runtime/workflows/instances/" + Uri.EscapeDataString(run.Workflow!.WorkflowExecutionId));
        var responseText = await response.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(Path.Join(evidence, "runtime-instance-response.json"), responseText);
        Assert.True(response.IsSuccessStatusCode, responseText);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        using var instance = JsonDocument.Parse(responseText);
        var outputs = instance.RootElement.GetProperty("outputs");
        Assert.False(outputs.TryGetProperty("orderTotal", out _));
        Assert.Equal("Payload", outputs.GetProperty("OrderTotal").GetProperty("captureMode").GetString());
        Assert.Equal(outputRaw, outputs.GetProperty("OrderTotal").GetProperty("value").GetRawText());
        await File.WriteAllTextAsync(Path.Join(evidence, "quickstart-backend-aggregate.json"),
            "{\"nativeRun\":true,\"runtimeApiRawOutput\":true,\"outputKeyCase\":true,\"designApiRawDefault\":true}");
    }

    // A diagnostic snapshot deliberately projects data. This host explicitly requests the
    // existing full-payload mode for this public, non-sensitive workflow output only.
    private sealed class QuickstartPayloadPolicy : IRuntimePayloadCapturePolicy
    {
        public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request) =>
            request.Subject == RuntimePayloadCaptureSubject.WorkflowOutput && !request.IsSensitive
                ? new(RuntimePayloadCaptureMode.Payload, "Public synthetic S-9 full-payload round trip.")
                : new DefaultRuntimePayloadCapturePolicy().Decide(request);
    }
}
