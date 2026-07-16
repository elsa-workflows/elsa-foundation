using System.Globalization;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Constants;

/// <summary>Allow-listed diagnostic fields for waited child terminal results.</summary>
public static class DispatchWorkflowDiagnostics
{
    public const string CodeKey = "code";
    public const string CategoryKey = "category";
    public const string SummaryKey = "summary";
    public const string IncidentCountKey = "incidentCount";
    public const string IncidentIdsTruncatedKey = "incidentIdsTruncated";
    public const string IncidentIdPrefix = "incidentId.";
    public const string Category = "execution";
    public const string FaultedCode = "child-workflow-faulted";
    public const string CancelledCode = "child-workflow-cancelled";
    public const string FaultedSummary = "The child workflow faulted.";
    public const string CancelledSummary = "The child workflow was cancelled.";
    public const int MaximumIncidentIds = 32;

    internal static IReadOnlyDictionary<string, string> Faulted(IReadOnlyCollection<string> incidentIds)
    {
        ArgumentNullException.ThrowIfNull(incidentIds);
        var sortedIds = incidentIds
            .Select(ValidateIncidentId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var metadata = Fixed(FaultedCode, FaultedSummary);
        metadata[IncidentCountKey] = sortedIds.Length.ToString(CultureInfo.InvariantCulture);
        metadata[IncidentIdsTruncatedKey] = sortedIds.Length > MaximumIncidentIds ? "true" : "false";
        for (var index = 0; index < Math.Min(sortedIds.Length, MaximumIncidentIds); index++)
            metadata[IncidentKey(index)] = sortedIds[index];
        return metadata;
    }

    internal static IReadOnlyDictionary<string, string> Cancelled() =>
        Fixed(CancelledCode, CancelledSummary);

    internal static void ValidateFaulted(IReadOnlyDictionary<string, string> metadata)
    {
        ValidateFixed(metadata, FaultedCode, FaultedSummary, allowIncidentIds: true);
        if (!metadata.TryGetValue(IncidentCountKey, out var incidentCountValue) ||
            !int.TryParse(incidentCountValue, NumberStyles.None, CultureInfo.InvariantCulture, out var incidentCount) ||
            incidentCount < 0 ||
            !metadata.TryGetValue(IncidentIdsTruncatedKey, out var truncatedValue) ||
            truncatedValue is not ("true" or "false"))
        {
            throw new ArgumentException("Fault diagnostics require invariant incident count and truncation fields.", nameof(metadata));
        }

        var incidentEntries = metadata
            .Where(item => item.Key.StartsWith(IncidentIdPrefix, StringComparison.Ordinal))
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToArray();
        var expectedEntryCount = Math.Min(incidentCount, MaximumIncidentIds);
        var expectedTruncated = incidentCount > MaximumIncidentIds ? "true" : "false";
        if (incidentEntries.Length != expectedEntryCount || !StringComparer.Ordinal.Equals(truncatedValue, expectedTruncated))
            throw new ArgumentException("Fault diagnostic incident count or truncation fields conflict with the indexed identifiers.", nameof(metadata));

        var incidentIds = new string[incidentEntries.Length];
        for (var index = 0; index < incidentEntries.Length; index++)
        {
            if (!StringComparer.Ordinal.Equals(incidentEntries[index].Key, IncidentKey(index)))
                throw new ArgumentException("Fault diagnostic incident identifiers must use contiguous canonical indexes.", nameof(metadata));
            incidentIds[index] = ValidateIncidentId(incidentEntries[index].Value);
        }

        if (!incidentIds.SequenceEqual(incidentIds.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
            incidentIds.Distinct(StringComparer.Ordinal).Count() != incidentIds.Length)
        {
            throw new ArgumentException("Fault diagnostic incident identifiers must be unique and ordinally sorted.", nameof(metadata));
        }
    }

    internal static void ValidateCancelled(IReadOnlyDictionary<string, string> metadata) =>
        ValidateFixed(metadata, CancelledCode, CancelledSummary, allowIncidentIds: false);

    private static Dictionary<string, string> Fixed(string code, string summary) =>
        new(StringComparer.Ordinal)
        {
            [CodeKey] = code,
            [CategoryKey] = Category,
            [SummaryKey] = summary
        };

    private static void ValidateFixed(
        IReadOnlyDictionary<string, string> metadata,
        string code,
        string summary,
        bool allowIncidentIds)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!metadata.TryGetValue(CodeKey, out var actualCode) || !StringComparer.Ordinal.Equals(actualCode, code) ||
            !metadata.TryGetValue(CategoryKey, out var actualCategory) || !StringComparer.Ordinal.Equals(actualCategory, Category) ||
            !metadata.TryGetValue(SummaryKey, out var actualSummary) || !StringComparer.Ordinal.Equals(actualSummary, summary) ||
            metadata.Keys.Any(key =>
                !StringComparer.Ordinal.Equals(key, CodeKey) &&
                !StringComparer.Ordinal.Equals(key, CategoryKey) &&
                !StringComparer.Ordinal.Equals(key, SummaryKey) &&
                (!allowIncidentIds || !StringComparer.Ordinal.Equals(key, IncidentCountKey)) &&
                (!allowIncidentIds || !StringComparer.Ordinal.Equals(key, IncidentIdsTruncatedKey)) &&
                (!allowIncidentIds || !key.StartsWith(IncidentIdPrefix, StringComparison.Ordinal))))
        {
            throw new ArgumentException("DispatchWorkflow terminal diagnostics contain unsupported or noncanonical fields.", nameof(metadata));
        }
    }

    private static string IncidentKey(int index) => $"{IncidentIdPrefix}{index:D3}";

    private static string ValidateIncidentId(string incidentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incidentId);
        return incidentId.Trim();
    }
}
