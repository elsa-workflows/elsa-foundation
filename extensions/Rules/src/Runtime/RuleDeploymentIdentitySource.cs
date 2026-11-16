using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;

namespace Elsa.Rules.Runtime;

/// <summary>
/// Reads a host-owned deployment receipt and advertises its pin only while the active PackageReference graph
/// and the loaded Rules assemblies still match it.
/// </summary>
/// <remarks>
/// The receipt is expected beside the application's files. Its pin is a statement produced by the host's
/// package deployment process and uses the codec's pin shape under a top-level <c>receiptSchemaVersion</c>.
/// This reader does not resolve or download packages and never loads assemblies.
/// </remarks>
internal sealed class RuleDeploymentIdentitySource : IRuleImplementationIdentitySource
{
    internal const string DefaultReceiptFileName = "elsa.rules.deployment-receipt.json";

    private const int ReceiptSchemaVersion = 1;
    private const int MaxReceiptBytes = 4 * 1024 * 1024;
    private const int MaxDepsBytes = 16 * 1024 * 1024;
    private const int MaxJsonDepth = 16;
    private const int MaxJsonNodes = 65_536;
    private const int MaxPackages = 128;
    private const int MaxAssetsPerPackage = 64;
    private const int MaxTotalAssets = 256;
    private const int MaxDependencyLibraries = 16_384;
    private const int MaxDependencyAssets = 32_768;
    private const int MaxLoadedAssemblies = 8_192;
    private const long MaxAssetBytes = 256L * 1024 * 1024;
    private const long MaxTotalAssetBytes = 512L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _depsPath;
    private readonly string _receiptPath;
    private readonly string _hostRoot;
    private readonly object _cacheLock = new();
    private bool _readCompleted;
    private RuleSemanticPin? _cachedPin;

    public RuleDeploymentIdentitySource(string depsPath, string receiptPath, string hostRoot)
    {
        _depsPath = depsPath;
        _receiptPath = receiptPath;
        _hostRoot = hostRoot;
    }

    public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_cacheLock)
        {
            if (_readCompleted)
                return ValueTask.FromResult(_cachedPin);
        }

        var pin = TryReadAndValidate(cancellationToken);
        lock (_cacheLock)
        {
            if (!_readCompleted)
            {
                _cachedPin = pin;
                _readCompleted = true;
            }

            return ValueTask.FromResult(_cachedPin);
        }
    }

    private RuleSemanticPin? TryReadAndValidate(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // These are host deployment inputs, not relative paths that may accidentally resolve against
            // the process working directory. Empty defaults deliberately mean "identity unavailable".
            if (!Path.IsPathFullyQualified(_depsPath) || !Path.IsPathFullyQualified(_receiptPath) ||
                !Path.IsPathFullyQualified(_hostRoot))
                return null;

            var physicalRoot = ResolvePhysicalPath(_hostRoot);
            if (!Directory.Exists(physicalRoot) || IsFilesystemRoot(physicalRoot))
                return null;

            var depsBytes = ReadTrustedFile(_depsPath, physicalRoot, MaxDepsBytes, cancellationToken);
            var receiptBytes = ReadTrustedFile(_receiptPath, physicalRoot, MaxReceiptBytes, cancellationToken);
            using var depsDocument = JsonDocument.Parse(depsBytes, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            using var receiptDocument = JsonDocument.Parse(receiptBytes, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });

            if (!HasBoundedJsonShape(depsDocument.RootElement) || !HasBoundedJsonShape(receiptDocument.RootElement))
                return null;

            var pin = ReadReceiptPin(receiptDocument.RootElement);
            if (pin is null)
                return null;

            _ = RuleRecordCodec.PinHash(pin);
            var graph = ReadActiveDependencyGraph(depsDocument.RootElement);
            if (graph is null || !HasRequiredPackagesAndAssemblies(pin))
                return null;

            var loadedAssemblies = ReadLoadedDefaultAssemblies();
            if (loadedAssemblies is null)
                return null;

            long totalAssetBytes = 0;
            foreach (var package in pin.Packages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var library = graph.FindPackage(package.PackageId, package.NuGetVersion);
                if (library is null || !StringComparer.Ordinal.Equals(library.Type, "package"))
                    return null;

                if (library.NuGetContentHash is { } depsHash &&
                    !StringComparer.Ordinal.Equals(depsHash, package.NuGetContentHash))
                    return null;

                foreach (var asset in package.SelectedAssets)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsCanonicalPackagePath(asset.PackageRelativeAssetPath) ||
                        !graph.HasUnambiguousRuntimeAsset(library, asset.PackageRelativeAssetPath))
                        return null;

                    var matchingAssemblies = loadedAssemblies
                        .Where(candidate => StringComparer.Ordinal.Equals(candidate.FullName, asset.AssemblyFullName))
                        .ToArray();
                    if (matchingAssemblies.Length != 1)
                        return null;

                    var loaded = matchingAssemblies[0];
                    if (string.IsNullOrWhiteSpace(loaded.Location) ||
                        !StringComparer.Ordinal.Equals(Path.GetFileName(loaded.Location),
                            Path.GetFileName(asset.PackageRelativeAssetPath)))
                        return null;

                    var physicalAsset = ResolvePhysicalPath(loaded.Location);
                    if (!IsUnderRoot(physicalAsset, physicalRoot))
                        return null;

                    var fileAssemblyName = AssemblyName.GetAssemblyName(physicalAsset).FullName;
                    if (!StringComparer.Ordinal.Equals(fileAssemblyName, asset.AssemblyFullName) ||
                        !StringComparer.Ordinal.Equals(loaded.FullName, fileAssemblyName))
                        return null;

                    var assetHash = HashBoundedFile(physicalAsset, ref totalAssetBytes, cancellationToken);
                    if (!StringComparer.Ordinal.Equals(assetHash, asset.AssetSha256))
                        return null;
                }
            }

            return pin;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsUnavailableFailure(exception))
        {
            return null;
        }
    }

    private static RuleSemanticPin? ReadReceiptPin(JsonElement receipt)
    {
        if (receipt.ValueKind != JsonValueKind.Object || !receipt.TryGetProperty("receiptSchemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var schemaVersion) || schemaVersion != ReceiptSchemaVersion ||
            !receipt.TryGetProperty("pin", out var pinElement) || pinElement.ValueKind != JsonValueKind.Object)
            return null;

        var pinSchemaVersion = RequiredInt32(pinElement, "pinSchemaVersion");
        var languageId = RequiredString(pinElement, "languageId");
        var languageVersion = RequiredInt32(pinElement, "languageVersion");
        var receiptSchema = RequiredInt32(pinElement, "receiptSchemaVersion");
        var stateSchema = RequiredInt32(pinElement, "stateSchemaVersion");
        var packageElements = RequiredArray(pinElement, "packages");
        if (packageElements.GetArrayLength() is < 1 or > MaxPackages)
            return null;

        var packages = new List<RulePackagePin>(packageElements.GetArrayLength());
        var totalAssets = 0;
        foreach (var packageElement in packageElements.EnumerateArray())
        {
            if (packageElement.ValueKind != JsonValueKind.Object)
                return null;
            var packageId = RequiredString(packageElement, "id");
            var version = RequiredString(packageElement, "version");
            var contentHash = RequiredString(packageElement, "contentHash");
            var assetElements = RequiredArray(packageElement, "assets");
            if (assetElements.GetArrayLength() is < 1 or > MaxAssetsPerPackage)
                return null;

            totalAssets += assetElements.GetArrayLength();
            if (totalAssets > MaxTotalAssets)
                return null;

            var assets = new List<RuleAssetPin>(assetElements.GetArrayLength());
            foreach (var assetElement in assetElements.EnumerateArray())
            {
                if (assetElement.ValueKind != JsonValueKind.Object)
                    return null;
                var path = RequiredString(assetElement, "path");
                if (!IsCanonicalPackagePath(path))
                    return null;
                assets.Add(new RuleAssetPin(path, RequiredString(assetElement, "assembly"),
                    RequiredString(assetElement, "sha256")));
            }

            packages.Add(new RulePackagePin(packageId, version, contentHash, assets));
        }

        return new RuleSemanticPin(pinSchemaVersion, packages, languageId, languageVersion, receiptSchema, stateSchema);
    }

    private static DependencyGraph? ReadActiveDependencyGraph(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("runtimeTarget", out var runtimeTarget) ||
            runtimeTarget.ValueKind != JsonValueKind.Object || !runtimeTarget.TryGetProperty("name", out var targetNameElement) ||
            targetNameElement.ValueKind != JsonValueKind.String || targetNameElement.GetString() is not { } targetName ||
            !root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object ||
            !targets.TryGetProperty(targetName, out var activeTarget) || activeTarget.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
            return null;

        var libraryMetadata = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries.EnumerateObject())
        {
            if (libraryMetadata.Count >= MaxDependencyLibraries)
                return null;
            libraryMetadata.Add(library.Name, library.Value);
        }

        var targetSeparator = targetName.LastIndexOf('/');
        var targetRid = targetSeparator >= 0 ? targetName[(targetSeparator + 1)..] : null;
        var packages = new List<DependencyLibrary>();
        var claims = new Dictionary<string, List<AssetClaim>>(StringComparer.OrdinalIgnoreCase);
        var assetCount = 0;
        var targetLibraryCount = 0;
        foreach (var targetLibrary in activeTarget.EnumerateObject())
        {
            if (++targetLibraryCount > MaxDependencyLibraries)
                return null;
            if (!TrySplitLibraryName(targetLibrary.Name, out var packageId, out var version))
                continue;
            if (!libraryMetadata.TryGetValue(targetLibrary.Name, out var metadata) || metadata.ValueKind != JsonValueKind.Object ||
                !metadata.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
                continue;

            if (!TryReadOptionalNuGetHash(metadata, out var contentHash))
                return null;

            var library = new DependencyLibrary(packageId, version, typeElement.GetString()!,
                contentHash);
            if (targetLibrary.Value.ValueKind != JsonValueKind.Object)
                return null;

            if (!ReadRuntimeAssets(targetLibrary.Value, "runtime", isRuntimeTargets: false, targetRid, library, claims, ref assetCount) ||
                !ReadRuntimeAssets(targetLibrary.Value, "runtimeTargets", isRuntimeTargets: true, targetRid, library, claims, ref assetCount))
                return null;

            packages.Add(library);
        }

        return new DependencyGraph(packages, claims);
    }

    private static bool ReadRuntimeAssets(JsonElement targetLibrary, string propertyName, bool isRuntimeTargets, string? targetRid,
        DependencyLibrary library, Dictionary<string, List<AssetClaim>> claims, ref int assetCount)
    {
        if (!targetLibrary.TryGetProperty(propertyName, out var assets))
            return true;
        if (assets.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var asset in assets.EnumerateObject())
        {
            if (++assetCount > MaxDependencyAssets)
                return false;
            if (isRuntimeTargets)
            {
                if (asset.Value.ValueKind != JsonValueKind.Object || !asset.Value.TryGetProperty("assetType", out var assetType) ||
                    assetType.ValueKind != JsonValueKind.String || !StringComparer.Ordinal.Equals(assetType.GetString(), "runtime") ||
                    targetRid is null || !asset.Value.TryGetProperty("rid", out var rid) || rid.ValueKind != JsonValueKind.String ||
                    !StringComparer.Ordinal.Equals(rid.GetString(), targetRid))
                    continue;
            }

            var path = asset.Name.Replace('\\', '/');
            if (!IsCanonicalPackagePath(path))
                return false;

            // NuGet emits _._ placeholders in package graphs to mark an intentionally empty asset group.
            // It is not a runtime assembly and must not make an otherwise valid host graph unavailable.
            if (StringComparer.Ordinal.Equals(Path.GetFileName(path), "_._"))
                continue;

            library.RuntimeAssetPaths.Add(path);
            var fileName = Path.GetFileName(path);
            if (!claims.TryGetValue(fileName, out var assetClaims))
                claims.Add(fileName, assetClaims = []);
            assetClaims.Add(new AssetClaim(library.PackageId, library.Version, path));
        }

        return true;
    }

    private static bool TryReadOptionalNuGetHash(JsonElement metadata, out string? contentHash)
    {
        contentHash = null;
        if (!metadata.TryGetProperty("sha512", out var element))
            return true;
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } value)
            return false;

        // Project and framework libraries commonly carry an empty sha512 field in deps.json. Only a
        // supplied, non-empty hash is corroborating evidence; malformed supplied values fail closed.
        if (value.Length == 0)
            return true;

        ReadOnlySpan<char> encoded = value.AsSpan();
        if (encoded.StartsWith("sha512-", StringComparison.Ordinal))
            encoded = encoded["sha512-".Length..];

        // A SHA-512 value has a fixed canonical Base64 length. Check before decoding so a malicious
        // unrelated deps library cannot trigger a large temporary allocation.
        if (encoded.Length != 88)
            return false;

        Span<byte> decoded = stackalloc byte[64];
        if (!Convert.TryFromBase64Chars(encoded, decoded, out var decodedBytes) || decodedBytes != decoded.Length ||
            !StringComparer.Ordinal.Equals(Convert.ToBase64String(decoded), encoded.ToString()))
            return false;

        contentHash = encoded.ToString();
        return true;
    }

    private static LoadedAssembly[]? ReadLoadedDefaultAssemblies()
    {
        // These types are already part of the loaded Rules implementation. Touching their Assembly objects
        // makes ordinary PackageReference resolution load the three required assemblies without an explicit
        // Assembly.Load call or a separately constructed load context.
        var requiredAssemblies = new[]
        {
            typeof(RuleSemanticPin).Assembly,
            typeof(IRuleRunStore).Assembly,
            typeof(RuleDeploymentIdentitySource).Assembly
        };
        if (requiredAssemblies.Any(assembly => AssemblyLoadContext.GetLoadContext(assembly) != AssemblyLoadContext.Default))
            return null;

        var assemblies = AssemblyLoadContext.Default.Assemblies.Concat(requiredAssemblies).Distinct()
            .Take(MaxLoadedAssemblies + 1).ToArray();
        if (assemblies.Length > MaxLoadedAssemblies)
            return null;

        var loaded = new List<LoadedAssembly>(assemblies.Length);
        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic)
                continue;
            var fullName = assembly.GetName().FullName;
            if (string.IsNullOrEmpty(fullName))
                continue;
            loaded.Add(new LoadedAssembly(fullName, assembly.Location));
        }

        return loaded.ToArray();
    }

    private static string HashBoundedFile(string path, ref long totalBytes, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81_920, FileOptions.SequentialScan);
        if (stream.Length > MaxAssetBytes || totalBytes + stream.Length > MaxTotalAssetBytes)
            throw new InvalidDataException("rules.deployment-asset-too-large");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81_920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            totalBytes += read;
            if (totalBytes > MaxTotalAssetBytes)
                throw new InvalidDataException("rules.deployment-assets-too-large");
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static byte[] ReadTrustedFile(string path, string physicalRoot, int maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var physicalPath = ResolvePhysicalPath(path);
        if (!IsUnderRoot(physicalPath, physicalRoot))
            throw new InvalidDataException("rules.deployment-file-outside-host");

        using var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81_920, FileOptions.SequentialScan);
        if (stream.Length > maxBytes)
            throw new InvalidDataException("rules.deployment-file-too-large");

        using var buffer = new MemoryStream((int)Math.Min(stream.Length, maxBytes));
        var chunk = new byte[81_920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(chunk, 0, chunk.Length);
            if (read == 0)
                break;
            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException("rules.deployment-file-too-large");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static string ResolvePhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var volumeRoot = Path.GetPathRoot(fullPath)
            ?? throw new InvalidDataException("rules.deployment-path-invalid");
        var segments = fullPath[volumeRoot.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var resolved = volumeRoot;
        for (var index = 0; index < segments.Length; index++)
        {
            resolved = Path.Combine(resolved, segments[index]);
            FileSystemInfo entry = index == segments.Length - 1 && File.Exists(resolved)
                ? new FileInfo(resolved)
                : new DirectoryInfo(resolved);
            resolved = entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? resolved;
        }

        return Path.GetFullPath(resolved);
    }

    private static bool IsUnderRoot(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        var rootPrefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootPrefix, comparison) || string.Equals(fullPath, fullRoot, comparison);
    }

    private static bool IsFilesystemRoot(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var volumeRoot = Path.GetPathRoot(fullPath);
        return volumeRoot is not null && string.Equals(fullPath,
            Path.TrimEndingDirectorySeparator(volumeRoot), comparison);
    }

    private static bool HasRequiredPackagesAndAssemblies(RuleSemanticPin pin)
    {
        var requiredAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Rules.Core"] = typeof(RuleSemanticPin).Assembly.GetName().FullName!,
            ["Elsa.Rules.Runtime"] = typeof(RuleDeploymentIdentitySource).Assembly.GetName().FullName!,
            ["Elsa.Rules.Persistence.Core"] = typeof(IRuleRunStore).Assembly.GetName().FullName!
        };

        foreach (var (packageId, assemblyFullName) in requiredAssemblies)
        {
            var matches = pin.Packages.Where(package => StringComparer.OrdinalIgnoreCase.Equals(package.PackageId, packageId))
                .Take(2).ToArray();
            if (matches.Length != 1 || matches[0].SelectedAssets.All(asset =>
                    !StringComparer.Ordinal.Equals(asset.AssemblyFullName, assemblyFullName)))
                return false;
        }

        return true;
    }

    private static bool IsCanonicalPackagePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > RuleLimits.MaxIdentifierUtf8Bytes || path.Contains('\\') ||
            path.StartsWith("/", StringComparison.Ordinal) || path.Contains(':') ||
            !StringComparer.Ordinal.Equals(path, path.Trim()))
            return false;

        try
        {
            if (StrictUtf8.GetByteCount(path) > RuleLimits.MaxIdentifierUtf8Bytes)
                return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        return path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static bool TrySplitLibraryName(string name, out string packageId, out string version)
    {
        var separator = name.LastIndexOf('/');
        if (separator <= 0 || separator == name.Length - 1)
        {
            packageId = string.Empty;
            version = string.Empty;
            return false;
        }

        packageId = name[..separator];
        version = name[(separator + 1)..];
        return true;
    }

    private static bool HasBoundedJsonShape(JsonElement root)
    {
        var nodes = 0;
        return Visit(root, ref nodes);

        static bool Visit(JsonElement element, ref int nodes)
        {
            if (++nodes > MaxJsonNodes)
                return false;

            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name) || !Visit(property.Value, ref nodes))
                        return false;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    if (!Visit(item, ref nodes))
                        return false;
            }

            return true;
        }
    }

    private static JsonElement RequiredArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("rules.deployment-receipt-invalid");
        return value;
    }

    private static string RequiredString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || value.GetString() is not { } text)
            throw new InvalidDataException("rules.deployment-receipt-invalid");
        return text;
    }

    private static int RequiredInt32(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            throw new InvalidDataException("rules.deployment-receipt-invalid");
        return number;
    }

    private static bool IsUnavailableFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or
        JsonException or InvalidDataException or ArgumentException or InvalidOperationException or FormatException or
        OverflowException or NotSupportedException or BadImageFormatException or FileLoadException or FileNotFoundException;

    private sealed record LoadedAssembly(string FullName, string Location);

    private sealed record DependencyLibrary(string PackageId, string Version, string Type, string? NuGetContentHash)
    {
        public HashSet<string> RuntimeAssetPaths { get; } = new(StringComparer.Ordinal);
    }

    private sealed record AssetClaim(string PackageId, string Version, string Path);

    private sealed class DependencyGraph(List<DependencyLibrary> packages, Dictionary<string, List<AssetClaim>> claims)
    {
        public DependencyLibrary? FindPackage(string packageId, string version)
        {
            var matches = packages.Where(package => StringComparer.OrdinalIgnoreCase.Equals(package.PackageId, packageId) &&
                StringComparer.Ordinal.Equals(package.Version, version)).Take(2).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        public bool HasUnambiguousRuntimeAsset(DependencyLibrary library, string path)
        {
            if (!library.RuntimeAssetPaths.Contains(path))
                return false;
            var fileName = Path.GetFileName(path);
            if (!claims.TryGetValue(fileName, out var matches) || matches.Count != 1)
                return false;
            var match = matches[0];
            return StringComparer.OrdinalIgnoreCase.Equals(match.PackageId, library.PackageId) &&
                   StringComparer.Ordinal.Equals(match.Version, library.Version) &&
                   StringComparer.Ordinal.Equals(match.Path, path);
        }
    }

}
