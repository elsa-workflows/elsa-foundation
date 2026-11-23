using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Elsa.Architecture.Tests;

/// <summary>Build-authenticated source documents; filenames never stand in for a matching PDB or source checksum.</summary>
internal sealed class JsonHashSourceDocuments
{
    internal sealed record Document(string Path, string Sha256);
    internal sealed record Method(int Token, int? KickoffToken, IReadOnlyList<string> Documents);
    internal IReadOnlyDictionary<string, Document> Documents { get; }
    internal IReadOnlyDictionary<int, Method> Methods { get; }
    internal IReadOnlyList<string> Errors { get; }
    internal Guid ImageMvid { get; }

    private JsonHashSourceDocuments(Dictionary<string, Document> documents, Dictionary<int, Method> methods, List<string> errors, Guid imageMvid = default)
    {
        Documents = documents; Methods = methods; Errors = errors; ImageMvid = imageMvid;
    }

    internal static JsonHashSourceDocuments Read(string image, string repoRoot, IReadOnlyDictionary<string, string>? pathMaps = null)
    {
        var documents = new Dictionary<string, Document>(StringComparer.Ordinal);
        var methods = new Dictionary<int, Method>();
        var errors = new List<string>();
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
            foreach (var handle in pdb.Documents)
            {
                var document = pdb.GetDocument(handle);
                var recorded = pdb.GetString(document.Name);
                var path = Resolve(recorded, repoRoot, pathMaps ?? new Dictionary<string, string>());
                if (path is null) { errors.Add($"Unmapped or ambiguous PDB source document: {recorded}"); continue; }
                if (!File.Exists(path)) { errors.Add($"Missing PDB source document: {path}"); continue; }
                var bytes = File.ReadAllBytes(path);
                var algorithm = pdb.GetGuid(document.HashAlgorithm);
                var actual = algorithm == new Guid("8829d00f-11b8-4213-878b-770e8597ac16") ? SHA256.HashData(bytes)
                    : algorithm == new Guid("ff1816ec-aa5e-4d10-87f7-6f4963833460") ? SHA1.HashData(bytes) : null;
                if (actual is null || !actual.AsSpan().SequenceEqual(pdb.GetBlobBytes(document.Hash)))
                { errors.Add($"PDB source checksum mismatch or unsupported checksum for {path}."); continue; }
                var relative = Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
                documents[relative] = new(relative, Convert.ToHexStringLower(SHA256.HashData(bytes)));
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
        }
        catch (Exception error) when (error is BadImageFormatException or IOException or InvalidOperationException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
        {
            errors.Add($"Cannot verify source provenance for {image}: {error.GetType().Name}: {error.Message}");
        }
        return new(documents, methods, errors, mvid);
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
