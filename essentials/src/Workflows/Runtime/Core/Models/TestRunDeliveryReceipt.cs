using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Bounded retry identity committed in the same checkpoint as exact test bookmark consumption.</summary>
public sealed record TestRunDeliveryReceipt
{
    public const string MetadataKey = "runtime.testDriveReceipt";
    public const string ReceiptPrefix = "runtime.testDrive.delivery.";
    public const int MaximumReceipts = 256;

    public TestRunDeliveryReceipt(string testRunId, string deliveryId, string waitId, string configurationDigest, string? stimulusDigest = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testRunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(waitId);
        if (!Guid.TryParseExact(deliveryId, "D", out _))
            throw new ArgumentException("Test delivery ID must be a UUID.", nameof(deliveryId));
        ArgumentNullException.ThrowIfNull(configurationDigest);
        if (configurationDigest.Length != 64 || configurationDigest.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Test delivery digest must be SHA-256 hex.", nameof(configurationDigest));
        TestRunId = testRunId;
        DeliveryId = Guid.Parse(deliveryId).ToString("D");
        WaitId = waitId;
        ConfigurationDigest = configurationDigest.ToLowerInvariant();
        if (stimulusDigest is not null && (stimulusDigest.Length != 64 || stimulusDigest.Any(character => !Uri.IsHexDigit(character))))
            throw new ArgumentException("Test stimulus digest must be SHA-256 hex.", nameof(stimulusDigest));
        StimulusDigest = stimulusDigest?.ToLowerInvariant();
    }

    public string TestRunId { get; }
    public string DeliveryId { get; }
    public string WaitId { get; }
    public string ConfigurationDigest { get; }
    public string? StimulusDigest { get; }
    public string StorageKey => ReceiptPrefix + DeliveryId;
    public string StorageValue => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{WaitId.Length}:{WaitId}:{ConfigurationDigest}")));

    public static string Digest(JsonElement? payload) =>
        Convert.ToHexStringLower(SHA256.HashData(payload is { } value ? JsonSerializer.SerializeToUtf8Bytes(value) : "null"u8.ToArray()));

    public bool? MatchesExisting(WorkflowExecutionState execution) =>
        execution.SystemMetadata.TryGetValue(StorageKey, out var existing) ? StringComparer.Ordinal.Equals(existing, StorageValue) : null;

    public WorkflowExecutionState Record(WorkflowExecutionState execution)
    {
        if (execution.RunKind != WorkflowRunKind.TestRun || execution.TestScope?.ScopeId != TestRunId)
            throw new InvalidOperationException("Test delivery authority does not match this execution.");
        if (MatchesExisting(execution) is false)
            throw new InvalidOperationException("This test delivery ID already names different data or another wait.");
        if (MatchesExisting(execution) is true)
            return execution;
        if (execution.SystemMetadata.Keys.Count(key => key.StartsWith(ReceiptPrefix, StringComparison.Ordinal)) >= MaximumReceipts)
            throw new InvalidOperationException("This test run reached its manual event delivery limit. Start a new test run.");
        var metadata = execution.SystemMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata[StorageKey] = StorageValue;
        return execution with { SystemMetadata = RuntimeModelMetadata.Snapshot(metadata) };
    }
}
