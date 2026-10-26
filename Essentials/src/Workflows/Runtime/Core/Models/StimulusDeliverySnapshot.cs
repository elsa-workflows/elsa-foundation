using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Trusted publisher scope captured with a recipient set; it grants no independent authority.</summary>
[JsonConverter(typeof(StimulusDeliveryScopeJsonConverter))]
public sealed class StimulusDeliveryScope
{
    public StimulusDeliveryScope(PersistenceScope persistenceScope, string? tenantId, WorkflowExecutionPartition? partition,
        WorkflowRunKind runKind, WorkflowTestScope? testScope)
    {
        ArgumentNullException.ThrowIfNull(persistenceScope);
        if (tenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!Enum.IsDefined(runKind)) throw new ArgumentOutOfRangeException(nameof(runKind));
        if (testScope is not null && (tenantId is not null && !StringComparer.Ordinal.Equals(testScope.TenantId, tenantId) ||
                                     partition is not null && testScope.Partition != partition))
            throw new ArgumentException("The saved test scope differs from its publisher scope.", nameof(testScope));
        PersistenceScope = persistenceScope;
        TenantId = tenantId;
        Partition = partition;
        RunKind = runKind;
        TestScope = testScope;
    }

    public PersistenceScope PersistenceScope { get; }
    public string? TenantId { get; }
    /// <summary>Null preserves the explicit legacy partition state; it never grants global access.</summary>
    public WorkflowExecutionPartition? Partition { get; }
    public WorkflowRunKind RunKind { get; }
    public WorkflowTestScope? TestScope { get; }

    public static StimulusDeliveryScope From(WorkflowExecutionState source, PersistenceScope persistenceScope)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(persistenceScope, source.TenantId, source.Partition, source.RunKind, source.TestScope);
    }

    /// <summary>Compare the saved facts to the provider's authoritative publisher; do not infer legacy defaults.</summary>
    public bool Matches(WorkflowExecutionState source, PersistenceScope persistenceScope)
    {
        ArgumentNullException.ThrowIfNull(source);
        return PersistenceScope == persistenceScope && TenantId == source.TenantId && Partition == source.Partition &&
               RunKind == source.RunKind && TestScope == source.TestScope;
    }
}

/// <summary>One complete selected binding, including entries sharing an artifact-scoped start key.</summary>
[JsonConverter(typeof(StimulusStartRecipientJsonConverter))]
public sealed class StimulusStartRecipient
{
    public StimulusStartRecipient(WorkflowTriggerBinding binding, WorkflowExecutableIdentity executable)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(binding.Metadata);
        WorkflowTriggerBinding.ValidateId(binding.TriggerBindingId);
        foreach (var identity in new[] { binding.ArtifactId, binding.DefinitionId, binding.ArtifactVersion, binding.ArtifactHash,
                     binding.ExecutableNodeId, binding.StimulusType, binding.StimulusHash, executable.ArtifactId,
                     executable.DefinitionId, executable.DefinitionVersionId, executable.ArtifactVersion, executable.ArtifactHash })
            ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (!binding.IsActive || !Enum.IsDefined(binding.Cardinality) ||
            binding.ArtifactId != executable.ArtifactId || binding.DefinitionId != executable.DefinitionId ||
            binding.ArtifactVersion != executable.ArtifactVersion || binding.ArtifactHash != executable.ArtifactHash)
            throw new ArgumentException("A selected binding must be active and match its complete executable pin.", nameof(binding));
        if (binding.CreatedAt == default || binding.CreatedAt == DateTimeOffset.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(binding));
        Binding = binding with { Metadata = RuntimeModelMetadata.Snapshot(binding.Metadata) };
        Executable = executable;
    }

    public WorkflowTriggerBinding Binding { get; }
    public WorkflowExecutableIdentity Executable { get; }
}

/// <summary>Exact selected wait identity without copying its retained payload.</summary>
[JsonConverter(typeof(StimulusResumeRecipientJsonConverter))]
public sealed class StimulusResumeRecipient
{
    public StimulusResumeRecipient(string workflowExecutionId, string bookmarkId, string activityExecutionId,
        string executableNodeId, string resumeTargetId, string stimulusType, string stimulusHash,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        foreach (var identity in new[] { workflowExecutionId, bookmarkId, activityExecutionId, executableNodeId,
                     resumeTargetId, stimulusType, stimulusHash })
            ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (createdAt == default || createdAt == DateTimeOffset.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(createdAt));
        if (expiresAt <= createdAt) throw new ArgumentOutOfRangeException(nameof(expiresAt));
        WorkflowExecutionId = workflowExecutionId;
        BookmarkId = bookmarkId;
        ActivityExecutionId = activityExecutionId;
        ExecutableNodeId = executableNodeId;
        ResumeTargetId = resumeTargetId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public string WorkflowExecutionId { get; }
    public string BookmarkId { get; }
    public string ActivityExecutionId { get; }
    public string ExecutableNodeId { get; }
    public string ResumeTargetId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? ExpiresAt { get; }

    public static StimulusResumeRecipient From(BookmarkState bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        return new(bookmark.WorkflowExecutionId, bookmark.BookmarkId, bookmark.ActivityExecutionId,
            bookmark.ExecutableNodeId, bookmark.ResumeTargetId, bookmark.StimulusType, bookmark.StimulusHash,
            bookmark.CreatedAt, bookmark.ExpiresAt);
    }

    /// <summary>Exact saved registration identity; expiration and authorization remain dispatcher decisions.</summary>
    public bool Matches(BookmarkState bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        return WorkflowExecutionId == bookmark.WorkflowExecutionId && BookmarkId == bookmark.BookmarkId &&
               ActivityExecutionId == bookmark.ActivityExecutionId && ExecutableNodeId == bookmark.ExecutableNodeId &&
               ResumeTargetId == bookmark.ResumeTargetId && StimulusType == bookmark.StimulusType &&
               StimulusHash == bookmark.StimulusHash && CreatedAt == bookmark.CreatedAt && ExpiresAt == bookmark.ExpiresAt;
    }
}

/// <summary>
/// One immutable first-delivery recipient set on its existing source outbox row. Absence is uninitialized;
/// a present snapshot with empty lists is authoritative. Provider source/CAS authorization is separate.
/// </summary>
[JsonConverter(typeof(StimulusDeliverySnapshotJsonConverter))]
public sealed class StimulusDeliverySnapshot
{
    public const int CurrentVersion = 1;

    public StimulusDeliverySnapshot(int version, string sourceOutboxItemId, string intentFingerprint,
        StimulusDeliveryScope scope, DateTimeOffset selectedAt, string stimulusType, string stimulusHash,
        IReadOnlyList<StimulusStartRecipient> starts, IReadOnlyList<StimulusResumeRecipient> resumes)
    {
        if (version != CurrentVersion) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceOutboxItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intentFingerprint);
        if (intentFingerprint.Length != 64 || intentFingerprint.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("A saved intent identity requires its complete canonical SHA256.", nameof(intentFingerprint));
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        if (selectedAt == default || selectedAt == DateTimeOffset.MaxValue) throw new ArgumentOutOfRangeException(nameof(selectedAt));
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(resumes);
        var savedStarts = starts.ToArray();
        var savedResumes = resumes.ToArray();
        if (savedStarts.Any(item => item is null) || savedResumes.Any(item => item is null))
            throw new ArgumentException("A complete recipient set cannot contain null entries.");
        if (savedStarts.Select(item => item.Binding.TriggerBindingId).Distinct(StringComparer.Ordinal).Count() != savedStarts.Length ||
            savedResumes.Select(item => (item.WorkflowExecutionId, item.BookmarkId)).Distinct().Count() != savedResumes.Length)
            throw new ArgumentException("A complete recipient set cannot contain duplicate identities.");
        if (savedStarts.Any(item => item.Binding.StimulusType != stimulusType || item.Binding.StimulusHash != stimulusHash) ||
            savedResumes.Any(item => item.StimulusType != stimulusType || item.StimulusHash != stimulusHash))
            throw new ArgumentException("Saved recipients must match the cohort's exact stimulus identity.");
        for (var index = 1; index < savedStarts.Length; index++)
        {
            var previous = savedStarts[index - 1].Binding;
            var current = savedStarts[index].Binding;
            var artifactOrder = StringComparer.Ordinal.Compare(previous.ArtifactId, current.ArtifactId);
            if (artifactOrder > 0 || artifactOrder == 0 && StringComparer.Ordinal.Compare(previous.TriggerBindingId, current.TriggerBindingId) > 0)
                throw new ArgumentException("Saved start bindings must preserve the router's artifact/binding order.", nameof(starts));
        }

        Version = version;
        SourceOutboxItemId = sourceOutboxItemId;
        IntentFingerprint = intentFingerprint;
        Scope = scope;
        SelectedAt = selectedAt;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Starts = Array.AsReadOnly(savedStarts);
        Resumes = Array.AsReadOnly(savedResumes);
    }

    public int Version { get; }
    public string SourceOutboxItemId { get; }
    public string IntentFingerprint { get; }
    public StimulusDeliveryScope Scope { get; }
    public DateTimeOffset SelectedAt { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public IReadOnlyList<StimulusStartRecipient> Starts { get; }
    public IReadOnlyList<StimulusResumeRecipient> Resumes { get; }

    /// <summary>Exact structural equality, including the saved order and explicit empty set.</summary>
    public bool IsEquivalentTo(StimulusDeliverySnapshot? other) => other is not null &&
        Version == other.Version && SourceOutboxItemId == other.SourceOutboxItemId &&
        IntentFingerprint == other.IntentFingerprint && SelectedAt == other.SelectedAt &&
        StimulusType == other.StimulusType && StimulusHash == other.StimulusHash &&
        Scope.PersistenceScope == other.Scope.PersistenceScope && Scope.TenantId == other.Scope.TenantId &&
        Scope.Partition == other.Scope.Partition && Scope.RunKind == other.Scope.RunKind && Scope.TestScope == other.Scope.TestScope &&
        Starts.Count == other.Starts.Count && Resumes.Count == other.Resumes.Count &&
        Starts.Zip(other.Starts).All(pair => pair.First.Executable == pair.Second.Executable &&
            pair.First.Binding with { Metadata = pair.Second.Binding.Metadata } == pair.Second.Binding &&
            RuntimeModelMetadata.AreEqual(pair.First.Binding.Metadata, pair.Second.Binding.Metadata)) &&
        Resumes.Zip(other.Resumes).All(pair => pair.First.WorkflowExecutionId == pair.Second.WorkflowExecutionId &&
            pair.First.BookmarkId == pair.Second.BookmarkId && pair.First.ActivityExecutionId == pair.Second.ActivityExecutionId &&
            pair.First.ExecutableNodeId == pair.Second.ExecutableNodeId && pair.First.ResumeTargetId == pair.Second.ResumeTargetId &&
            pair.First.StimulusType == pair.Second.StimulusType && pair.First.StimulusHash == pair.Second.StimulusHash &&
            pair.First.CreatedAt == pair.Second.CreatedAt && pair.First.ExpiresAt == pair.Second.ExpiresAt);

    /// <summary>Structural source binding only. The provider must still verify the persisted row and live claim.</summary>
    public bool MatchesSource(PostCommitDeliveryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return SourceOutboxItemId == context.SourceOutboxItemId && IntentFingerprint == context.IntentFingerprint &&
               Scope.PersistenceScope == context.Scope;
    }
}

internal sealed class StimulusDeliverySnapshotJsonConverter : JsonConverter<StimulusDeliverySnapshot>
{
    public StimulusDeliverySnapshotJsonConverter() { }
    public override StimulusDeliverySnapshot Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<Wire>(ref reader, options) ?? throw new JsonException("A recipient snapshot must be an object.");
        try { return new(wire.Version, wire.SourceOutboxItemId, wire.IntentFingerprint, wire.Scope,
            wire.SelectedAt, wire.StimulusType, wire.StimulusHash, wire.Starts, wire.Resumes); }
        catch (ArgumentException exception) { throw new JsonException("The persisted recipient snapshot is invalid.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, StimulusDeliverySnapshot value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new Wire(value.Version, value.SourceOutboxItemId, value.IntentFingerprint, value.Scope,
            value.SelectedAt, value.StimulusType, value.StimulusHash, value.Starts, value.Resumes), options);

    private sealed record Wire([property: JsonRequired] int Version, [property: JsonRequired] string SourceOutboxItemId,
        [property: JsonRequired] string IntentFingerprint, [property: JsonRequired] StimulusDeliveryScope Scope,
        [property: JsonRequired] DateTimeOffset SelectedAt, [property: JsonRequired] string StimulusType,
        [property: JsonRequired] string StimulusHash, [property: JsonRequired] IReadOnlyList<StimulusStartRecipient> Starts,
        [property: JsonRequired] IReadOnlyList<StimulusResumeRecipient> Resumes);
}

/// <summary>New cohort wire requires complete binding provenance without changing legacy binding readers.</summary>
internal sealed class StimulusStartRecipientJsonConverter : JsonConverter<StimulusStartRecipient>
{
    public StimulusStartRecipientJsonConverter() { }
    public override StimulusStartRecipient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<Wire>(ref reader, options) ?? throw new JsonException("A start recipient must be an object.");
        if (wire.Binding is null) throw new JsonException("A start recipient requires its saved binding.");
        try { return new(wire.Binding.ToBinding(), wire.Executable); }
        catch (ArgumentException exception) { throw new JsonException("The saved start recipient is invalid.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, StimulusStartRecipient value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new Wire(BindingWire.From(value.Binding), value.Executable), options);

    private sealed record Wire([property: JsonRequired] BindingWire Binding,
        [property: JsonRequired] WorkflowExecutableIdentity Executable);

    private sealed record BindingWire([property: JsonRequired] string TriggerBindingId,
        [property: JsonRequired] string ArtifactId, [property: JsonRequired] string DefinitionId,
        [property: JsonRequired] string ArtifactVersion, [property: JsonRequired] string ArtifactHash,
        [property: JsonRequired] string ExecutableNodeId, [property: JsonRequired] string StimulusType,
        [property: JsonRequired] string StimulusHash, [property: JsonRequired] string? CorrelationScope,
        [property: JsonRequired] IReadOnlyDictionary<string, string> Metadata, [property: JsonRequired] DateTimeOffset CreatedAt,
        [property: JsonRequired] string? ActivationId, [property: JsonRequired] string? SlotId,
        [property: JsonRequired] TriggerCardinality Cardinality, [property: JsonRequired] bool IsActive)
    {
        internal WorkflowTriggerBinding ToBinding() => new(TriggerBindingId, ArtifactId, DefinitionId, ArtifactVersion,
            ArtifactHash, ExecutableNodeId, StimulusType, StimulusHash, CorrelationScope, Metadata, CreatedAt,
            ActivationId, SlotId, Cardinality, IsActive);

        internal static BindingWire From(WorkflowTriggerBinding binding) => new(binding.TriggerBindingId, binding.ArtifactId,
            binding.DefinitionId, binding.ArtifactVersion, binding.ArtifactHash, binding.ExecutableNodeId,
            binding.StimulusType, binding.StimulusHash, binding.CorrelationScope, binding.Metadata, binding.CreatedAt,
            binding.ActivationId, binding.SlotId, binding.Cardinality, binding.IsActive);
    }
}

internal sealed class StimulusDeliveryScopeJsonConverter : JsonConverter<StimulusDeliveryScope>
{
    public StimulusDeliveryScopeJsonConverter() { }

    public override StimulusDeliveryScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<Wire>(ref reader, options) ?? throw new JsonException("A delivery scope must be an object.");
        try { return new(wire.PersistenceScope, wire.TenantId, wire.Partition, wire.RunKind, wire.TestScope); }
        catch (ArgumentException exception) { throw new JsonException("The saved delivery scope is invalid.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, StimulusDeliveryScope value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new Wire(value.PersistenceScope, value.TenantId, value.Partition, value.RunKind, value.TestScope), options);

    private sealed record Wire([property: JsonRequired] PersistenceScope PersistenceScope,
        [property: JsonRequired] string? TenantId, [property: JsonRequired] WorkflowExecutionPartition? Partition,
        [property: JsonRequired] WorkflowRunKind RunKind, [property: JsonRequired] WorkflowTestScope? TestScope);
}

internal sealed class StimulusResumeRecipientJsonConverter : JsonConverter<StimulusResumeRecipient>
{
    public StimulusResumeRecipientJsonConverter() { }

    public override StimulusResumeRecipient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wire = JsonSerializer.Deserialize<Wire>(ref reader, options) ?? throw new JsonException("A resume recipient must be an object.");
        try { return new(wire.WorkflowExecutionId, wire.BookmarkId, wire.ActivityExecutionId, wire.ExecutableNodeId,
            wire.ResumeTargetId, wire.StimulusType, wire.StimulusHash, wire.CreatedAt, wire.ExpiresAt); }
        catch (ArgumentException exception) { throw new JsonException("The saved resume recipient is invalid.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, StimulusResumeRecipient value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new Wire(value.WorkflowExecutionId, value.BookmarkId, value.ActivityExecutionId,
            value.ExecutableNodeId, value.ResumeTargetId, value.StimulusType, value.StimulusHash, value.CreatedAt, value.ExpiresAt), options);

    private sealed record Wire([property: JsonRequired] string WorkflowExecutionId, [property: JsonRequired] string BookmarkId,
        [property: JsonRequired] string ActivityExecutionId, [property: JsonRequired] string ExecutableNodeId,
        [property: JsonRequired] string ResumeTargetId, [property: JsonRequired] string StimulusType,
        [property: JsonRequired] string StimulusHash, [property: JsonRequired] DateTimeOffset CreatedAt,
        [property: JsonRequired] DateTimeOffset? ExpiresAt);
}
