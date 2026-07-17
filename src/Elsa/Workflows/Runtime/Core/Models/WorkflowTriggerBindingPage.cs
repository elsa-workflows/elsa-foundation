using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// One finite exact-stimulus lookup over active workflow trigger bindings.
/// </summary>
public sealed record WorkflowTriggerBindingPageQuery
{
    public const int DefaultLimit = 100;
    public const int MaximumLimit = 500;

    public WorkflowTriggerBindingPageQuery(
        string stimulusType,
        string stimulusHash,
        int limit = DefaultLimit,
        string? continuationToken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        if (limit is <= 0 or > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                $"Trigger-binding page limit must be between 1 and {MaximumLimit}.");
        }

        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Limit = limit;
        ContinuationToken = continuationToken;
        Offset = DecodeOffset(continuationToken);
    }

    public string StimulusType { get; }
    public string StimulusHash { get; }
    public int Limit { get; }
    public string? ContinuationToken { get; }

    /// <summary>
    /// Adapter-facing offset decoded from <see cref="ContinuationToken"/>. Callers should treat the token as opaque.
    /// </summary>
    public int Offset { get; }

    internal string EncodeContinuation(int offset)
    {
        if (offset <= Offset)
            throw new ArgumentOutOfRangeException(nameof(offset), offset, "A continuation must advance the current page.");

        var payload = $"1:{offset}:{QueryBinding()}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private int DecodeOffset(string? continuationToken)
    {
        if (continuationToken is null)
            return 0;
        if (string.IsNullOrWhiteSpace(continuationToken))
            throw new ArgumentException("A trigger-binding continuation token cannot be blank.", nameof(continuationToken));

        try
        {
            var encoded = continuationToken.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(encoded)).Split(':');
            if (parts is not ["1", var offsetText, var queryBinding] ||
                !int.TryParse(offsetText, out var offset) ||
                offset <= 0 ||
                !StringComparer.Ordinal.Equals(queryBinding, QueryBinding()))
            {
                throw new FormatException();
            }

            return offset;
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The trigger-binding continuation token is invalid or belongs to another stimulus query.",
                nameof(continuationToken),
                exception);
        }
    }

    private string QueryBinding()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{StimulusType}\0{StimulusHash}"));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>
/// One bounded, deterministically ordered page of active trigger bindings.
/// </summary>
public sealed record WorkflowTriggerBindingPage
{
    public WorkflowTriggerBindingPage(
        WorkflowTriggerBindingPageQuery query,
        IReadOnlyList<WorkflowTriggerBinding> items,
        long totalCount)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > query.Limit)
            throw new ArgumentException("A trigger-binding page cannot exceed its requested limit.", nameof(items));
        if (totalCount < query.Offset + items.Count)
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "The total count cannot be smaller than the returned window.");
        if (items.Count == 0 && query.Offset < totalCount)
            throw new ArgumentException("A non-terminal trigger-binding page cannot be empty.", nameof(items));

        Items = items;
        TotalCount = totalCount;
        NextContinuationToken = query.Offset + items.Count < totalCount
            ? query.EncodeContinuation(checked(query.Offset + items.Count))
            : null;
    }

    public IReadOnlyList<WorkflowTriggerBinding> Items { get; }
    public long TotalCount { get; }
    public string? NextContinuationToken { get; }
}
