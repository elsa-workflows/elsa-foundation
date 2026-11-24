using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Fact]
    public async Task Model_owned_preview_and_save_use_declared_defaults_without_completing_optional_members()
    {
        var services = new ServiceCollection(); services.AddSingleton<IWellKnownTypeRegistry>(new PolicyRegistry()); services.AddWorkflowRuntime();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var fixture = new Fixture(mapper: scope.ServiceProvider.GetRequiredService<ITriggerInputMapper>());
        fixture.Discovery.ModelKey = "bpmn";
        fixture.Discovery.InputSemantics = TriggerInputSemantics.ModelOwned;
        fixture.Discovery.InputContract = new(1, [new("optional", new("String"), false, isNullable: true), new("required", new("Int32"), true, Element("7"))]);
        var preview = JsonSerializer.SerializeToElement(await fixture.Management.MappingPreviewAsync("trigger-a", Element("""
            {"expectedRevision":1,"target":{"definitionId":"definition-a","slotName":"default"},"contractRevision":"contract-v1","mappings":[],"sample":{}}
            """), default), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Empty(preview.GetProperty("findings").EnumerateArray());
        var fields = preview.GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("inputName").GetString()!);
        Assert.Equal("Defaulted", fields["required"].GetProperty("state").GetString());
        Assert.Equal(7, fields["required"].GetProperty("value").GetInt32());
        Assert.Equal("Omitted", fields["optional"].GetProperty("state").GetString());
        var command = Element("""
            {"kind":"AddSubscription","target":{"definitionId":"definition-a","slotName":"default"},"mappings":[],"enabled":false,"invocationRequest":"None"}
            """);
        var impact = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        Assert.True(impact.CanApply);
        var applied = await fixture.Management.ApplyAsync("trigger-a", JsonSerializer.SerializeToElement(new
            { requestId = "model-defaults", previewId = impact.PreviewId, expectedRevision = 1, command }), default);
        Assert.Equal("Applied", applied.Disposition);
        Assert.Single(fixture.Store.Resource.Subscriptions); Assert.Equal(1, fixture.Store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_mapping_preview_and_save_use_the_pinned_null_policy(bool enabled)
    {
        var services = new ServiceCollection(); services.AddSingleton<IWellKnownTypeRegistry>(new PolicyRegistry()); services.AddWorkflowRuntime();
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var fixture = new Fixture(mapper: scope.ServiceProvider.GetRequiredService<ITriggerInputMapper>());
        fixture.Discovery.InputContract = new(1, [new("count", new("Int32"), true)]);
        fixture.Discovery.ValuePolicies = new(enabled, false);
        var mappings = Element("""[{"inputName":"count","binding":{"kind":"Literal","value":null}}]""");
        var preview = JsonSerializer.SerializeToElement(await fixture.Management.MappingPreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new
            { expectedRevision = 1, target = new { definitionId = "definition-a", slotName = "default" }, contractRevision = "contract-v1", mappings, sample = new { } }), default), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (enabled)
        {
            Assert.Empty(preview.GetProperty("findings").EnumerateArray());
            Assert.Equal(0, Assert.Single(preview.GetProperty("fields").EnumerateArray()).GetProperty("value").GetInt32());
        }
        else Assert.NotEmpty(preview.GetProperty("findings").EnumerateArray());
        var command = JsonSerializer.SerializeToElement(new { kind = "AddSubscription", target = new { definitionId = "definition-a", slotName = "default" }, mappings, enabled = false, invocationRequest = "None" });
        var impact = await fixture.Management.PreviewAsync("trigger-a", JsonSerializer.SerializeToElement(new { expectedRevision = 1, command }), default);
        Assert.Equal(enabled, impact.CanApply);
        if (!enabled) { Assert.Equal(0, fixture.Store.Writes); return; }
        var applied = await fixture.Management.ApplyAsync("trigger-a", JsonSerializer.SerializeToElement(new
            { requestId = "native-null", previewId = impact.PreviewId, expectedRevision = 1, command }), default);
        Assert.Equal("Applied", applied.Disposition); Assert.Single(fixture.Store.Resource.Subscriptions);
    }

    private sealed class PolicyRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) { alias = type == typeof(int) ? "Int32" : "String"; return type == typeof(int) || type == typeof(string); }
        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => [typeof(int), typeof(string)];
        public string GetAliasOrDefault(Type type) => type == typeof(int) ? "Int32" : "String";
        public Type GetTypeOrDefault(string alias) => alias == "Int32" ? typeof(int) : alias == "String" ? typeof(string) : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) { type = GetTypeOrDefault(alias); return type != typeof(object); }
    }
}
