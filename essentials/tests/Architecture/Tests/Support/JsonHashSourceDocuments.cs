using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Architecture.Tests;

/// <summary>Build-authenticated source documents; filenames never stand in for a matching PDB or source checksum.</summary>
internal sealed class JsonHashSourceDocuments
{
    internal sealed record Document(string Path, string Sha256, string Origin = "checkout", byte[]? EmbeddedSource = null,
        string? PortableSha256 = null);
    internal sealed record Method(int Token, int? KickoffToken, IReadOnlyList<string> Documents);
    internal IReadOnlyDictionary<string, Document> Documents { get; }
    internal IReadOnlyDictionary<int, Method> Methods { get; }
    internal IReadOnlyDictionary<int, IReadOnlyList<string>> TypeDocuments { get; }
    internal IReadOnlyList<string> Errors { get; }
    internal Guid ImageMvid { get; }

    private JsonHashSourceDocuments(Dictionary<string, Document> documents, Dictionary<int, Method> methods, List<string> errors, Guid imageMvid = default,
        Dictionary<int, IReadOnlyList<string>>? typeDocuments = null)
    {
        Documents = documents; Methods = methods; Errors = errors; ImageMvid = imageMvid;
        TypeDocuments = typeDocuments ?? [];
    }

    internal static JsonHashSourceDocuments Read(string image, string repoRoot, IReadOnlyDictionary<string, string>? pathMaps = null,
        IReadOnlySet<string>? externalCompileSources = null, IReadOnlyList<string>? generatedSourceRoots = null)
    {
        var documents = new Dictionary<string, Document>(StringComparer.Ordinal);
        var methods = new Dictionary<int, Method>();
        var errors = new List<string>();
        var typeDocuments = new Dictionary<int, IReadOnlyList<string>>();
        var mvid = Guid.Empty;
        var symbols = Path.ChangeExtension(image, ".pdb");
        if (!File.Exists(symbols)) return new(documents, methods, [$"Missing portable PDB for {image}."]);
        try
        {
            using var peStream = File.OpenRead(image);
            using var pe = new PEReader(peStream);
            var metadata = pe.GetMetadataReader();
            mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            using var pdbStream = File.OpenRead(symbols);
            using var provider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
            var pdb = provider.GetMetadataReader();
            var entries = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
            if (entries.Length != 1 || entries[0].MinorVersion != 0x504d || pdb.DebugMetadataHeader is not { } header || header.Id.Length != 20)
                return new(documents, methods, [$"Missing or ambiguous PE/PDB debug identity for {image}."]);
            var id = header.Id.AsSpan();
            var codeView = pe.ReadCodeViewDebugDirectoryData(entries[0]);
            if (codeView.Age != 1 || codeView.Guid != new Guid(id[..16]) || entries[0].Stamp != BinaryPrimitives.ReadUInt32LittleEndian(id[16..]))
                return new(documents, methods, [$"PE/PDB debug identity mismatch for {image}."]);
            var checksums = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.PdbChecksum).ToArray();
            if (checksums.Length != 1) return new(documents, methods, [$"Missing or ambiguous PE/PDB checksum for {image}."]);
            var checksum = pe.ReadPdbChecksumDebugDirectoryData(checksums[0]);
            var pdbBytes = File.ReadAllBytes(symbols);
            // Portable PDB checksums clear the 20-byte content ID, which is checked separately above.
            // https://github.com/dotnet/corefx/blob/master/src/System.Reflection.Metadata/specs/PE-COFF.md#portable-pdb-checksum
            if (header.IdStartOffset < 0 || header.IdStartOffset > pdbBytes.Length - header.Id.Length)
                return new(documents, methods, [$"Invalid Portable PDB content ID location for {image}."]);
            pdbBytes.AsSpan(header.IdStartOffset, header.Id.Length).Clear();
            var actualPdb = checksum.AlgorithmName switch
            {
                "SHA256" => SHA256.HashData(pdbBytes),
                "SHA384" => SHA384.HashData(pdbBytes),
                "SHA512" => SHA512.HashData(pdbBytes),
                "SHA1" => SHA1.HashData(pdbBytes),
                _ => null
            };
            if (actualPdb is null || !actualPdb.AsSpan().SequenceEqual(checksum.Checksum.AsSpan()))
                return new(documents, methods, [$"PDB checksum mismatch or unsupported checksum for {image}."]);
            var handles = new Dictionary<DocumentHandle, string>();
            var maps = pathMaps ?? new Dictionary<string, string>();
            foreach (var handle in pdb.Documents)
            {
                var document = pdb.GetDocument(handle);
                var recorded = pdb.GetString(document.Name);
                var path = Resolve(recorded, repoRoot, maps);
                var origin = "checkout";
                // A selected map is authoritative even when its targets are invalid or ambiguous.
                // External input admission must not turn a mapping refusal into a different origin.
                var explicitlyMapped = maps.Keys.Any(prefix => recorded.Replace('\\', '/').StartsWith(
                    prefix.Replace('\\', '/').TrimEnd('/') + '/', StringComparison.Ordinal));
                if (path is null && !explicitlyMapped && ResolveExternal(recorded, repoRoot, externalCompileSources) is { } external)
                { path = external; origin = "external-compile"; }
                if (path is null) { errors.Add($"Unmapped or ambiguous PDB source document: {recorded}"); continue; }
                byte[] bytes;
                if (File.Exists(path)) bytes = File.ReadAllBytes(path);
                else if (origin == "checkout" && IsGenerated(path, generatedSourceRoots) && EmbeddedSource(pdb, handle) is { } embedded)
                { bytes = embedded; origin = "embedded-generated"; }
                else { errors.Add($"Missing PDB source document: {path}"); continue; }
                var algorithm = pdb.GetGuid(document.HashAlgorithm);
                var actual = algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16") ? SHA256.HashData(bytes)
                    : algorithm == new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460") ? SHA1.HashData(bytes) : null;
                if (actual is null || !actual.AsSpan().SequenceEqual(pdb.GetBlobBytes(document.Hash)))
                { errors.Add($"PDB source checksum mismatch or unsupported checksum for {path}."); continue; }
                var relative = origin == "external-compile" ? path : Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
                documents[relative] = new(relative, Convert.ToHexStringLower(SHA256.HashData(bytes)), origin,
                    origin == "embedded-generated" ? bytes : null, PortableDigest(bytes));
                handles[handle] = relative;
            }
            foreach (var handle in metadata.MethodDefinitions)
            {
                var debug = pdb.GetMethodDebugInformation(handle);
                var used = debug.GetSequencePoints().Select(point => point.Document.IsNil ? debug.Document : point.Document)
                    .Append(debug.Document).Where(document => !document.IsNil).Distinct().ToArray();
                var missing = used.Where(document => !handles.ContainsKey(document)).ToArray();
                if (missing.Length != 0) { errors.Add($"Unverified source document for method 0x{MetadataTokens.GetToken(handle):x8} in {image}."); continue; }
                var kickoff = debug.GetStateMachineKickoffMethod();
                var token = MetadataTokens.GetToken(handle);
                methods[token] = new(token, kickoff.IsNil ? null : MetadataTokens.GetToken(kickoff), used.Select(document => handles[document]).Order(StringComparer.Ordinal).ToArray());
            }
            // The compiler's type-document relation supplements method sequence points. In C# it is
            // emitted for top-level types, with nested methods contributing to that same source union.
            // https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#type-definition-document-c-and-vb-compilers
            var groups = new Dictionary<TypeDefinitionHandle, HashSet<string>>();
            var roots = new Dictionary<TypeDefinitionHandle, TypeDefinitionHandle>();
            foreach (var handle in metadata.TypeDefinitions)
            {
                var root = handle;
                while (!metadata.GetTypeDefinition(root).GetDeclaringType().IsNil) root = metadata.GetTypeDefinition(root).GetDeclaringType();
                roots[handle] = root;
                if (!groups.TryGetValue(root, out var paths)) groups[root] = paths = new(StringComparer.Ordinal);
                foreach (var method in metadata.GetTypeDefinition(handle).GetMethods())
                    if (methods.TryGetValue(MetadataTokens.GetToken(method), out var entry)) paths.UnionWith(entry.Documents);
                foreach (var customHandle in pdb.GetCustomDebugInformation(handle))
                {
                    var custom = pdb.GetCustomDebugInformation(customHandle);
                    if (pdb.GetGuid(custom.Kind) != new Guid("932e74bc-dba9-4478-8d46-0f32a7bab3d3")) continue;
                    var blob = pdb.GetBlobReader(custom.Value);
                    while (blob.RemainingBytes > 0)
                    {
                        var row = blob.ReadCompressedInteger();
                        if (row <= 0 || row > pdb.Documents.Count) throw new BadImageFormatException("Invalid type source document row.");
                        if (!handles.TryGetValue(MetadataTokens.DocumentHandle(row), out var path))
                        { errors.Add($"Unverified declaring-type source document for type 0x{MetadataTokens.GetToken(handle):x8} in {image}."); continue; }
                        paths.Add(path);
                    }
                }
            }
            var complete = groups.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.Order(StringComparer.Ordinal).ToArray());
            foreach (var (handle, root) in roots) typeDocuments[MetadataTokens.GetToken(handle)] = complete[root];
        }
        catch (Exception error) when (error is BadImageFormatException or IOException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            errors.Add($"Cannot verify source provenance for {image}: {error.GetType().Name}: {error.Message}");
        }
        return new(documents, methods, errors, mvid, typeDocuments);
    }

    private static string PortableDigest(byte[] authenticatedBytes)
    {
        // Called only after the exact PDB checksum matches these same bytes. Keep that checksum above;
        // normalize source formatting, never compiled literals or the carried JSON value.
        var bytes = authenticatedBytes.AsSpan();
        Encoding encoding = new UTF8Encoding(false, true);
        var prefix = 0;
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); prefix = 4; }
        else if (bytes.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); prefix = 4; }
        else if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); prefix = 2; }
        else if (bytes.StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); prefix = 2; }
        else if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) prefix = 3;
        var text = encoding.GetString(bytes[prefix..]).Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string? ResolveExternal(string recorded, string repoRoot, IReadOnlySet<string>? compileSources)
    {
        // Only an exact evaluated Compile item is eligible. Package/cache directories and
        // filenames are never blanket exemptions from source authentication.
        if (compileSources is null || !Path.IsPathFullyQualified(recorded) ||
            recorded.Replace('\\', '/').Split('/').Any(component => component is "." or "..")) return null;
        var absolute = Path.GetFullPath(recorded);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (absolute.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot)) + Path.DirectorySeparatorChar, comparison)) return null;
        return compileSources.Contains(absolute) && HasNoLinkedAncestor(absolute) ? absolute : null;
    }

    private static bool IsGenerated(string path, IReadOnlyList<string>? roots)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return roots?.Any(root => Path.IsPathFullyQualified(root) && HasNoLinkedAncestor(root) &&
            path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, comparison)) == true;
    }

    private static byte[]? EmbeddedSource(MetadataReader pdb, DocumentHandle document)
    {
        // Portable PDB EmbeddedSource: a zero format prefix contains raw bytes; a positive
        // prefix is the exact uncompressed length of a deflate stream. The PDB/PE identity
        // and whole PDB checksum have already been verified; document checksums follow.
        // https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#embedded-source-c-and-vb-compilers
        var records = pdb.GetCustomDebugInformation(document).Select(pdb.GetCustomDebugInformation)
            .Where(info => pdb.GetGuid(info.Kind) == new Guid("0e8a571b-6926-466e-b4ad-8ab04611f5fe")).ToArray();
        if (records.Length == 0) return null;
        if (records.Length != 1) throw new BadImageFormatException("Ambiguous embedded source records.");
        var blob = pdb.GetBlobBytes(records[0].Value);
        if (blob.Length < sizeof(int)) throw new BadImageFormatException("Truncated embedded source format.");
        var size = BinaryPrimitives.ReadInt32LittleEndian(blob);
        const int maximum = 64 * 1024 * 1024;
        if (size < 0 || size > maximum || size == 0 && blob.Length - sizeof(int) > maximum)
            throw new BadImageFormatException("Unsupported or excessive embedded source size.");
        if (size == 0) return blob[sizeof(int)..];
        using var compressed = new MemoryStream(blob, sizeof(int), blob.Length - sizeof(int), writable: false);
        using var deflate = new DeflateStream(compressed, CompressionMode.Decompress);
        var source = new byte[size];
        deflate.ReadExactly(source);
        if (deflate.ReadByte() != -1) throw new BadImageFormatException("Embedded source length mismatch.");
        return source;
    }

    internal static string? Resolve(string document, string repoRoot, IReadOnlyDictionary<string, string> pathMaps)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var candidates = new HashSet<string>(comparer);
        Add(document);
        var normalized = document.Replace('\\', '/');
        foreach (var (prefix, target) in pathMaps)
        {
            var start = prefix.Replace('\\', '/').TrimEnd('/') + '/';
            if (normalized.StartsWith(start, StringComparison.Ordinal)) Add(Path.Join(target, normalized[start.Length..]));
        }
        return candidates.Count == 1 ? candidates.Single() : null;

        void Add(string path)
        {
            if (!Path.IsPathFullyQualified(path)) return;
            var absolute = Path.GetFullPath(path);
            if (!path.Replace('\\', '/').Split('/').Any(component => component is "." or "..") &&
                Within(absolute, root) && HasNoLinkedAncestor(absolute)) candidates.Add(absolute);
        }

        bool Within(string path, string parent) => path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, comparison);
    }

    // Source ownership requires an ordinary checkout path. Refuse links/reparse points rather
    // than guessing how target ancestors and '..' interact across operating systems.
    private static bool HasNoLinkedAncestor(string path)
    {
        var root = Path.GetPathRoot(path)!;
        var current = root;
        foreach (var component in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Join(current, component);
            FileSystemInfo item = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (item.LinkTarget is not null || item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }
}
