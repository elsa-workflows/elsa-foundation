using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Server-provisioned resource recurrence identity, independent of every workflow publication.</summary>
public sealed record RecurringTriggerOwner
{
    [JsonConstructor]
    public RecurringTriggerOwner(string triggerId, long generation, long configurationRevision,
        string sourceTypeKey, string sourceFamily, PersistenceScope scope, string? tenantId, string timeZoneId = "UTC")
    {
        foreach (var text in new[] { triggerId, sourceTypeKey, sourceFamily, timeZoneId }) ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configurationRevision);
        ArgumentNullException.ThrowIfNull(scope);
        if (tenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        TriggerId = triggerId; Generation = generation; ConfigurationRevision = configurationRevision;
        SourceTypeKey = sourceTypeKey; SourceFamily = sourceFamily; Scope = scope; TenantId = tenantId; TimeZoneId = timeZoneId;
    }
    public string TriggerId { get; }
    public long Generation { get; }
    public long ConfigurationRevision { get; }
    public string SourceTypeKey { get; }
    public string SourceFamily { get; }
    public PersistenceScope Scope { get; }
    public string? TenantId { get; }
    public string TimeZoneId { get; }
}
