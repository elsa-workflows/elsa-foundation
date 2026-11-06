using System.Globalization;
using System.Numerics;
using Elsa.Etl.Core;
using Elsa.Etl.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Etl.Releases;

internal sealed class EtlReleaseSemanticVerifier(IEtlReleaseArtifactCodec codec) : IReleaseArtifactSemanticVerifier
{
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.etl", "1");
    public IReadOnlyCollection<ReleaseCodecKey> SupportedCodecs { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseCodecKey(EtlReleaseCodecs.MemberKind, EtlReleaseCodecs.Artifact, EtlArtifact.CurrentFormatVersion),
        new ReleaseCodecKey(EtlReleaseCodecs.MemberKind, EtlReleaseCodecs.Artifact, EtlArtifactV2Json.FormatVersion)
    });

    public ValueTask<ReleaseSemanticFacts> VerifyAsync(ReleaseContentMember member, ReadOnlyMemory<byte> payload,
        IReleaseContentReader content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EtlReleaseArtifact release;
        try { release = codec.Decode(payload); }
        catch (EtlReleaseArtifactException exception)
        {
            return ValueTask.FromResult(new ReleaseSemanticFacts(exception.IsUnsupported ? ReleaseSemanticStatus.Unsupported : ReleaseSemanticStatus.Invalid,
                null, null, false, diagnostics: [new(exception.Code, member.Key)]));
        }
        var artifact = release.Artifact;
        var key = new ReleaseMemberKey(EtlReleaseCodecs.MemberKind, artifact.ArtifactId);
        var version = new ReleaseOwnerVersion("integer", null, artifact.DefinitionVersion);
        if (member.Codec != new ReleaseCodecKey(EtlReleaseCodecs.MemberKind, EtlReleaseCodecs.Artifact, artifact.FormatVersion))
            return ValueTask.FromResult(new ReleaseSemanticFacts(ReleaseSemanticStatus.Invalid, key, version, false,
                diagnostics: [new("etl_release_codec_format_mismatch", member.Key)]));

        var resources = new List<ReleaseResourceRequirement>();
        var prerequisites = new List<ReleaseSemanticPrerequisite>();
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        string PrerequisiteKey(string name) => "etl/" + artifact.ArtifactId + "/" + name;
        void Capability(string identity)
        {
            if (capabilities.Add(identity)) prerequisites.Add(new(PrerequisiteKey(identity), key, "runtime-capability", identity, "exact", "1"));
        }
        void Limit(string name, BigInteger minimum) => prerequisites.Add(new(PrerequisiteKey("etl.limit." + name), key,
            "runtime-capacity", "etl." + name, "integer-range", "[" + minimum.ToString(CultureInfo.InvariantCulture) + ",)"));
        void Resource(string[] origin, string alias, string kind, string role)
        {
            resources.Add(new(key, origin, alias, "etl.binding." + kind + "." + role, "1"));
            Capability("etl.connector." + kind);
        }
        Capability("etl.artifact.v" + artifact.FormatVersion.ToString(CultureInfo.InvariantCulture));
        if (artifact.FormatVersion == EtlArtifact.CurrentFormatVersion)
        {
            var pipeline = EtlArtifactJson.Read(artifact.Pipeline);
            Resource(["bindings", "source"], pipeline.Bindings.Source, "csv", "source");
            Resource(["bindings", "lookup"], pipeline.Bindings.Lookup, "csv", "lookup");
            Resource(["bindings", "output"], pipeline.Bindings.Output, "jsonl", "main-output");
            Resource(["bindings", "errors"], pipeline.Bindings.Errors, "jsonl", "error-output");
            var limits = pipeline.Limits;
            Limit("max-partitions", limits.PartitionCount);
            Limit("capacity-records", limits.MaxBatchRecords);
            Limit("capacity-input-bytes", limits.MaxBatchBytes);
            Limit("capacity-output-bytes", limits.MaxOutputBatchBytes);
            Limit("max-lookup-records", limits.MaxLookupRecords);
            Limit("max-lookup-cells", new BigInteger(limits.MaxLookupRecords) * pipeline.LookupSchema.Count);
            Limit("capacity-lookup-bytes", limits.MaxLookupBytes);
            Limit("max-snapshot-bytes", limits.MaxSnapshotBytes);
        }
        else
        {
            var pipeline = EtlArtifactV2Json.Read(artifact.Pipeline);
            var (sourceKind, sourceBinding) = pipeline.Source switch
            {
                EtlCsvSource item => ("csv", item.Binding), EtlHttpJsonSource item => ("http", item.Binding),
                EtlJsonlSource item => ("jsonl", item.Binding), EtlSqlSource item => ("sql", item.Binding),
                EtlRabbitMqSource item => ("rabbitmq", item.Binding), _ => throw new InvalidOperationException("Unverified ETL source.")
            };
            Resource(["source"], sourceBinding, sourceKind, "source");
            if (pipeline.Lookup is { } lookup) Resource(["lookup"], lookup.Binding, "csv", "lookup");
            foreach (var (role, destination) in new[] { ("main-output", pipeline.Routes.Main), ("error-output", pipeline.Routes.Errors) })
            {
                var (kind, binding) = destination switch
                {
                    EtlJsonlDestination item => ("jsonl", item.Binding), EtlHttpDestination item => ("http", item.Binding),
                    EtlCsvDestination item => ("csv", item.Binding), EtlSqlDestination item => ("sql", item.Binding),
                    EtlRabbitMqDestination item => ("rabbitmq", item.Binding), _ => throw new InvalidOperationException("Unverified ETL destination.")
                };
                Resource(["routes", role == "main-output" ? "main" : "errors"], binding, kind, role);
            }
            var limits = pipeline.Limits;
            var http = pipeline.Http;
            Limit("max-partitions", limits.MaxPartitions);
            Limit("capacity-records", pipeline.Source is EtlHttpJsonSource ? Math.Max(limits.MaxBatchRecords, http.MaxPageRecords) : limits.MaxBatchRecords);
            Limit("capacity-input-bytes", pipeline.Source is EtlHttpJsonSource ? Math.Max(limits.MaxBatchBytes, http.MaxPageBytes) : limits.MaxBatchBytes);
            Limit("capacity-output-bytes", limits.MaxOutputBatchBytes);
            Limit("max-snapshot-bytes", limits.MaxSnapshotBytes);
            Limit("max-retained-output-bytes", limits.MaxRetainedOutputBytes);
            if (pipeline.Lookup is { } declaredLookup)
            {
                Limit("max-lookup-records", limits.MaxLookupRecords);
                Limit("max-lookup-cells", new BigInteger(limits.MaxLookupRecords) * declaredLookup.Schema.Count);
                Limit("capacity-lookup-bytes", limits.MaxLookupBytes);
            }
            Limit("http-max-page-bytes", http.MaxPageBytes);
            Limit("http-max-page-records", http.MaxPageRecords);
            Limit("http-max-json-depth", http.MaxJsonDepth);
            Limit("http-max-continuation-bytes", http.MaxContinuationBytes);
            Limit("http-max-pages-per-partition", http.MaxPagesPerPartition);
            Limit("http-max-retained-input-bytes", http.MaxRetainedInputBytes);
            Limit("http-max-retained-metadata-bytes", http.MaxRetainedMetadataBytes);
            Limit("http-max-attempts", http.MaxAttempts);
            Limit("http-max-deadline-seconds", http.DeadlineSeconds);
            Limit("http-max-request-timeout-seconds", http.RequestTimeoutSeconds);
            Limit("http-max-delay-seconds", http.MaxDelaySeconds);
            if (pipeline.Continuous is not null) Capability("etl.continuous.http");
            if (pipeline.Source is EtlSqlSource { Watermark: not null }) Capability("etl.sql.watermark");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ReleaseSemanticFacts(ReleaseSemanticStatus.Verified, key, version, true,
            resources: resources, prerequisites: prerequisites,
            rootCapabilities: [new(key, ReleaseRootAction.MakeAvailable)]));
    }
}
