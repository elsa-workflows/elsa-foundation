using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
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

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
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

    public static WorkflowDefinitionDraft WorkflowDraft(string scope = ScopeA) => new()
    {
        Id = WorkflowDraftId,
        TenantId = scope,
        WorkflowDefinitionId = WorkflowDefinitionId,
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

    public static ActivityDefinitionVersion ActivityVersion(string scope = ScopeA) => new("1.0.0", ActivityDefinitionId)
    {
        Id = ActivityVersionId,
        TenantId = scope,
        ProviderKey = "elsa.http",
        ProviderSchemaVersion = "1",
        ConsumerKey = "elsa.workflow",
        ConsumerSchemaVersion = "1",
        SourceKind = "fixture",
        SourceId = "http-request",
        Hash = "fixture-hash",
        CreatedAt = Epoch,
        LastModifiedAt = Epoch
    };

    /// <summary>Computes an invariant result hash for parity assertions and evidence records.</summary>
    public static string ResultHash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
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
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, JsonOptions);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, JsonOptions);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<object>(serializedData, JsonOptions)!;
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type, JsonOptions)!;
        public object Deserialize(JsonElement serializedData) => serializedData.Deserialize<object>(JsonOptions)!;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData, JsonOptions)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(JsonOptions)!;
        public JsonSerializerOptions GetOptions() => JsonOptions;
    }
}
