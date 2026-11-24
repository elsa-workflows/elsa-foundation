using System.Reflection;
using System.Text;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonHashReviewRegisterTests
{
    [Fact]
    public void A_review_accounts_for_exact_sites_and_never_grants_a_helper_a_future_hash_allowance()
    {
        var graph = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("HashRegisterFixture", """
            public static class Recipe {
                public static byte[] Hash(byte[] bytes) => System.Security.Cryptography.SHA256.HashData(bytes);
            }
            """))]);
        var group = Assert.Single(graph.References.Where(reference => reference.Role != "ordinary").GroupBy(reference => reference.Owner));
        var row = new JsonHashReviewRegister.Owner(group.Key, "non_json", "Opaque physical bytes, never serialized JSON.", ["Recipe.cs"], [], JsonHashReviewRegister.Sites(group));
        var reviewed = new JsonHashReviewRegister.Register(1, "consumer", "authority", [row]);
        Assert.Empty(JsonHashReviewRegister.Validate(graph, reviewed, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [] }, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [row with { Sites = [] }] }, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [row with { Classification = "shared_authority" }] }, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [row with { Reason = "" }] }, new HashSet<string>()));
        Assert.NotEmpty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [row with { Classification = "json_routed" }] }, new HashSet<string>()));
        // A nonempty dummy is only a schema control. Shipping separately requires its authenticated file receipt.
        Assert.Empty(JsonHashReviewRegister.Validate(graph, reviewed with { Owners = [row with { Classification = "json_routed", GoldenEvidence = ["DummyGolden.cs"] }] }, new HashSet<string>()));
        var changed = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("HashRegisterFixture", """
            public static class Recipe {
                public static byte[] Hash(byte[] bytes) {
                    _ = System.Security.Cryptography.SHA256.HashData(bytes);
                    return System.Security.Cryptography.SHA256.HashData(bytes);
                }
            }
            """))]);
        Assert.NotEmpty(JsonHashReviewRegister.Validate(changed, reviewed, new HashSet<string>()));
        var added = JsonHashReferenceGraph.Scan([Assembly.Load(FixtureCompiler.Compile("HashRegisterFixture", """
            public static class Recipe {
                public static byte[] Hash(byte[] bytes) => System.Security.Cryptography.SHA256.HashData(bytes);
                public static byte[] NewJsonHash() => System.Security.Cryptography.SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { Value = 1 }));
            }
            """))]);
        Assert.NotEmpty(JsonHashReviewRegister.Validate(added, reviewed, new HashSet<string>()));
    }

    [Fact]
    public void Golden_files_are_bound_to_their_owner_and_deletion_or_content_change_refuses_the_review()
    {
        using var files = new GoldenFiles();
        var first = Path.Join(files.Root, "Golden.cs");
        File.WriteAllText(first, "// fixed input -> pinned digest\n", new UTF8Encoding(false));
        var owner = EvidenceOwner("Golden.cs");
        var initial = JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [owner]);
        Assert.Empty(initial.Errors);
        var reviewed = new JsonHashReviewRegister.Register(1, "consumer", "authority", [owner]) { GoldenSignature = initial.Signature };
        Assert.Empty(JsonHashReviewRegister.ValidateGoldenEvidence(reviewed, initial));
        Assert.NotEmpty(JsonHashReviewRegister.ValidateGoldenEvidence(reviewed with { GoldenSignature = "" }, initial));
        File.WriteAllText(first, "// changed expected digest\n", new UTF8Encoding(false));
        Assert.NotEmpty(JsonHashReviewRegister.ValidateGoldenEvidence(reviewed, JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [owner])));
        File.WriteAllText(first, "// fixed input -> pinned digest\n", new UTF8Encoding(false));
        File.Copy(first, Path.Join(files.Root, "Other.cs"));
        Assert.NotEmpty(JsonHashReviewRegister.ValidateGoldenEvidence(reviewed,
            JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Other.cs")])));
        Assert.NotEmpty(JsonHashReviewRegister.ValidateGoldenEvidence(reviewed,
            JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [owner with { Identity = "AnotherOwner" }])));
        File.Delete(first);
        var deleted = JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [owner]);
        Assert.NotEmpty(deleted.Errors);
        Assert.Equal("", deleted.Signature);
        Assert.NotEmpty(JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner()]).Errors);
        Assert.NotEmpty(JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("missing.cs")]).Errors);
    }

    [Fact]
    public void Text_golden_receipts_are_portable_but_escaped_changes_and_binary_bytes_are_exact()
    {
        using var files = new GoldenFiles();
        var path = Path.Join(files.Root, "Golden.cs");
        const string lf = "// René\nconst string value = \"\\r\\n\";\n";
        File.WriteAllText(path, lf, new UTF8Encoding(false));
        var baseline = JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Golden.cs")]);
        Assert.Empty(baseline.Errors);
        File.WriteAllText(path, lf.Replace("\n", "\r\n", StringComparison.Ordinal), new UTF8Encoding(true));
        Assert.Equal(baseline.Signature, JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Golden.cs")]).Signature);
        File.WriteAllText(path, lf, new UnicodeEncoding(false, true, true));
        Assert.Equal(baseline.Signature, JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Golden.cs")]).Signature);
        File.WriteAllText(path, lf.Replace("\\r\\n", "\\n", StringComparison.Ordinal), new UTF8Encoding(false));
        Assert.NotEqual(baseline.Signature, JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Golden.cs")]).Signature);
        File.WriteAllBytes(path, [0xff]);
        Assert.NotEmpty(JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Golden.cs")]).Errors);
        var binary = Path.Join(files.Root, "Payload.bin");
        File.WriteAllBytes(binary, [0xff, 0, 10]);
        var bytes = JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Payload.bin")]);
        Assert.Empty(bytes.Errors);
        File.WriteAllBytes(binary, [0xff, 0, 13, 10]);
        Assert.NotEqual(bytes.Signature, JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Payload.bin")]).Signature);
    }

    [Fact]
    public void Golden_evidence_requires_ordinary_contained_relative_files_and_cannot_hash_its_register()
    {
        using var files = new GoldenFiles();
        using var outside = new GoldenFiles();
        File.WriteAllText(Path.Join(files.Root, "Golden.cs"), "golden");
        File.WriteAllText(Path.Join(outside.Root, "Outside.cs"), "outside");
        Directory.CreateDirectory(Path.Join(files.Root, "folder"));
        var register = Path.Join(files.Root, "essentials/tests/Architecture/Tests/Baselines/json-hash-review.json");
        Directory.CreateDirectory(Path.GetDirectoryName(register)!);
        File.WriteAllText(register, "{}");
        foreach (var path in new[] { "", " ", "../Outside.cs", "folder/../Golden.cs", "./Golden.cs", "/outside.cs", "\\outside.cs", "C:\\outside.cs",
                     "file:///outside.cs", "https://example.invalid/golden.cs", "Golden.cs:stream", Path.Join(outside.Root, "Outside.cs"), "folder",
                     "essentials/tests/Architecture/Tests/Baselines/json-hash-review.json" })
        {
            var result = JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner(path)]);
            Assert.True(result.Errors.Count != 0 && result.Signature.Length == 0, "Unowned golden must refuse: " + path);
        }
        Assert.NotEmpty(JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner("Golden.cs", "Golden.cs")]).Errors);
    }

    [GoldenLinkFact]
    public void File_and_directory_symlinks_cannot_supply_golden_evidence()
    {
        using var files = new GoldenFiles();
        using var outside = new GoldenFiles();
        File.WriteAllText(Path.Join(outside.Root, "Golden.cs"), "outside");
        File.WriteAllText(Path.Join(files.Root, "Inside.cs"), "inside");
        var directory = Path.Join(files.Root, "escape");
        var file = Path.Join(files.Root, "linked.cs");
        var contained = Path.Join(files.Root, "contained.cs");
        Directory.CreateSymbolicLink(directory, outside.Root);
        File.CreateSymbolicLink(file, Path.Join(outside.Root, "Golden.cs"));
        File.CreateSymbolicLink(contained, Path.Join(files.Root, "Inside.cs"));
        try
        {
            foreach (var path in new[] { "escape/Golden.cs", "linked.cs", "contained.cs" })
                Assert.NotEmpty(JsonHashReviewRegister.ReadGoldenEvidence(files.Root, [EvidenceOwner(path)]).Errors);
        }
        finally { Directory.Delete(directory); File.Delete(file); File.Delete(contained); }
    }

    private static JsonHashReviewRegister.Owner EvidenceOwner(params string[] files) =>
        new("JsonOwner", "json_routed", "Schema fixture for a reviewed JSON recipe.", ["Recipe.cs"], files, []);

    private sealed class GoldenLinkFactAttribute : FactAttribute
    {
        public GoldenLinkFactAttribute()
        {
            if (OperatingSystem.IsWindows()) Skip = "Symlink refusal controls run in the permitted unprivileged Linux qualification; Windows link policy is unchanged.";
        }
    }

    private sealed class GoldenFiles : IDisposable
    {
        internal string Root { get; } = Path.Join(Path.GetTempPath(), "elsa-s9-golden-register-" + Guid.NewGuid().ToString("N"));
        internal GoldenFiles() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(Root);
            Assert.StartsWith(parent, target, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Directory.Delete(target, recursive: true);
        }
    }
}
