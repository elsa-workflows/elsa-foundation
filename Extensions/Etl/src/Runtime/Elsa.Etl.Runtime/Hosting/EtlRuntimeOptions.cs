using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Execution;

namespace Elsa.Etl.Runtime.Hosting;

/// <summary>Validated immutable host settings shared by the finite ETL runtime.</summary>
internal sealed record EtlRuntimeOptions(
    string SnapshotRoot,
    IReadOnlyList<HostFileBinding> FileBindings,
    int MaxConcurrentAttempts,
    EtlAttemptCharge Capacity,
    long MaxLookupRecords,
    long MaxLookupCells,
    int MaxPartitions,
    long MaxSnapshotBytes,
    long MaxRetainedOutputBytes,
    EtlHttpHostLimits HttpLimits)
{
    public static EtlRuntimeOptions Create(EtlRuntimeFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (string.IsNullOrWhiteSpace(feature.SnapshotRoot) || !Path.IsPathFullyQualified(feature.SnapshotRoot) ||
            feature.MaxConcurrentAttempts <= 0 || feature.MaxLiveRecords <= 0 || feature.MaxLiveInputBytes <= 0 ||
            feature.MaxLiveLookupBytes <= 0 || feature.MaxLiveOutputBytes <= 0 || feature.MaxLookupRecords <= 0 ||
            feature.MaxLookupCells <= 0 || feature.MaxPartitions <= 0 ||
            feature.MaxSnapshotBytes <= 0 || feature.MaxRetainedOutputBytes <= 0 || feature.FileBindings is null)
            throw InvalidConfiguration();

        var http = feature.HttpLimits;
        if (http is null || http.MaxPageBytes <= 0 || http.MaxPageRecords <= 0 || http.MaxJsonDepth is <= 0 or > 64 ||
            http.MaxContinuationBytes <= 0 || http.MaxContinuationBytes > EtlHttpPolicy.MaximumSupportedContinuationBytes ||
            http.MaxPagesPerPartition <= 0 || http.MaxRetainedInputBytes <= 0 || http.MaxRetainedMetadataBytes <= 0 ||
            http.MaxAttempts <= 0 || http.MaxDeadlineSeconds <= 0 || http.MaxRequestTimeoutSeconds <= 0 || http.MaxDelaySeconds <= 0 ||
            http.MaxPageBytes > http.MaxRetainedInputBytes || http.MaxContinuationBytes > http.MaxPageBytes ||
            http.MaxRequestTimeoutSeconds > http.MaxDeadlineSeconds || http.MaxDelaySeconds > http.MaxDeadlineSeconds)
            throw InvalidConfiguration();

        try
        {
            var bindings = feature.FileBindings.Select(CreateBinding).ToArray();
            // Validate the complete trusted registry once while composing the feature. The runtime-scoped
            // registry is rebuilt from these immutable values for each scope.
            _ = new HostFileBindings(bindings);

            return new EtlRuntimeOptions(
                Path.GetFullPath(feature.SnapshotRoot),
                Array.AsReadOnly(bindings),
                feature.MaxConcurrentAttempts,
                new EtlAttemptCharge(feature.MaxLiveRecords, feature.MaxLiveInputBytes,
                    feature.MaxLiveLookupBytes, feature.MaxLiveOutputBytes),
                feature.MaxLookupRecords,
                feature.MaxLookupCells,
                feature.MaxPartitions,
                feature.MaxSnapshotBytes,
                feature.MaxRetainedOutputBytes,
                http);
        }
        catch (Exception exception) when (exception is ArgumentException or EtlBindingException or NullReferenceException)
        {
            throw InvalidConfiguration();
        }
    }

    private static HostFileBinding CreateBinding(EtlHostFileBindingOptions? options)
    {
        if (options is null)
            throw InvalidConfiguration();

        var role = options.Role switch
        {
            "Source" => EtlFileRole.Source,
            "Lookup" => EtlFileRole.Lookup,
            "MainOutput" => EtlFileRole.MainOutput,
            "ErrorOutput" => EtlFileRole.ErrorOutput,
            _ => throw InvalidConfiguration()
        };

        var scope = new EtlScope(options.TenantId, options.PersistencePartition);
        return new HostFileBinding(scope, options.Alias, role, options.Revision, options.Path, options.IsCurrent,
            options.ExpectedSha256, options.ImmutableInput, options.AtomicPublication);
    }

    private static InvalidOperationException InvalidConfiguration() =>
        new("ETL runtime host settings are invalid.");
}
