using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Etl.Persistence.EntityFrameworkCore.Stores;

internal static class EtlRuntimeRecordCodec
{
    private const int InvocationJsonMaximumBytes = 512 * 1024;
    private const int BindingJsonMaximumBytes = 64 * 1024;
    private const int TerminalJsonMaximumBytes = 16 * 1024;

    public static ScopeIdentity Scope(EtlScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ValidateOptionalIdentity(scope.TenantId, "tenant id");
        ValidateIdentity(scope.PersistencePartition, "persistence partition");

        var key = EtlRuntimeIdentity.ScopeKey(scope);
        return new(key, scope.TenantId is null ? null : EfRelationalIdentity.Encode(scope.TenantId),
            EfRelationalIdentity.Encode(scope.PersistencePartition));
    }

    public static string EncodeIdentity(string value, string label)
    {
        ValidateIdentity(value, label);
        return EfRelationalIdentity.Encode(value);
    }

    public static string DecodeIdentity(string value, string label) => Decode(value, label);

    public static void EnsureScopeResidual(EtlRuntimeRunRecord row, EtlScope expected)
    {
        var identity = Scope(expected);
        if (!StringComparer.Ordinal.Equals(row.ScopeKey, identity.Key) ||
            !StringComparer.Ordinal.Equals(row.TenantId, identity.TenantId) ||
            !StringComparer.Ordinal.Equals(row.PersistencePartition, identity.PersistencePartition))
            throw new InvalidDataException("The ETL runtime row does not match its scoped identity.");

        EnsureEncodedResidual(row.RunId, expectedRunId: Decode(row.RunKey, "run id"), "run id");
    }

    public static EtlRuntimeRunRecord RunRecord(EtlInvocation invocation, string fingerprint, string? bindingsJson, long nowTicks)
    {
        var scope = Scope(invocation.Scope);
        return new EtlRuntimeRunRecord
        {
            ScopeKey = scope.Key,
            TenantId = scope.TenantId,
            PersistencePartition = scope.PersistencePartition,
            RunKey = EncodeIdentity(invocation.RunId, "run id"),
            RunId = EncodeIdentity(invocation.RunId, "run id"),
            InvocationFingerprint = fingerprint,
            InvocationJson = Write(invocation),
            BindingsJson = bindingsJson,
            Status = (int)EtlRunStatus.Preparing,
            Revision = 1,
            CreatedAtUtcTicks = nowTicks,
            UpdatedAtUtcTicks = nowTicks,
            SchemaVersion = EtlRuntimeEfModule.SchemaVersion
        };
    }

    public static string Write<T>(T value) => EtlRuntimeJson.Write(value);

    public static T Read<T>(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var maximumBytes = typeof(T) == typeof(EtlInvocation) ? InvocationJsonMaximumBytes :
            typeof(T) == typeof(EtlRunBindings) ? BindingJsonMaximumBytes :
            typeof(T) == typeof(EtlTerminalResult) ? TerminalJsonMaximumBytes :
            throw new InvalidOperationException($"No persisted ETL size bound is defined for {typeof(T).Name}.");
        return EtlRuntimeJson.Read<T>(json, maximumBytes);
    }

    public static EtlInvocation ReadInvocation(string json)
    {
        var invocation = Read<EtlInvocation>(json);
        try { ValidateInvocation(invocation); }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new InvalidDataException("The persisted ETL invocation is invalid or unsupported.", exception);
        }
        return invocation;
    }

    public static string HashText(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    public static EtlInvocation ValidateInvocation(EtlInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(invocation.Scope);
        ArgumentNullException.ThrowIfNull(invocation.Artifact);
        ArgumentNullException.ThrowIfNull(invocation.ResultAddress);
        ValidateIdentity(invocation.RunId, "run id");

        var artifact = invocation.Artifact;
        if (artifact.FormatVersion != EtlArtifact.CurrentFormatVersion ||
            artifact.DefinitionVersion <= 0 || artifact.ArtifactId is null or { Length: 0 or > 128 } ||
            artifact.DefinitionId is null or { Length: 0 or > 450 } || !IsSha256(artifact.ContentHash) ||
            artifact.Pipeline.ValueKind != JsonValueKind.Object ||
            JsonMarshal.GetRawUtf8Value(artifact.Pipeline).Length > EtlArtifact.MaximumByteLength)
            throw new ArgumentException("The ETL invocation carries an unsupported or invalid artifact.", nameof(invocation));

        EtlPipeline pipeline;
        try
        {
            pipeline = EtlArtifactJson.Read(artifact.Pipeline);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("The ETL invocation carries an invalid typed pipeline.", nameof(invocation), exception);
        }
        var canonicalPipeline = EtlArtifactJson.Write(pipeline);
        if (!StringComparer.Ordinal.Equals(artifact.Pipeline.GetRawText(), canonicalPipeline.GetRawText()))
            throw new ArgumentException("The ETL invocation artifact pipeline is not in its typed canonical representation.", nameof(invocation));
        if (!StringComparer.Ordinal.Equals(EtlArtifactJson.Hash(canonicalPipeline), artifact.ContentHash))
            throw new ArgumentException("The ETL invocation artifact hash does not match its pipeline.", nameof(invocation));

        var address = invocation.ResultAddress;
        if (address.FormatVersion != EtlNativeResultAddress.CurrentFormatVersion ||
            address.PayloadSchemaVersion <= 0 ||
            !StringComparer.Ordinal.Equals(address.ProviderId, EtlNativeResultAddress.CompletionProviderId))
            throw new ArgumentException("The ETL invocation carries an unsupported native result address.", nameof(invocation));

        ValidateIdentity(address.WorkflowExecutionId, "workflow execution id");
        ValidateIdentity(address.ActivityExecutionId, "activity execution id");
        ValidateIdentity(address.ExecutableId, "executable id");
        ValidateIdentity(address.ExecutableHash, "executable hash");
        ValidateIdentity(address.NodeId, "node id");
        ValidateIdentity(address.BookmarkId, "bookmark id");
        ValidateIdentity(address.RegistrationId, "registration id");
        ValidateIdentity(address.ResumeTargetId, "resume target id");
        ValidateIdentity(address.StimulusType, "stimulus type");
        ValidateIdentity(address.StimulusHash, "stimulus hash");
        ValidateIdentity(address.PayloadTypeAlias, "payload type alias");
        ValidateIdentity(address.DeduplicationPolicy, "deduplication policy");
        ValidatePipelineLimits(pipeline.Limits);
        var artifactBytes = JsonSerializer.SerializeToUtf8Bytes(artifact).Length;
        if (artifactBytes > EtlArtifact.MaximumByteLength || artifactBytes > pipeline.Limits.MaxArtifactBytes)
            throw new ArgumentException("The ETL invocation artifact exceeds its configured serialized size limit.", nameof(invocation));
        if (Encoding.UTF8.GetByteCount(Write(invocation)) > EtlArtifact.MaximumByteLength + 64 * 1024)
            throw new ArgumentException("The ETL invocation exceeds its persisted size bound.", nameof(invocation));

        return invocation;
    }

    public static string ValidateBindings(EtlInvocation invocation, EtlRunBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var pipeline = EtlArtifactJson.Read(invocation.Artifact.Pipeline);
        ValidateBinding(bindings.Source, pipeline.Bindings.Source, input: true);
        ValidateBinding(bindings.Lookup, pipeline.Bindings.Lookup, input: true);
        ValidateBinding(bindings.Output, pipeline.Bindings.Output, input: false);
        ValidateBinding(bindings.Errors, pipeline.Bindings.Errors, input: false);
        return Write(bindings);
    }

    public static EtlRunBindings? ReadBindings(string? json)
    {
        if (json is null) return null;
        try { return Read<EtlRunBindings>(json); }
        catch (JsonException exception) { throw new InvalidDataException("The persisted ETL binding snapshot is invalid.", exception); }
    }

    public static void ValidateStoredBindings(EtlInvocation invocation, EtlRunBindings bindings)
    {
        try { ValidateBindings(invocation, bindings); }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            throw new InvalidDataException("The persisted ETL binding snapshot does not match its artifact.", exception);
        }
    }

    public static EtlPreparedRun ReadPrepared(EtlRuntimePreparedRecord row)
    {
        EnsureSchema(row.SchemaVersion);
        var result = new EtlPreparedRun(row.FormatVersion,
            new(row.SourceKey, row.SourceHash, row.SourceLength),
            new(row.LookupKey, row.LookupHash, row.LookupLength),
            new(row.SourceIndexKey, row.SourceIndexHash, row.SourceIndexLength),
            row.SourceRecordCount);
        try { ValidatePrepared(result); }
        catch (ArgumentException exception) { throw new InvalidDataException("The persisted ETL prepared descriptor is invalid.", exception); }
        return result;
    }

    public static void ValidatePrepared(EtlPreparedRun prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (prepared.FormatVersion != EtlPreparedRun.CurrentFormatVersion || prepared.SourceRecordCount < 0)
            throw new ArgumentException("The prepared ETL descriptor has an unsupported format or record count.", nameof(prepared));
        ValidateObject(prepared.Source, "source snapshot");
        ValidateObject(prepared.Lookup, "lookup snapshot");
        ValidateObject(prepared.SourceIndex, "source index");
    }

    public static void ValidateDiagnostic(EtlRunDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ValidateToken(diagnostic.Code, 128, "diagnostic code");
        if (diagnostic.RecordOrdinal is < 0)
            throw new ArgumentOutOfRangeException(nameof(diagnostic), "An ETL diagnostic ordinal cannot be negative.");
        if (diagnostic.Field is not null)
            ValidateToken(diagnostic.Field, 256, "diagnostic field");
    }

    public static void EnsureSchema(string value)
    {
        try
        {
            EfSchemaVersion.EnsureReadable(EtlRuntimeEfModule.Chain, value);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The persisted ETL runtime row uses an unsupported schema version.", exception);
        }
    }

    public static DateTimeOffset ReadUtc(long ticks)
    {
        try { return new DateTimeOffset(ticks, TimeSpan.Zero); }
        catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("The ETL runtime row has an invalid timestamp.", exception); }
    }

    public static void ValidateIdentity(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > EtlRuntimeEfModule.IdentityMaximumLength ||
            !StringComparer.Ordinal.Equals(value, value.Trim()) || value.Any(char.IsControl))
            throw new ArgumentException($"The ETL {label} must be a bounded exact nonblank value.", label);
    }

    private static void ValidateOptionalIdentity(string? value, string label)
    {
        if (value is not null)
            ValidateIdentity(value, label);
    }

    private static void EnsureEncodedResidual(string encoded, string expectedRunId, string label)
    {
        if (!StringComparer.Ordinal.Equals(Decode(encoded, label), expectedRunId))
            throw new InvalidDataException($"The ETL runtime row does not match its {label} identity.");
    }

    private static string Decode(string encoded, string label)
    {
        try { return EfRelationalIdentity.Decode(encoded); }
        catch (InvalidOperationException exception) { throw new InvalidDataException($"The ETL runtime row has an invalid encoded {label}.", exception); }
    }

    private static void ValidateBinding(EtlBindingRevision binding, string expectedAlias, bool input)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ValidateToken(binding.Alias, 128, "binding alias");
        ValidateIdentity(binding.Revision, "binding revision");
        ValidateIdentity(binding.Handle, "binding handle");
        if (!StringComparer.Ordinal.Equals(binding.Alias, expectedAlias))
            throw new ArgumentException("An ETL binding does not match the published artifact alias.", nameof(binding));
        if (input != (binding.ExpectedSha256 is not null) ||
            input && !IsSha256(binding.ExpectedSha256))
            throw new ArgumentException("An ETL input binding requires its expected SHA-256, and output bindings must not carry one.", nameof(binding));
    }

    public static void ValidateObject(EtlObjectReference reference, string label)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Key.Length is 0 or > 128 || reference.Key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '_' and not '-'))
            throw new ArgumentException($"The ETL {label} key is not a bounded opaque key.", nameof(reference));
        if (!IsSha256(reference.ContentHash) || reference.ByteLength < 0)
            throw new ArgumentException($"The ETL {label} reference has an invalid hash or length.", nameof(reference));
    }

    private static void ValidatePipelineLimits(EtlLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.PartitionCount <= 0 || limits.MaxRecordBytes <= 0 || limits.MaxFields <= 0 || limits.MaxBatchRecords <= 0 ||
            limits.MaxBatchBytes <= 0 || limits.MaxOutstandingRecords <= 0 || limits.MaxOutstandingBytes <= 0 ||
            limits.MaxLookupRecords <= 0 || limits.MaxLookupBytes <= 0 || limits.MaxOutputBatchBytes <= 0 ||
            limits.MaxSnapshotBytes <= 0 || limits.MaxArtifactBytes <= 0 ||
            limits.MaxBatchRecords > limits.MaxOutstandingRecords || limits.MaxBatchBytes > limits.MaxOutstandingBytes ||
            limits.MaxRecordBytes > limits.MaxBatchBytes || limits.MaxLookupBytes > limits.MaxSnapshotBytes ||
            limits.MaxArtifactBytes > EtlArtifact.MaximumByteLength)
            throw new ArgumentException("The ETL artifact contains invalid or unbounded runtime limits.", nameof(limits));
    }

    private static void ValidateToken(string value, int maximum, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum ||
            !StringComparer.Ordinal.Equals(value, value.Trim()) || value.Any(char.IsControl))
            throw new ArgumentException($"The ETL {label} must be a bounded exact nonblank value.", label);
    }

    internal sealed record ScopeIdentity(string Key, string? TenantId, string PersistencePartition);
}
