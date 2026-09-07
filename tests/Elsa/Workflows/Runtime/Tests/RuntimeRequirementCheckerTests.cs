using System.Text.Json;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeRequirementCheckerTests
{
    [Fact]
    public void Check_reports_consumer_and_storage_driver_statuses_with_exact_ordering()
    {
        var checker = Checker(
            [new RuntimeActivityConsumerCapability("sample.consumer", ["2", "1"])],
            ["sample.driver"]);
        var subject = new RuntimeRequirementCheckSubject(
            "artifact-1",
            [
                new RuntimeRequirement("sample.consumer", "1"),
                new RuntimeRequirement("sample.consumer", "9"),
                new RuntimeRequirement("sample.missing", "1"),
                new RuntimeRequirement("sample.consumer", "1")
            ],
            [new RuntimeStorageDriverRequirement("sample.driver"), new RuntimeStorageDriverRequirement("sample.missing-driver")],
            []);

        var result = checker.Check(subject);

        Assert.False(result.IsSatisfied);
        Assert.Collection(
            result.Requirements,
            entry => AssertRequirement(entry, "sample.consumer", "1", RuntimeRequirementStatus.Available, ["1", "2"]),
            entry => AssertRequirement(entry, "sample.consumer", "9", RuntimeRequirementStatus.UnsupportedSchema, ["1", "2"]),
            entry => AssertRequirement(entry, "sample.missing", "1", RuntimeRequirementStatus.Missing, []));
        Assert.Collection(
            result.StorageDrivers,
            entry => Assert.Equal(("sample.driver", RuntimeRequirementStatus.Available), (entry.DriverKey, entry.Status)),
            entry => Assert.Equal(("sample.missing-driver", RuntimeRequirementStatus.Missing), (entry.DriverKey, entry.Status)));
        Assert.Empty(result.ActivityTypes);
    }

    [Fact]
    public void Check_reports_registered_missing_and_unreadable_clr_activity_types_by_node()
    {
        const string registeredAlias = "Sample.RegisteredActivity";
        var checker = Checker([], [], (registeredAlias, typeof(object)));
        var subject = new RuntimeRequirementCheckSubject(
            "artifact-2",
            [],
            [],
            [
                Node("node-registered", WellKnownRuntimeActivityConsumers.ClrActivity, AliasPayload(registeredAlias)),
                Node("node-missing", WellKnownRuntimeActivityConsumers.ClrActivity, AliasPayload("Sample.MissingActivity")),
                Node("node-unreadable", WellKnownRuntimeActivityConsumers.ClrActivity, JsonSerializer.SerializeToElement(new[] { "not-a-descriptor" })),
                Node("node-other-consumer", "sample.other-consumer", AliasPayload("Sample.NotChecked"))
            ]);

        var result = checker.Check(subject);

        Assert.False(result.IsSatisfied);
        Assert.Collection(
            result.ActivityTypes,
            entry => AssertActivityType(entry, "Sample.MissingActivity", ["node-missing"], RuntimeRequirementStatus.MissingActivityType),
            entry => AssertActivityType(entry, "Sample.RegisteredActivity", ["node-registered"], RuntimeRequirementStatus.Available),
            entry => AssertActivityType(entry, string.Empty, ["node-unreadable"], RuntimeRequirementStatus.MissingActivityType));
    }

    private static void AssertRequirement(
        RuntimeRequirementStatusEntry entry,
        string consumerKey,
        string schemaVersion,
        RuntimeRequirementStatus status,
        IReadOnlyCollection<string> supportedSchemaVersions)
    {
        Assert.Equal(consumerKey, entry.ConsumerKey);
        Assert.Equal(schemaVersion, entry.SchemaVersion);
        Assert.Equal(status, entry.Status);
        Assert.Equal(supportedSchemaVersions, entry.SupportedSchemaVersions);
    }

    private static void AssertActivityType(
        ActivityTypeStatusEntry entry,
        string typeAlias,
        IReadOnlyCollection<string> nodeIds,
        RuntimeRequirementStatus status)
    {
        Assert.Equal(typeAlias, entry.TypeAlias);
        Assert.Equal(nodeIds, entry.NodeIds);
        Assert.Equal(status, entry.Status);
    }

    private static RuntimeRequirementChecker Checker(
        IEnumerable<IRuntimeActivityConsumerCapability> consumers,
        IEnumerable<string> driverKeys,
        params (string Alias, Type Type)[] registeredTypes)
    {
        var registry = new TestTypeRegistry(registeredTypes);
        var drivers = new RuntimeDurableValueStorageDriverRegistry(driverKeys.Select(key => new StubDriver(key)));
        return new(consumers, drivers, registry, new TestPayloadSerializer());
    }

    private static ExecutableNode Node(string id, string descriptorType, JsonElement payload) =>
        new(
            id,
            id,
            "sample.activity",
            "1",
            descriptorType,
            payload,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, string>());

    private static JsonElement AliasPayload(string alias) =>
        JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias));

    private sealed record StubDriver(string DriverKey) : IRuntimeDurableValueStorageDriver
    {
        public ValueTask<RuntimeDurableValueEncoding> EncodeAsync(object? value, RuntimeValueTypeDescriptor type, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<object?> DecodeAsync(DurableValueState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestTypeRegistry(IEnumerable<(string Alias, Type Type)> entries) : IWellKnownTypeRegistry
    {
        private readonly IReadOnlyDictionary<string, Type> _types = entries.ToDictionary(x => x.Alias, x => x.Type, StringComparer.Ordinal);

        public void RegisterType(Type type, string alias) => throw new NotSupportedException();

        public bool TryGetAlias(Type type, out string alias)
        {
            var entry = _types.FirstOrDefault(x => x.Value == type);
            alias = entry.Key;
            return entry.Value is not null;
        }

        public bool TryGetType(string alias, out Type type) => _types.TryGetValue(alias, out type!);
        public IEnumerable<Type> ListTypes() => _types.Values;
        public string GetAliasOrDefault(Type type) => _types.FirstOrDefault(x => x.Value == type).Key ?? type.FullName!;
        public Type GetTypeOrDefault(string alias) => _types.GetValueOrDefault(alias) ?? typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type) => _types.TryGetValue(alias, out type!);
    }

    private sealed class TestPayloadSerializer : IPayloadSerializer
    {
        private readonly JsonSerializerOptions _options = new();

        public string Serialize(object payload) => JsonSerializer.Serialize(payload, _options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, _options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, _options)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, _options)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(_options)!;
        public T Deserialize<T>(string payload) => JsonSerializer.Deserialize<T>(payload, _options)!;
        public T Deserialize<T>(JsonElement payload) => payload.Deserialize<T>(_options)!;
        public JsonSerializerOptions GetOptions() => _options;
    }
}
