using Elsa.Primitives.Models;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Mapping;
using Elsa3.Activities.Design.Import.Integration.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa3.Activities.Design.Import.Integration.Tests;

/// <summary>Spec1232 SC004: independently authored native and Elsa3 documents through real import/publication/runtime.</summary>
public sealed class NullPolicyNativeConversionParityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Equal_pinned_policies_produce_equal_values_refusals_and_diagnostics(bool enabled)
    {
        await using var host = await Elsa3ImportHost.StartAsync(configureServices: services =>
            services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
        var typeKey = typeof(RunJavaScript).FullName!;
        var catalog = await host.GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(typeKey)}");
        var entry = Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == typeKey);
        var scriptVersion = Elsa3ImportHost.Required(entry, "definition", "headVersionId");
        var cases = new[]
        {
            new Case("nullable", "String", "return null;", "null", "null"),
            new Case("scalar", "Int32", "return null;", null, "0", "VF-ACT-004", ["VF-NULL-001"]),
            new Case("undefined", "Any", "return undefined;", null, "null", "VF-UNDEF-001"),
            new Case("partial", "Any", "if (false) return 1; return undefined;", null, "null", "VF-UNDEF-001"),
            new Case("deep", "Any", "return {customer:{discount:undefined}};", "{\"customer\":{}}", "{\"customer\":{\"discount\":null}}", OnWarnings: [], OffWarnings: ["VF-UNDEF-002"]),
            new Case("sparse", "Any", "return [1,,undefined];", "[1,null,null]", "[1,null,null]", OnWarnings: ["VF-UNDEF-003"], OffWarnings: ["VF-UNDEF-003"]),
            new Case("nan", "Any", "return NaN;", "null", "null", OnWarnings: ["VF-NUM-001"], OffWarnings: ["VF-NUM-001"]),
            new Case("infinity", "Any", "return Infinity;", "null", "null", OnWarnings: ["VF-NUM-001"], OffWarnings: ["VF-NUM-001"]),
            new Case("negative-infinity", "Any", "return -Infinity;", "null", "null", OnWarnings: ["VF-NUM-001"], OffWarnings: ["VF-NUM-001"]),
            new Case("stringify", "Any", "return JSON.stringify({discount:undefined,n:NaN});", "\"{\\\"n\\\":null}\"", "\"{\\\"n\\\":null}\""),
            new Case("undefined-write", "String", "variables.captured = undefined; return 1;", null, "null", "VF-UNDEF-001", Setter: true),
            new Case("null-write", "Int32", "variables.captured = null; return 1;", null, "0", "VF-ACT-004", ["VF-NULL-001"], Setter: true)
        };
        foreach (var item in cases)
        {
            output.WriteLine($"BEGIN {item.Name} policies={enabled}");
            var native = await Native(host, scriptVersion, item, enabled);
            var imported = await Imported(host, item, enabled);
            Assert.Equal(native.Workflow!.Status, imported.Workflow!.Status);
            var expected = enabled ? item.On : item.Off;
            if (expected is null)
            {
                Assert.Equal(WorkflowExecutionStatus.Faulted, native.Workflow.Status);
                foreach (var run in new[] { native, imported })
                    Assert.Contains(run.Activities, state => state.Fault?.Message.Contains(item.Refusal!, StringComparison.Ordinal) == true);
            }
            else
            {
                Assert.Equal(WorkflowExecutionStatus.Completed, native.Workflow.Status);
                foreach (var run in new[] { native, imported })
                {
                    Assert.Empty(run.Incidents);
                    var value = run.Workflow!.RootVariableFrame!.Values["captured"];
                    using var oracle = JsonDocument.Parse(expected);
                    Assert.Equal(oracle.RootElement.ValueKind == JsonValueKind.Null ? ValuePresence.ExplicitNull : ValuePresence.Present, value.Presence);
                    if (value.Presence == ValuePresence.Present) Assert.True(JsonElement.DeepEquals(oracle.RootElement, value.InlineValue!.Value), run.Describe());
                    var codes = run.Activities.SelectMany(state => state.Warnings ?? []).Select(warning => warning.Code).Distinct().Order(StringComparer.Ordinal);
                    Assert.Equal(((enabled ? item.OnWarnings : item.OffWarnings) ?? []).Distinct().Order(StringComparer.Ordinal), codes);
                }
            }
            Assert.Equal(Diagnostics(native), Diagnostics(imported));
            output.WriteLine($"PASS {item.Name}: {native.Workflow.Status}; diagnostics={string.Join(',', Diagnostics(native))}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_CLR_non_nullable_String_has_no_fabricated_default_in_native_or_converted_execution(bool enabled)
    {
        await using var host = await Elsa3ImportHost.StartAsync(configureServices: services =>
            services.PostConfigure<Elsa3MappingOptions>(options => options.ApplyElsa3NullPolicies = enabled));
        var key = typeof(Elsa.Activities.Primitives.Activities.PublishEvent).FullName!;
        var catalog = await host.GetJsonAsync($"design/activities/definitions?search={Uri.EscapeDataString(key)}");
        var activity = Assert.Single(catalog["items"]!.AsArray(), item => (string?)item?["definition"]?["activityTypeKey"] == key);
        var expression = new ExpressionDefinition("JavaScript", "null", new TypeReference("String"),
            new Dictionary<string, ExpressionParameterBinding>(), JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingReadonlyV1);
        var submitted = await host.PostAsync("design/workflows/definitions/submit", new JsonObject
        {
            ["name"] = "native non-nullable String", ["state"] = new JsonObject
            {
                ["variables"] = new JsonArray(), ["inputs"] = new JsonArray(), ["outputs"] = new JsonArray(),
                ["strategyOptions"] = new JsonObject { ["nullHandling"] = new JsonObject { ["defaultOnNull"] = enabled, ["undefinedAsNull"] = enabled } },
                ["rootActivity"] = new JsonObject { ["nodeId"] = "subject", ["activityVersionId"] = Elsa3ImportHost.Required(activity, "definition", "headVersionId"),
                    ["inputs"] = new JsonArray(new JsonObject { ["referenceKey"] = "EventName", ["value"] = new JsonObject { ["expressionType"] = "JavaScript",
                        ["value"] = JsonSerializer.SerializeToNode(expression, new JsonSerializerOptions(JsonSerializerDefaults.Web)) } }), ["outputs"] = new JsonArray() }
            }
        });
        var native = await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(submitted, "version", "id")));
        var source = JsonNode.Parse(Elsa3ImportHost.Collection("null-members-script"))!.AsArray()[0]!.DeepClone();
        source["id"] = "s2-string-null-v1"; source["definitionId"] = "s2-string-null";
        source["root"] = new JsonObject { ["id"] = "subject", ["nodeId"] = "subject", ["type"] = "Elsa.PublishEvent", ["version"] = 1,
            ["eventName"] = new JsonObject { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "JavaScript", ["value"] = "null" } } };
        var handle = await host.UploadCollectionJsonAsync(new JsonArray(source).ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle); var selected = Assert.Single(items);
        var applied = await host.ApplyAsync(handle, plan, Guid.NewGuid().ToString("N"), Elsa3ImportHost.Required(selected, "sourceVersionId"));
        Assert.True(applied.StatusCode == HttpStatusCode.Created, applied.Body.ToJsonString());
        var converted = await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(selected, "workflowVersionId")));
        foreach (var run in new[] { native, converted })
        {
            // Materialization is refused before Started. The incident remains available for operator recovery.
            Assert.NotEqual(WorkflowExecutionStatus.Completed, run.Workflow!.Status);
            var refused = Assert.Single(run.Activities, state => state.Execution.AuthoredActivityId == "subject");
            Assert.Null(refused.StartedAt); Assert.Null(refused.Completion); Assert.Null(refused.InputSnapshot);
            var incident = Assert.Single(run.Incidents, incident => incident.Message.Contains("VF-ACT-004", StringComparison.Ordinal));
            Assert.Contains("EventName", incident.Message);
            Assert.DoesNotContain("non-blank EventName", incident.Message, StringComparison.Ordinal);
        }
        Assert.Equal(Diagnostics(native), Diagnostics(converted));
    }

    private static string[] Diagnostics(RunState run) => run.Activities.SelectMany(state => state.Warnings ?? [])
        .Select(warning => $"{warning.Code}|{warning.Severity}|{string.Join(';', (warning.Details ?? new Dictionary<string,string>()).OrderBy(pair => pair.Key).Select(pair => pair.Key + '=' + pair.Value))}")
        .Order(StringComparer.Ordinal).ToArray();

    private static async Task<RunState> Native(Elsa3ImportHost host, string scriptVersion, Case item, bool enabled)
    {
        var state = JsonNode.Parse("""{"variables":[{"referenceKey":"captured","name":"captured","type":{"alias":"Any","collectionKind":"Single"}}],"inputs":[],"outputs":[],"rootActivity":{"nodeId":"script","inputs":[],"outputs":[]},"strategyOptions":{"nullHandling":{}}}""")!;
        state["variables"]![0]!["type"]!["alias"] = item.Alias == "Any" ? TypeAliasConvention.AnyAlias : item.Alias;
        state["rootActivity"]!["activityVersionId"] = scriptVersion;
        state["rootActivity"]!["inputs"] = new JsonArray(new JsonObject { ["referenceKey"] = "script", ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = item.Body } });
        if (!item.Setter) state["rootActivity"]!["outputs"] = JsonNode.Parse("""[{"referenceKey":"result","value":{"expressionType":"Variable","value":{"referenceKey":"captured","declaringScopeId":"workflow"}}}]""");
        state["strategyOptions"]!["nullHandling"]!["defaultOnNull"] = enabled;
        state["strategyOptions"]!["nullHandling"]!["undefinedAsNull"] = enabled;
        var submitted = await host.PostAsync("design/workflows/definitions/submit", new JsonObject { ["name"] = "native parity " + item.Name, ["state"] = state });
        return await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(submitted, "version", "id")));
    }

    private static async Task<RunState> Imported(Elsa3ImportHost host, Case item, bool enabled)
    {
        var source = JsonNode.Parse(Elsa3ImportHost.Collection("null-members-script"))!.AsArray()[0]!.DeepClone();
        var id = "s2-parity-" + item.Name + '-' + enabled;
        source["id"] = id + "-v1"; source["definitionId"] = id; source["name"] = id;
        source["variables"] = new JsonArray(new JsonObject { ["id"] = "captured", ["name"] = "captured", ["typeName"] = item.Alias == "Any" ? "Object" : item.Alias });
        var script = new JsonObject { ["id"] = "script", ["nodeId"] = "script", ["type"] = "Elsa.RunJavaScript", ["version"] = 1,
            ["script"] = new JsonObject { ["typeName"] = "String", ["expression"] = new JsonObject { ["type"] = "Literal", ["value"] = item.Setter ? item.Body.Replace("variables.captured = undefined", "setVariable('captured', undefined)", StringComparison.Ordinal).Replace("variables.captured = null", "setVariable('captured', null)", StringComparison.Ordinal) : item.Body } } };
        if (!item.Setter) script["result"] = JsonNode.Parse("""{"typeName":"Object","memoryReference":{"id":"captured"}}""");
        source["root"] = script;
        var handle = await host.UploadCollectionJsonAsync(new JsonArray(source).ToJsonString());
        var (plan, items) = await host.AnalyzeAsync(handle);
        var selected = Assert.Single(items);
        var apply = await host.ApplyAsync(handle, plan, Guid.NewGuid().ToString("N"), Elsa3ImportHost.Required(selected, "sourceVersionId"));
        Assert.True(apply.StatusCode == HttpStatusCode.Created, apply.Body.ToJsonString());
        return await host.RunAsync(await host.PublishWorkflowAsync(Elsa3ImportHost.Required(selected, "workflowVersionId")));
    }

    private sealed record Case(string Name, string Alias, string Body, string? Off, string On, string? Refusal = null,
        string[]? OnWarnings = null, string[]? OffWarnings = null, bool Setter = false);
}
