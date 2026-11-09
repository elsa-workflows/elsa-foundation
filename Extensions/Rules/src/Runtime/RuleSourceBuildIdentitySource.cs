using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Rules.Runtime;

/// <summary>
/// Advertises the identity of the Rules assemblies this process actually loaded from disk, as an explicit opt-in for
/// source-built hosts that have no packaged deployment receipt.
/// </summary>
/// <remarks>
/// The pin keeps the packaged pin shape so admission, publication and recovery compare it like any other: every package
/// id carries the <see cref="ProvenancePrefix"/> marker, the asset path sits under <c>source-build/</c>, and the content
/// hash slot holds the SHA-512 of the loaded assembly file instead of a NuGet archive hash. A source-build identity can
/// therefore never equal a packaged one and stays distinguishable as execution-environment provenance (spec 999 FR-003). It proves which bytes ran, not where they came from: a run admitted under it is
/// recoverable only by a host that loads byte-identical Rules assemblies. The packaged receipt reader is unchanged
/// and remains the only identity a deployment can qualify.
/// </remarks>
internal sealed class RuleSourceBuildIdentitySource(ILogger<RuleSourceBuildIdentitySource> logger) : IRuleImplementationIdentitySource
{
    internal const string ProvenancePrefix = "source-build:";
    private const long MaxAssemblyBytes = 256L * 1024 * 1024;

    private readonly object _cacheLock = new();
    private bool _readCompleted;
    private RuleSemanticPin? _cachedPin;

    public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_cacheLock)
        {
            if (_readCompleted)
                return ValueTask.FromResult(_cachedPin);
        }

        var pin = TryRead(cancellationToken);
        lock (_cacheLock)
        {
            if (!_readCompleted)
            {
                _cachedPin = pin;
                _readCompleted = true;
                if (pin is not null)
                    logger.LogWarning(
                        "Rules is running under a source-build identity ({PinHash}). Runs admitted under it are recoverable only by a host that loads byte-identical Rules assemblies; use a packaged deployment receipt for production hosts.",
                        RuleRecordCodec.PinHash(pin));
            }

            return ValueTask.FromResult(_cachedPin);
        }
    }

    /// <summary>Builds the pin for the loaded assemblies of the three required Rules packages, or null when any cannot be identified from disk.</summary>
    internal static RuleSemanticPin? ReadLoadedAssemblies(CancellationToken cancellationToken = default)
    {
        var required = new (string PackageId, Assembly Assembly)[]
        {
            ("Elsa.Rules.Core", typeof(RuleSemanticPin).Assembly),
            ("Elsa.Rules.Runtime", typeof(RuleSourceBuildIdentitySource).Assembly),
            ("Elsa.Rules.Persistence.Core", typeof(IRuleRunStore).Assembly)
        };
        var packages = new List<RulePackagePin>(required.Length);
        foreach (var (packageId, assembly) in required)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assembly.IsDynamic || AssemblyLoadContext.GetLoadContext(assembly) != AssemblyLoadContext.Default ||
                string.IsNullOrEmpty(assembly.Location) || !File.Exists(assembly.Location))
                return null;
            var name = assembly.GetName();
            if (name.FullName is not { Length: > 0 } fullName || name.Version is not { } version)
                return null;
            var (sha256, sha512) = HashFile(assembly.Location, cancellationToken);
            var asset = new RuleAssetPin("source-build/" + Path.GetFileName(assembly.Location), fullName, sha256);
            packages.Add(new RulePackagePin(ProvenancePrefix + packageId, version.ToString(), sha512, [asset]));
        }

        return new RuleSemanticPin(1, packages, RuleLanguage.Id, 1, 1, 1);
    }

    private RuleSemanticPin? TryRead(CancellationToken cancellationToken)
    {
        try
        {
            return ReadLoadedAssemblies(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
                                              or InvalidOperationException or NotSupportedException)
        {
            logger.LogWarning(exception, "The source-build Rules identity could not be read; Rules admission is unavailable in this host.");
            return null;
        }
    }

    /// <summary>The asset SHA-256 (lowercase hex) and the pin's content-hash slot (canonical Base64 SHA-512) of one assembly file.</summary>
    private static (string Sha256, string Sha512) HashFile(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81_920, FileOptions.SequentialScan);
        if (stream.Length > MaxAssemblyBytes)
            throw new InvalidOperationException("rules.source-build-assembly-too-large");
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        var buffer = new byte[81_920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sha256.AppendData(buffer, 0, read);
            sha512.AppendData(buffer, 0, read);
        }

        return (Convert.ToHexStringLower(sha256.GetHashAndReset()), Convert.ToBase64String(sha512.GetHashAndReset()));
    }
}
