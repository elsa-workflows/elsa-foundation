using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Serialization.Core;
using Xunit;

namespace Elsa.Activities.Design.Api.Tests;

public sealed class ActivityAuthoringCatalogTriggerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_DI_construction_works_with_and_without_a_type_registry(bool withRegistry)
    {
        var (definition, version) = Fixture("""{"typeAlias":"acme.start"}""");
        var hash = new DefaultActivityDefinitionHasher().Hash(definition, version);
        version.Hash = hash;
        var view = await CatalogReaderFixture.ListAsync([definition], [version],
            wellKnownTypes: withRegistry ? new Registry(typeof(AnnotatedActivity)) : null, resolveFromServices: true);
        var descriptor = Assert.Single(view.Activities, item => item.ActivityVersionId == version.Id);
        Assert.Equal(withRegistry ? "Trigger" : "Action", descriptor.ExecutionType);
        Assert.Equal("version", version.Id);
        Assert.Equal("1.0.0", version.Version);
        Assert.Equal(ActivityExecutionType.Action, version.ExecutionType);
        Assert.Equal(hash, version.Hash);
        Assert.Equal(hash, new DefaultActivityDefinitionHasher().Hash(definition, version));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{\"typeAlias\":42}")]
    [InlineData("{\"typeAlias\":null}")]
    [InlineData("{\"typeAlias\":\"\"}")]
    [InlineData("{\"typeAlias\":\"missing.type\"}")]
    public async Task Missing_malformed_and_unresolved_descriptors_preserve_the_stored_projection(string payload)
    {
        var (definition, version) = Fixture(payload);
        var view = await CatalogReaderFixture.ListAsync([definition], [version], wellKnownTypes: new Registry(typeof(AnnotatedActivity)));
        Assert.Equal("Action", Assert.Single(view.Activities, item => item.ActivityVersionId == version.Id).ExecutionType);
    }

    [Fact]
    public async Task Default_undefined_payload_preserves_existing_catalog_reads()
    {
        var (definition, version) = Fixture("{}");
        version.DescriptorPayload = default;
        var view = await CatalogReaderFixture.ListAsync([definition], [version], wellKnownTypes: new Registry(typeof(AnnotatedActivity)));
        Assert.Equal("Action", Assert.Single(view.Activities, item => item.ActivityVersionId == version.Id).ExecutionType);
    }

    [Theory]
    [InlineData("acme.start", "elsa.graph-activity", "Action")]
    [InlineData("acme.action", "elsa.clr-activity", "Action")]
    [InlineData("acme.object", "elsa.clr-activity", "Action")]
    [InlineData("acme.start", "elsa.clr-activity", "Trigger")]
    public async Task Uses_the_registered_descriptor_alias_and_trigger_annotation_only(string alias, string consumer, string expected)
    {
        var (definition, version) = Fixture(JsonSerializer.Serialize(new { TYPEALIAS = alias }));
        // A misleading definition key must not override the descriptor or consumer boundary.
        version.ConsumerKey = consumer;
        var view = await CatalogReaderFixture.ListAsync([definition], [version], wellKnownTypes: new Registry(typeof(AnnotatedActivity)));
        Assert.Equal(expected, Assert.Single(view.Activities, item => item.ActivityVersionId == version.Id).ExecutionType);
    }

    [Fact]
    public async Task Registry_failures_are_not_hidden_as_missing_metadata()
    {
        var (definition, version) = Fixture("""{"typeAlias":"acme.start"}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogReaderFixture.ListAsync([definition], [version],
            wellKnownTypes: new Registry(typeof(AnnotatedActivity), throwOnLookup: true)));
    }

    private static (ActivityDefinition, ActivityDefinitionVersion) Fixture(string json)
    {
        var definition = new ActivityDefinition { Id = "definition", ActivityTypeKey = "Unregistered.Timer.NamedTrigger", DisplayName = "Renamed activity" };
        var version = new ActivityDefinitionVersion("1.0.0", definition.Id) { Id = "version", ConsumerKey = "elsa.clr-activity",
            DescriptorPayload = JsonSerializer.Deserialize<JsonElement>(json) };
        return (definition, version);
    }

    [TriggerActivity]
    private sealed class AnnotatedActivity;
    private sealed class OrdinaryActivity;

    private sealed class Registry(Type trigger, bool throwOnLookup = false) : IWellKnownTypeRegistry
    {
        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            if (throwOnLookup) throw new InvalidOperationException("Registry failure");
            type = alias switch { "acme.start" => trigger, "acme.action" => typeof(OrdinaryActivity), _ => typeof(object) };
            return alias is "acme.start" or "acme.action" or "acme.object";
        }
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias) => throw new NotSupportedException();
        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => throw new NotSupportedException();
        public string GetAliasOrDefault(Type type) => throw new NotSupportedException();
        public Type GetTypeOrDefault(string alias) => TryGetTypeOrDefault(alias, out var type) ? type : typeof(object);
    }
}
