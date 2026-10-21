using System.Collections.ObjectModel;

namespace Elsa.Rules.Core;

/// <summary>An exact persistence scope; a null tenant is a distinct un-tenanted scope.</summary>
public sealed record RuleScope
{
    public RuleScope(string? tenantId, string persistencePartition)
    {
        if (tenantId is not null)
            RuleValueValidation.Identifier(tenantId, nameof(tenantId));
        RuleValueValidation.Identifier(persistencePartition, nameof(persistencePartition));
        TenantId = tenantId;
        PersistencePartition = persistencePartition;
    }

    public string? TenantId { get; }
    public string PersistencePartition { get; }
}

/// <summary>The full stable identity of one invocation inside an exact scope.</summary>
public sealed record RuleRunIdentity
{
    public RuleRunIdentity(RuleScope scope, string invocationId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        RuleValueValidation.Identifier(invocationId, nameof(invocationId));
        Scope = scope;
        InvocationId = invocationId;
    }

    public RuleScope Scope { get; }
    public string InvocationId { get; }
}

/// <summary>A lowercase SHA-256 digest of the complete immutable invocation envelope.</summary>
public sealed record RuleInvocationFingerprint
{
    public RuleInvocationFingerprint(string sha256)
    {
        RuleValueValidation.Sha256(sha256, nameof(sha256));
        Sha256 = sha256;
    }

    public string Sha256 { get; }
}

/// <summary>A byte-exact immutable artifact snapshot with a syntactically valid SHA-256 identity.</summary>
public sealed class RuleArtifactSnapshot
{
    private readonly byte[] _canonicalBytes;

    public RuleArtifactSnapshot(int formatVersion, string artifactId, string sha256, ReadOnlyMemory<byte> canonicalBytes)
    {
        if (formatVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        RuleValueValidation.Identifier(artifactId, nameof(artifactId));
        RuleValueValidation.Sha256(sha256, nameof(sha256));
        if (canonicalBytes.Length > RuleLimits.MaxArtifactBytes)
            throw new ArgumentOutOfRangeException(nameof(canonicalBytes), "The artifact exceeds the v1 byte limit.");

        _canonicalBytes = canonicalBytes.ToArray();
        FormatVersion = formatVersion;
        ArtifactId = artifactId;
        Sha256 = sha256;
    }

    public int FormatVersion { get; }
    public string ArtifactId { get; }
    public string Sha256 { get; }

    /// <summary>Returns a copy so callers cannot mutate the retained artifact through a backing array.</summary>
    public ReadOnlyMemory<byte> CanonicalBytes => new(_canonicalBytes.ToArray());
}

/// <summary>A path and cryptographic identity for one selected package runtime asset.</summary>
public sealed record RuleAssetPin
{
    public RuleAssetPin(string packageRelativeAssetPath, string assemblyFullName, string assetSha256)
    {
        RuleValueValidation.Identifier(packageRelativeAssetPath, nameof(packageRelativeAssetPath));
        RuleValueValidation.Identifier(assemblyFullName, nameof(assemblyFullName));
        RuleValueValidation.Sha256(assetSha256, nameof(assetSha256));
        PackageRelativeAssetPath = packageRelativeAssetPath;
        AssemblyFullName = assemblyFullName;
        AssetSha256 = assetSha256;
    }

    public string PackageRelativeAssetPath { get; }
    public string AssemblyFullName { get; }
    public string AssetSha256 { get; }
}

/// <summary>An immutable NuGet package pin and its selected semantic runtime assets.</summary>
public sealed class RulePackagePin
{
    private readonly ReadOnlyCollection<RuleAssetPin> _selectedAssets;

    public RulePackagePin(string packageId, string nuGetVersion, string nuGetContentHash, IReadOnlyList<RuleAssetPin> selectedAssets)
    {
        RuleValueValidation.Identifier(packageId, nameof(packageId));
        RuleValueValidation.Identifier(nuGetVersion, nameof(nuGetVersion));
        RuleValueValidation.Identifier(nuGetContentHash, nameof(nuGetContentHash));
        ArgumentNullException.ThrowIfNull(selectedAssets);
        var copy = selectedAssets.ToArray();
        if (copy.Any(static asset => asset is null))
            throw new ArgumentException("Selected assets cannot contain null values.", nameof(selectedAssets));
        PackageId = packageId;
        NuGetVersion = nuGetVersion;
        NuGetContentHash = nuGetContentHash;
        _selectedAssets = Array.AsReadOnly(copy);
    }

    public string PackageId { get; }
    public string NuGetVersion { get; }
    public string NuGetContentHash { get; }
    public IReadOnlyList<RuleAssetPin> SelectedAssets => _selectedAssets;
}

/// <summary>An immutable package, language, receipt, and state-schema compatibility pin.</summary>
public sealed class RuleSemanticPin
{
    private readonly ReadOnlyCollection<RulePackagePin> _packages;

    public RuleSemanticPin(
        int pinSchemaVersion,
        IReadOnlyList<RulePackagePin> packages,
        string languageId,
        int languageVersion,
        int receiptSchemaVersion,
        int stateSchemaVersion)
    {
        if (pinSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(pinSchemaVersion));
        ArgumentNullException.ThrowIfNull(packages);
        RuleValueValidation.Identifier(languageId, nameof(languageId));
        if (languageVersion <= 0 || receiptSchemaVersion <= 0 || stateSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(languageVersion), "Pinned schema versions must be positive.");

        var copy = packages.ToArray();
        if (copy.Any(static package => package is null))
            throw new ArgumentException("Package pins cannot contain null values.", nameof(packages));
        PinSchemaVersion = pinSchemaVersion;
        _packages = Array.AsReadOnly(copy);
        LanguageId = languageId;
        LanguageVersion = languageVersion;
        ReceiptSchemaVersion = receiptSchemaVersion;
        StateSchemaVersion = stateSchemaVersion;
    }

    public int PinSchemaVersion { get; }
    public IReadOnlyList<RulePackagePin> Packages => _packages;
    public string LanguageId { get; }
    public int LanguageVersion { get; }
    public int ReceiptSchemaVersion { get; }
    public int StateSchemaVersion { get; }
}

/// <summary>The frozen native destination against which a terminal delivery is proved.</summary>
public sealed record RuleNativeResultAddress
{
    public RuleNativeResultAddress(
        int formatVersion,
        string workflowExecutionId,
        string activityExecutionId,
        string executableId,
        string executableHash,
        string nodeId,
        string bookmarkId,
        string resumeTargetId,
        string registrationId,
        string stimulusType,
        string stimulusHash,
        string resultContractId,
        int resultSchemaVersion,
        string deduplicationPolicy,
        string providerId)
    {
        if (formatVersion <= 0 || resultSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(formatVersion));
        RuleValueValidation.Identifier(workflowExecutionId, nameof(workflowExecutionId));
        RuleValueValidation.Identifier(activityExecutionId, nameof(activityExecutionId));
        RuleValueValidation.Identifier(executableId, nameof(executableId));
        RuleValueValidation.Sha256(executableHash, nameof(executableHash));
        RuleValueValidation.Identifier(nodeId, nameof(nodeId));
        RuleValueValidation.Identifier(bookmarkId, nameof(bookmarkId));
        RuleValueValidation.Identifier(resumeTargetId, nameof(resumeTargetId));
        RuleValueValidation.Identifier(registrationId, nameof(registrationId));
        RuleValueValidation.Identifier(stimulusType, nameof(stimulusType));
        RuleValueValidation.Sha256(stimulusHash, nameof(stimulusHash));
        RuleValueValidation.Identifier(resultContractId, nameof(resultContractId));
        RuleValueValidation.Identifier(deduplicationPolicy, nameof(deduplicationPolicy));
        RuleValueValidation.Identifier(providerId, nameof(providerId));
        FormatVersion = formatVersion;
        WorkflowExecutionId = workflowExecutionId;
        ActivityExecutionId = activityExecutionId;
        ExecutableId = executableId;
        ExecutableHash = executableHash;
        NodeId = nodeId;
        BookmarkId = bookmarkId;
        ResumeTargetId = resumeTargetId;
        RegistrationId = registrationId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        ResultContractId = resultContractId;
        ResultSchemaVersion = resultSchemaVersion;
        DeduplicationPolicy = deduplicationPolicy;
        ProviderId = providerId;
    }

    public int FormatVersion { get; }
    public string WorkflowExecutionId { get; }
    public string ActivityExecutionId { get; }
    public string ExecutableId { get; }
    public string ExecutableHash { get; }
    public string NodeId { get; }
    public string BookmarkId { get; }
    public string ResumeTargetId { get; }
    public string RegistrationId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public string ResultContractId { get; }
    public int ResultSchemaVersion { get; }
    public string DeduplicationPolicy { get; }
    public string ProviderId { get; }
}

/// <summary>An immutable invocation envelope captured before admission and reused on replay.</summary>
public sealed class RuleInvocationEnvelope
{
    private readonly ReadOnlyCollection<RuleFact> _initialFacts;

    public RuleInvocationEnvelope(
        RuleRunIdentity identity,
        RuleArtifactSnapshot artifact,
        IReadOnlyList<RuleFact> initialFacts,
        int maxRounds,
        RuleSemanticPin expectedImplementation,
        int invocationSchemaVersion,
        RuleNativeResultAddress? nativeDestination)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(initialFacts);
        ArgumentNullException.ThrowIfNull(expectedImplementation);
        if (initialFacts.Count > RuleLimits.MaxVocabularyFacts)
            throw new ArgumentOutOfRangeException(nameof(initialFacts));
        if (maxRounds is <= 0 or > RuleLimits.MaxRounds)
            throw new ArgumentOutOfRangeException(nameof(maxRounds));
        if (invocationSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(invocationSchemaVersion));
        var copy = initialFacts.ToArray();
        if (copy.Any(static fact => fact is null))
            throw new ArgumentException("Initial facts cannot contain null values.", nameof(initialFacts));
        Identity = identity;
        Artifact = artifact;
        _initialFacts = Array.AsReadOnly(copy);
        MaxRounds = maxRounds;
        ExpectedImplementation = expectedImplementation;
        InvocationSchemaVersion = invocationSchemaVersion;
        NativeDestination = nativeDestination;
    }

    public RuleRunIdentity Identity { get; }
    public RuleArtifactSnapshot Artifact { get; }
    public IReadOnlyList<RuleFact> InitialFacts => _initialFacts;
    public int MaxRounds { get; }
    public RuleSemanticPin ExpectedImplementation { get; }
    public int InvocationSchemaVersion { get; }
    public RuleNativeResultAddress? NativeDestination { get; }
}

/// <summary>Request to admit one frozen invocation.</summary>
public sealed record RuleRunAdmission(RuleInvocationEnvelope Invocation);

/// <summary>Request to cancel an invocation, including when no admission row exists yet.</summary>
public sealed record RuleCancellationRequest(RuleInvocationEnvelope Invocation);

/// <summary>A safe stable diagnostic code, with no arbitrary exception or provider payload.</summary>
public sealed record RuleRunDiagnostic
{
    public RuleRunDiagnostic(string code)
    {
        RuleValueValidation.Identifier(code, nameof(code));
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Permanent deterministic pre-admission refusal for a valid common envelope.</summary>
public sealed record RuleAdmissionRefusal(RuleInvocationEnvelope Invocation, RuleRunDiagnostic Diagnostic);

/// <summary>Stable terminal outcome of a direct or native Rules invocation.</summary>
public enum RuleTerminalKind
{
    Succeeded,
    Faulted,
    Cancelled,
    Refused
}

/// <summary>Complete bounded result facts and diagnostic for one immutable terminal transition.</summary>
public sealed class RuleTerminalResult
{
    private readonly ReadOnlyCollection<RuleFact> _resultFacts;

    public RuleTerminalResult(
        string terminalId,
        RuleTerminalKind kind,
        int committedRounds,
        int resultSchemaVersion,
        IReadOnlyList<RuleFact> resultFacts,
        string finalFactsSha256,
        RuleRunDiagnostic? diagnostic)
    {
        RuleValueValidation.Identifier(terminalId, nameof(terminalId));
        if (committedRounds < 0 || committedRounds > RuleLimits.MaxRounds)
            throw new ArgumentOutOfRangeException(nameof(committedRounds));
        if (resultSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(resultSchemaVersion));
        ArgumentNullException.ThrowIfNull(resultFacts);
        if (resultFacts.Count > RuleLimits.MaxVocabularyFacts)
            throw new ArgumentOutOfRangeException(nameof(resultFacts));
        RuleValueValidation.Sha256(finalFactsSha256, nameof(finalFactsSha256));
        var copy = resultFacts.ToArray();
        if (copy.Any(static fact => fact is null))
            throw new ArgumentException("Terminal facts cannot contain null values.", nameof(resultFacts));
        TerminalId = terminalId;
        Kind = kind;
        CommittedRounds = committedRounds;
        ResultSchemaVersion = resultSchemaVersion;
        _resultFacts = Array.AsReadOnly(copy);
        FinalFactsSha256 = finalFactsSha256;
        Diagnostic = diagnostic;
    }

    public string TerminalId { get; }
    public RuleTerminalKind Kind { get; }
    public int CommittedRounds { get; }
    public int ResultSchemaVersion { get; }
    public IReadOnlyList<RuleFact> ResultFacts => _resultFacts;
    public string FinalFactsSha256 { get; }
    public RuleRunDiagnostic? Diagnostic { get; }
}

/// <summary>The immutable native payload whose proof is required before its delivery is retired.</summary>
public sealed record RuleTerminalEnvelope
{
    public RuleTerminalEnvelope(string deliveryId, RuleNativeResultAddress destination, RuleTerminalResult result, string canonicalPayloadSha256)
    {
        RuleValueValidation.Identifier(deliveryId, nameof(deliveryId));
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(result);
        RuleValueValidation.Sha256(canonicalPayloadSha256, nameof(canonicalPayloadSha256));
        DeliveryId = deliveryId;
        Destination = destination;
        Result = result;
        CanonicalPayloadSha256 = canonicalPayloadSha256;
    }

    public string DeliveryId { get; }
    public RuleNativeResultAddress Destination { get; }
    public RuleTerminalResult Result { get; }
    public string CanonicalPayloadSha256 { get; }
}

/// <summary>The durable proof class establishing that a native terminal delivery is safe to retire.</summary>
public enum RuleDeliveryProofKind
{
    Consumed,
    AlreadyConsumed,
    ParentProvenTerminal
}

/// <summary>Proof identity returned by the native adapter after checking the frozen parent destination.</summary>
public sealed record RuleDeliveryProof
{
    public RuleDeliveryProof(
        RuleDeliveryProofKind kind,
        string destinationIdentity,
        string terminalId,
        string payloadSha256,
        string parentCommitOrTerminalId)
    {
        RuleValueValidation.Identifier(destinationIdentity, nameof(destinationIdentity));
        RuleValueValidation.Identifier(terminalId, nameof(terminalId));
        RuleValueValidation.Sha256(payloadSha256, nameof(payloadSha256));
        RuleValueValidation.Identifier(parentCommitOrTerminalId, nameof(parentCommitOrTerminalId));
        Kind = kind;
        DestinationIdentity = destinationIdentity;
        TerminalId = terminalId;
        PayloadSha256 = payloadSha256;
        ParentCommitOrTerminalId = parentCommitOrTerminalId;
    }

    public RuleDeliveryProofKind Kind { get; }
    public string DestinationIdentity { get; }
    public string TerminalId { get; }
    public string PayloadSha256 { get; }
    public string ParentCommitOrTerminalId { get; }
}
