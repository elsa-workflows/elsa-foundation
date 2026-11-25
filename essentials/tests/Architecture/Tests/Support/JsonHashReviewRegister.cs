using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Elsa.Serialization.Core;

namespace Elsa.Architecture.Tests;

internal static class JsonHashReviewRegister
{
    internal sealed record Site(string Role, string Target, string Operation, int Occurrence,
        IReadOnlyList<string> GenericArguments, string DeclaringTypeContext);
    internal sealed record Owner(string Identity, string Classification, string Reason, IReadOnlyList<string> Sources,
        IReadOnlyList<string> GoldenEvidence, IReadOnlyList<Site> Sites);
    internal sealed record Register(int FormatVersion, string ConsumerSignature, string AuthoritySignature, IReadOnlyList<Owner> Owners)
    {
        public string GoldenSignature { get; init; } = "";
    }
    internal sealed record GoldenReceipt(string Signature, IReadOnlyList<string> Errors);
    private const string RegisterPath = "essentials/tests/Architecture/Tests/Baselines/json-hash-review.json";
    private static readonly HashSet<string> Classes = ["json_routed", "mixed_split_routed", "non_json", "physical_file_witness", "physical_row_witness", "shared_authority"];

    internal static IReadOnlyList<Site> Sites(IEnumerable<JsonHashReferenceGraph.Reference> references) => references
        .Where(reference => reference.Role != "ordinary")
        .Select(reference => new Site(reference.Role, reference.Target, reference.Operation, reference.Occurrence,
            reference.GenericArguments, reference.DeclaringTypeContext))
        .OrderBy(site => site.Role, StringComparer.Ordinal).ThenBy(site => site.Target, StringComparer.Ordinal)
        .ThenBy(site => site.Operation, StringComparer.Ordinal).ThenBy(site => site.Occurrence).ToArray();

    internal static IReadOnlyList<string> Validate(JsonHashReferenceGraph graph, Register register, IReadOnlySet<string> authorityOwners)
    {
        var errors = new List<string>();
        if (register.FormatVersion != 1) errors.Add("Unsupported hash review register format.");
        errors.AddRange(graph.Errors);
        var observed = graph.References.Where(reference => reference.Role != "ordinary").GroupBy(reference => reference.Owner)
            .ToDictionary(group => group.Key, group => Sites(group), StringComparer.Ordinal);
        var rows = register.Owners.GroupBy(owner => owner.Identity).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var missing in observed.Keys.Except(rows.Keys, StringComparer.Ordinal)) errors.Add("Unreviewed hash ingress/primitive/gateway: " + missing);
        foreach (var stale in rows.Keys.Except(observed.Keys, StringComparer.Ordinal)) errors.Add("Stale reviewed hash owner: " + stale);
        foreach (var (identity, entries) in rows)
        {
            if (entries.Length != 1) { errors.Add("Duplicate hash review owner: " + identity); continue; }
            var row = entries[0];
            if (!Classes.Contains(row.Classification) || string.IsNullOrWhiteSpace(row.Reason) || row.Sources.Count == 0)
                errors.Add("Hash review needs its classification, reason and authenticated source: " + identity);
            if (row.Classification is "json_routed" or "mixed_split_routed" && row.GoldenEvidence.Count == 0)
                errors.Add("JSON hash review needs actual-owner golden evidence: " + identity);
            if (authorityOwners.Contains(identity) != (row.Classification == "shared_authority"))
                errors.Add("Only the exact shared authority image may own its primitive allowance: " + identity);
            if (!observed.TryGetValue(identity, out var sites)) continue;
            if (!JsonCanonicalForm.Equals(JsonSerializer.SerializeToElement(sites, JsonBaseline.Options), JsonSerializer.SerializeToElement(row.Sites, JsonBaseline.Options)))
                errors.Add("Changed reviewed hash site/role/generic binding: " + identity);
            if (sites.Any(site => site.Role == "json-hash-input") && row.Classification is not ("json_routed" or "mixed_split_routed" or "shared_authority"))
                errors.Add("JSON gateway must be reviewed as JSON, not as a physical/non-JSON exemption: " + identity);
        }
        return errors;
    }

    internal static GoldenReceipt ReadGoldenEvidence(string repoRoot, IReadOnlyList<Owner> owners)
    {
        var errors = new List<string>();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var digests = new Dictionary<string, string>(StringComparer.Ordinal);
        var bindings = new List<(string Owner, string[] Files)>();
        foreach (var owner in owners.OrderBy(owner => owner.Identity, StringComparer.Ordinal))
        {
            if (owner.Classification is "json_routed" or "mixed_split_routed" && owner.GoldenEvidence.Count == 0)
                errors.Add("JSON hash review needs actual-owner golden evidence: " + owner.Identity);
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (var evidence in owner.GoldenEvidence)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(evidence) || evidence != evidence.Trim() || evidence.Contains(':') ||
                        evidence.Contains('\0') || Path.IsPathRooted(evidence))
                        throw new InvalidOperationException("Golden evidence must be a relative repository file.");
                    var portable = evidence.Replace('\\', '/');
                    if (portable.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
                        throw new InvalidOperationException("Golden evidence cannot contain traversal or path aliases.");
                    var path = Path.GetFullPath(Path.Join(root, portable));
                    if (!path.StartsWith(prefix, comparison) || !HasNoLinkedAncestor(path))
                        throw new InvalidOperationException("Golden evidence escapes the ordinary repository tree.");
                    if (string.Equals(path, Path.GetFullPath(Path.Join(root, RegisterPath)), comparison))
                        throw new InvalidOperationException("The hash review register cannot be its own golden evidence.");
                    if (!File.Exists(path)) throw new FileNotFoundException("Golden evidence file is missing.", portable);
                    if (!files.Add(portable)) throw new InvalidOperationException("Duplicate golden evidence reference.");
                    if (!digests.ContainsKey(portable)) digests[portable] = EvidenceDigest(path);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
                { errors.Add($"Invalid hash golden evidence for {owner.Identity}: {evidence}: {error.Message}"); }
            }
            bindings.Add((owner.Identity, files.Order(StringComparer.Ordinal).ToArray()));
        }
        if (errors.Count != 0) return new("", errors);
        return new(JsonHashRecipeReceipt.Digest(new
        {
            Bindings = bindings.Select(binding => new { binding.Owner, binding.Files }).ToArray(),
            Files = digests.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray()
        }), errors);
    }

    internal static IReadOnlyList<string> ValidateGoldenEvidence(Register reviewed, GoldenReceipt current)
    {
        var errors = current.Errors.ToList();
        if (string.IsNullOrWhiteSpace(reviewed.GoldenSignature) || reviewed.GoldenSignature != current.Signature)
            errors.Add("Changed or missing actual-owner golden file receipt. Review the complete referenced test/vector files before updating GoldenSignature.");
        return errors;
    }

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

    private static string EvidenceDigest(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0) throw new InvalidOperationException("Golden evidence file is empty.");
        // Text witnesses are portable across checkout encoding/EOL. Byte-oriented data fixtures stay exact.
        var text = Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".json" or ".jsonc" or ".py" or ".md" or ".txt" or
            ".ts" or ".tsx" or ".js" or ".jsx" or ".mjs" or ".cjs" or ".mts" or ".cts" or ".xml" or ".props" or ".targets" or ".csproj" or ".yml" or ".yaml";
        if (!text) return "binary:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
        Encoding encoding = new UTF8Encoding(false, true);
        var offset = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, false, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, false, true); offset = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) offset = 3;
        var decoded = encoding.GetString(bytes, offset, bytes.Length - offset);
        if (decoded.Length == 0) throw new InvalidOperationException("Decoded golden evidence text is empty.");
        var normalized = decoded.Replace("\r\n", "\n", StringComparison.Ordinal);
        return "text:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
