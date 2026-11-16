using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Immutable execution authority and root-initiator attribution.</summary>
public sealed class WorkflowExecutionAuthoritySnapshot
{
    public WorkflowExecutionAuthoritySnapshot(
        string systemIdentity,
        string rootInitiator,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootInitiator);

        SystemIdentity = systemIdentity;
        RootInitiator = rootInitiator;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public string SystemIdentity { get; }
    public string RootInitiator { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    public static WorkflowExecutionAuthoritySnapshot CreateRoot(
        string requestedBy,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(requestedBy, requestedBy, metadata);

    /// <summary>Stable non-reversible key used to constrain persistence queries to one complete execution-authority partition.</summary>
    public static string PartitionKey(
        string systemIdentity,
        string rootInitiator,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootInitiator);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, "workflow-execution-authority/v1");
        AppendString(hash, systemIdentity);
        AppendString(hash, rootInitiator);
        var entries = (metadata ?? new Dictionary<string, string>())
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        AppendInt32(hash, entries.Length);
        foreach (var entry in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Value);
            AppendString(hash, entry.Key);
            AppendString(hash, entry.Value);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Compares every durable authority fact using ordinal metadata semantics.</summary>
    public static bool Matches(
        WorkflowExecutionAuthoritySnapshot authority,
        string systemIdentity,
        string rootInitiator,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        return StringComparer.Ordinal.Equals(authority.SystemIdentity, systemIdentity) &&
               StringComparer.Ordinal.Equals(authority.RootInitiator, rootInitiator) &&
               MetadataEquals(authority.Metadata, metadata);
    }

    /// <summary>Compares authority metadata without depending on dictionary ordering or comparer implementation.</summary>
    public static bool MetadataEquals(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        left ??= new Dictionary<string, string>();
        right ??= new Dictionary<string, string>();
        if (left.Count != right.Count)
            return false;

        using var leftEntries = left.OrderBy(entry => entry.Key, StringComparer.Ordinal).GetEnumerator();
        using var rightEntries = right.OrderBy(entry => entry.Key, StringComparer.Ordinal).GetEnumerator();
        while (leftEntries.MoveNext() && rightEntries.MoveNext())
        {
            if (!StringComparer.Ordinal.Equals(leftEntries.Current.Key, rightEntries.Current.Key) ||
                !StringComparer.Ordinal.Equals(leftEntries.Current.Value, rightEntries.Current.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static void AppendString(IncrementalHash hash, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = Encoding.Unicode.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        hash.AppendData(bytes);
    }
}
