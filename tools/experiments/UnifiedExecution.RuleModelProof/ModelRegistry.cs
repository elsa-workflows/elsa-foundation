using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.UnifiedExecution.ModelContracts;
using Elsa.UnifiedExecution.WorkProof;

namespace Elsa.UnifiedExecution.RuleModelProof;

internal sealed record ModelManifest(
    string ModelId,
    string ModelVersion,
    string StateSchema,
    string ArtifactFile,
    string ArtifactSha256,
    string PackageId,
    string PackageVersion,
    string PackageAssembly,
    string PackageSha256,
    string PackageType,
    ModelWorkHandler[] WorkHandlers,
    string[] RequiredCapabilities);

internal sealed record ResolvedModel(
    ModelManifest Manifest,
    PinnedModelArtifact Artifact,
    IExecutionModelPackage Package,
    ICompositionPolicy Policy,
    string PackageAssemblyPath,
    string ExecutionPinFingerprint);

internal sealed class HostCapabilities(IEnumerable<string> values)
{
    public IReadOnlySet<string> Values { get; } = values.ToHashSet(StringComparer.Ordinal);

    public static HostCapabilities Complete { get; } = new(
    [
        "work.scope-authorization.v1",
        "work.claim-fencing.v1",
        "work.result-receipts.v1",
        "composition.atomic-semantic-commit.v1",
        "composition.inspect-state.v1"
    ]);
}

/// <summary>Generic manifest/package resolver. It selects by declared package identity, without model-specific core branches.</summary>
internal static class ModelRegistry
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static ResolvedModel Resolve(string manifestPath, HostCapabilities host)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        ModelManifest manifest = JsonSerializer.Deserialize<ModelManifest>(File.ReadAllText(manifestPath), WebJson)
            ?? throw new InvalidDataException("Model manifest is empty.");

        string[] missing = manifest.RequiredCapabilities.Except(host.Values, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new NotSupportedException($"Host is missing required model capabilities: {string.Join(", ", missing)}.");

        string baseDirectory = Path.GetDirectoryName(manifestPath)!;
        byte[] artifactBytes = File.ReadAllBytes(Path.Combine(baseDirectory, manifest.ArtifactFile));
        string artifactFingerprint = Hash(artifactBytes);
        if (!string.Equals(artifactFingerprint, manifest.ArtifactSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The model artifact differs from the SHA-256 pinned in its manifest.");

        string assemblyPath = Path.GetFullPath(Path.Combine(baseDirectory, manifest.PackageAssembly));
        byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
        if (!string.Equals(Hash(assemblyBytes), manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The model package assembly differs from the SHA-256 pinned in its manifest.");

        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        if (!string.Equals(Hash(File.ReadAllBytes(assembly.Location)), manifest.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The loaded package assembly bytes differ from the package hash pinned in the manifest.");
        Type type = assembly.GetType(manifest.PackageType, throwOnError: true)!;
        if (!typeof(IExecutionModelPackage).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidDataException("The manifest package entry point does not implement the shared model-package contract.");
        var package = (IExecutionModelPackage)Activator.CreateInstance(type)!;
        if (package.PackageId != manifest.PackageId || package.PackageVersion != manifest.PackageVersion
            || !package.WorkHandlers.OrderBy(x => x.Id, StringComparer.Ordinal).ThenBy(x => x.Version, StringComparer.Ordinal)
                .SequenceEqual(manifest.WorkHandlers.OrderBy(x => x.Id, StringComparer.Ordinal).ThenBy(x => x.Version, StringComparer.Ordinal))
            || !package.SupportedStateSchemas.Contains(manifest.StateSchema, StringComparer.Ordinal))
            throw new InvalidDataException("The loaded package descriptor does not match the pinned manifest.");

        var artifact = new PinnedModelArtifact(manifest.ModelId, manifest.ModelVersion, manifest.StateSchema,
            System.Text.Encoding.UTF8.GetString(artifactBytes), artifactFingerprint);
        ICompositionPolicy policy = package.CreatePolicy(artifact);
        string expectedPackagePolicyId = $"{manifest.ModelId}@{manifest.ModelVersion}:{artifactFingerprint}:{manifest.StateSchema}";
        if (policy.Id != expectedPackagePolicyId)
            throw new InvalidDataException("The model policy identity does not match the pinned artifact and state schema.");

        string[] requiredCapabilities = manifest.RequiredCapabilities.Order(StringComparer.Ordinal).ToArray();
        ModelWorkHandler[] handlers = manifest.WorkHandlers.OrderBy(x => x.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Version, StringComparer.Ordinal).ToArray();
        var pin = new ExecutionModelPin(manifest.ModelId, manifest.ModelVersion, artifactFingerprint,
            manifest.StateSchema, manifest.PackageId, manifest.PackageVersion, manifest.PackageType,
            manifest.PackageSha256.ToLowerInvariant(), handlers, requiredCapabilities);
        string executionPinFingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(pin, WebJson));
        ICompositionPolicy pinnedPolicy = new PinnedModelPolicy(policy, $"model-pin:{executionPinFingerprint}");
        return new ResolvedModel(manifest, artifact, package, pinnedPolicy, assemblyPath, executionPinFingerprint);
    }

    public static ResolvedModel ResolveExecution(string manifestPath, HostCapabilities host, ExecutionSnapshot execution)
    {
        ResolvedModel model = Resolve(manifestPath, host);
        if (execution.PolicyId != model.Policy.Id)
            throw new InvalidDataException("Persisted execution references a different model policy than the resolved artifact.");
        model.Package.ValidateState(model.Artifact, execution.StateJson);
        return model;
    }

    private static string Hash(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed record ExecutionModelPin(string ModelId, string ModelVersion, string ArtifactSha256,
        string StateSchema, string PackageId, string PackageVersion, string PackageType, string PackageSha256,
        ModelWorkHandler[] WorkHandlers, string[] RequiredCapabilities);

    private sealed class PinnedModelPolicy(ICompositionPolicy inner, string id) : ICompositionPolicy
    {
        public string Id => id;
        public CompositionPlan Start(string inputJson) => inner.Start(inputJson);
        public CompositionPlan ApplyResult(string stateJson, WorkReceipt receipt) => inner.ApplyResult(stateJson, receipt);
        public CompositionPlan Continue(string stateJson, string continuationId) => inner.Continue(stateJson, continuationId);
    }
}
