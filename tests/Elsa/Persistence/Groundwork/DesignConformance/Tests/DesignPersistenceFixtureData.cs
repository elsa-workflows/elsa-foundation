using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Persistence.Groundwork.DesignConformance.Tests;

/// <summary>
/// Fixed inputs shared by the EF oracle and every provider fixture. Values are deliberately
/// provider-neutral: no physical table names, connection strings, or provider SDK types belong here.
/// </summary>
public static class DesignPersistenceFixtureData
{
    public static readonly DateTimeOffset Epoch = new(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
    public const string ScopeA = "design-scope-a";
    public const string ScopeB = "design-scope-b";
    public const string WorkflowDefinitionId = "workflow-order-processing";
    public const string WorkflowVersionId = "workflow-order-processing-v1";
    public const string WorkflowDraftId = "workflow-order-processing-draft";
    public const string WorkflowVersionLayoutId = "workflow-order-processing-v1-layout";
    public const string ActivityDefinitionId = "activity-http-request";
    public const string ActivityVersionId = "activity-http-request-v1";
    public const string ReconciledActivityDefinitionId = "activity-reconciled";
    public const string ReconciledActivityVersionId = "activity-reconciled-v1";

    private static readonly JsonSerializerOptions SerializationOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static WorkflowDefinition WorkflowDefinition(string scope = ScopeA) => new()
    {
        Id = WorkflowDefinitionId,
        TenantId = scope,
        Name = "Order processing",
        Description = "Creates and tracks an order.",
        CreatedAt = Epoch,
        LastModifiedAt = Epoch
    };

    public static WorkflowDefinitionVersion WorkflowVersion(string scope = ScopeA) => new(WorkflowDefinitionId, "1.0.0")
    {
        Id = WorkflowVersionId,
        TenantId = scope,
        CreatedAt = Epoch,
        LastModifiedAt = Epoch,
        SourceCreatedAt = Epoch
    };

    public static WorkflowDefinitionState WorkflowState(string rootNodeId = "root") => new(
        Variables: [],
        RootActivity: new ActivityNode(rootNodeId, ActivityVersionId, [], []),
        Inputs: [],
        Outputs: [],
        StrategyOptions: null);

    public static IReadOnlyCollection<DesignMetadataRecord> WorkflowDraftLayout() =>
    [new("root", 10, 20, 180, 64)];

    public static WorkflowDefinitionDraft WorkflowDraft(string scope = ScopeA, WorkflowDefinitionState? state = null) => new()
    {
        Id = WorkflowDraftId,
        TenantId = scope,
        WorkflowDefinitionId = WorkflowDefinitionId,
        State = state ?? WorkflowDefinitionState.Empty,
        CreatedAt = Epoch,
        LastModifiedAt = Epoch
    };

    public static WorkflowDefinitionVersionLayout WorkflowVersionLayout(string scope = ScopeA) => new()
    {
        Id = WorkflowVersionLayoutId,
        TenantId = scope,
        WorkflowDefinitionVersionId = WorkflowVersionId
    };

    public static ActivityDefinition ActivityDefinition(string scope = ScopeA) => new()
    {
        Id = ActivityDefinitionId,
        TenantId = scope,
        ActivityTypeKey = "Elsa.Http.SendRequest",
        Category = "HTTP",
        DisplayName = "Send HTTP request",
        Description = "Sends a deterministic test request.",
        CreatedAt = Epoch,
        LastModifiedAt = Epoch
    };

    public static ActivityDefinitionVersion ActivityVersion(string version = "1.0.0", string id = ActivityVersionId, string scope = ScopeA) => new(version, ActivityDefinitionId)
    {
        Id = id,
        TenantId = scope,
        ProviderKey = "elsa.http",
        ProviderSchemaVersion = "1",
        ConsumerKey = "elsa.workflow",
        ConsumerSchemaVersion = "1",
        DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "http-request", method = "GET" }, SerializationOptions),
        SourceKind = "fixture",
        SourceId = "http-request",
        Hash = "fixture-hash",
        CreatedAt = Epoch,
        LastModifiedAt = Epoch
    };

    public static ActivityDefinitionVersion ReconciledActivityVersion(string scope = ScopeA)
    {
        var definition = new ActivityDefinition
        {
            Id = ReconciledActivityDefinitionId,
            TenantId = scope,
            ActivityTypeKey = "Elsa.Http.ReconciledRequest",
            Category = "HTTP",
            DisplayName = "Reconciled HTTP request",
            Description = "A deterministic reconciliation candidate.",
            CreatedAt = Epoch,
            LastModifiedAt = Epoch
        };

        return new ActivityDefinitionVersion("1.0.0", definition.Id)
        {
            Id = ReconciledActivityVersionId,
            TenantId = scope,
            Definition = definition,
            ProviderKey = "elsa.http",
            ProviderSchemaVersion = "1",
            ConsumerKey = "elsa.workflow",
            ConsumerSchemaVersion = "1",
            DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "reconciled-http-request", method = "GET" }, SerializationOptions),
            SourceKind = "fixture",
            SourceId = "reconciled-http-request",
            Hash = "fixture-reconciled-hash",
            CreatedAt = Epoch,
            LastModifiedAt = Epoch
        };
    }

    /// <summary>Computes an invariant result hash for parity assertions and evidence records.</summary>
    public static string ResultHash<T>(T value)
    {
        var document = JsonSerializer.SerializeToElement(value, SerializationOptions);
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonicalJson(document, writer);
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteCanonicalJson(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(property.Value, writer);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                    WriteCanonicalJson(item, writer);

                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    public sealed class FixedSystemClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    public sealed class DeterministicIdentityGenerator(IEnumerable<string> values) : IIdentityGenerator
    {
        private readonly Queue<string> values = new(values ?? throw new ArgumentNullException(nameof(values)));

        public string Generate() => values.Count > 0
            ? values.Dequeue()
            : throw new InvalidOperationException("The deterministic fixture identity sequence is exhausted.");
    }

    public sealed class DeterministicPayloadSerializer : IPayloadSerializer
    {
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, SerializationOptions);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, SerializationOptions);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, SerializationOptions)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, SerializationOptions)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(SerializationOptions)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, SerializationOptions)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(SerializationOptions)!;
        public JsonSerializerOptions GetOptions() => new(SerializationOptions);
    }
}
