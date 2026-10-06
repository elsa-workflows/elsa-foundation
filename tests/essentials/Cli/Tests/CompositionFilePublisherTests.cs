using System.Text;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionFilePublisherTests
{
    private static readonly byte[] AuthoredJson = Encoding.UTF8.GetBytes("{\"schemaVersion\":\"1\",\"safe\":true}\n");

    [Fact]
    public void Publishes_complete_authored_bytes_after_recheck_through_adjacent_private_stage()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "accepted.json");
        var recheckCalls = 0;

        CompositionFilePublisher.PublishAuthored(destination, fixture.Source, AuthoredJson, () =>
        {
            recheckCalls++;
            Assert.False(File.Exists(destination));
            var stage = Assert.Single(Directory.GetFiles(fixture.Parent, ".accepted.json.*.tmp"));
            Assert.Equal(AuthoredJson, File.ReadAllBytes(stage));
            Assert.Equal(fixture.Parent, Path.GetDirectoryName(stage));
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(stage);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
            }
        });

        Assert.Equal(1, recheckCalls);
        Assert.Equal(AuthoredJson, File.ReadAllBytes(destination));
        Assert.Empty(Directory.GetFiles(fixture.Parent, ".accepted.json.*.tmp"));
        Assert.Equal("unchanged", File.ReadAllText(Path.Join(fixture.Source, "source.json")));
    }

    [Fact]
    public void Existing_destination_is_refused_without_overwrite_or_recheck()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "accepted.json");
        File.WriteAllText(destination, "existing");
        var recheckCalled = false;

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishAuthored(
            destination, fixture.Source, AuthoredJson, () => recheckCalled = true));

        Assert.Equal("bridge-output-exists", refusal.Code);
        Assert.DoesNotContain(fixture.Parent, refusal.Message, StringComparison.Ordinal);
        Assert.False(recheckCalled);
        Assert.Equal("existing", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(fixture.Parent, ".accepted.json.*.tmp"));
    }

    [Fact]
    public void Destination_inside_source_is_refused_before_staging()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Source, "accepted.json");
        var recheckCalled = false;

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishAuthored(
            destination, fixture.Source, AuthoredJson, () => recheckCalled = true));

        Assert.Equal("bridge-output-exists", refusal.Code);
        Assert.False(recheckCalled);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(fixture.Parent, ".accepted.json.*.tmp"));
    }

    [Fact]
    public void Source_recheck_refusal_cleans_staging_and_publishes_nothing()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "accepted.json");

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishAuthored(
            destination,
            fixture.Source,
            AuthoredJson,
            () => throw CliRefusal.Resolution("bridge-source-changed", "safe source-change refusal")));

        Assert.Equal("bridge-source-changed", refusal.Code);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(fixture.Parent, ".accepted.json.*.tmp"));
    }

    [Fact]
    public void Cancellation_during_recheck_cleans_staging_and_uses_review_required_code()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "accepted.json");
        using var cancellation = new CancellationTokenSource();

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishAuthored(
            destination, fixture.Source, AuthoredJson, cancellation.Cancel, cancellation.Token));

        Assert.Equal("bridge-review-required", refusal.Code);
        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(fixture.Parent, ".accepted.json.*.tmp"));
    }

    [Fact]
    public void Destination_created_during_recheck_is_not_overwritten()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "accepted.json");

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishAuthored(
            destination,
            fixture.Source,
            AuthoredJson,
            () => File.WriteAllText(destination, "raced destination")));

        Assert.Equal("bridge-output-exists", refusal.Code);
        Assert.Equal("raced destination", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(fixture.Parent, ".accepted.json.*.tmp"));
    }

    [Fact]
    public void Missing_output_parent_is_a_safe_output_failure()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "missing", "accepted.json");

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishAuthored(
            destination, fixture.Source, AuthoredJson, () => { }));

        Assert.Equal("bridge-output-failed", refusal.Code);
        Assert.DoesNotContain(fixture.Parent, refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(destination)));
    }

    [Fact]
    public void Publishes_every_candidate_file_as_one_fresh_directory_and_preserves_sources()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "candidate");
        var files = new Dictionary<string, byte[]>
        {
            ["shells.json"] = Encoding.UTF8.GetBytes("{\"candidate\":true}"),
            ["appsettings.Production.json"] = Encoding.UTF8.GetBytes("{\"unchanged\":0}")
        };
        var sourceBefore = Directory.GetFiles(fixture.Source)
            .ToDictionary(path => path, File.ReadAllBytes);
        var recheckCalls = 0;

        CompositionFilePublisher.PublishCandidate(destination, fixture.Source, files, () =>
        {
            recheckCalls++;
            Assert.True(Directory.Exists(fixture.Source));
            Assert.False(Directory.Exists(destination));
            var stage = Assert.Single(Directory.GetDirectories(fixture.Parent, ".candidate.*.tmp"));
            Assert.Equal(files.Keys.Order(), Directory.GetFiles(stage).Select(Path.GetFileName).Order());
            foreach (var (name, bytes) in files)
                Assert.Equal(bytes, File.ReadAllBytes(Path.Join(stage, name)));
            if (!OperatingSystem.IsWindows())
            {
                var mode = File.GetUnixFileMode(stage);
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
            }
        });

        Assert.Equal(1, recheckCalls);
        Assert.Equal(files.Keys.Order(), Directory.GetFiles(destination).Select(Path.GetFileName).Order());
        foreach (var (name, bytes) in files)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Join(destination, name)));
        Assert.Equal(sourceBefore.Keys.Order(), Directory.GetFiles(fixture.Source).Order());
        foreach (var (path, bytes) in sourceBefore)
            Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate.*.tmp"));
    }

    [Theory]
    [InlineData("selected.json")]
    [InlineData("unselected.json")]
    public void Source_recheck_refusal_for_selected_or_unselected_change_cleans_staging(string changedFile)
    {
        using var fixture = new PublisherFixture();
        File.WriteAllText(Path.Join(fixture.Source, "selected.json"), "before-selected");
        File.WriteAllText(Path.Join(fixture.Source, "unselected.json"), "before-unselected");
        var destination = Path.Join(fixture.Parent, "candidate");
        var files = new Dictionary<string, byte[]> { ["selected.json"] = Encoding.UTF8.GetBytes("candidate") };
        var sourceSnapshot = Directory.GetFiles(fixture.Source).ToDictionary(path => path, File.ReadAllBytes);

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishCandidate(
            destination,
            fixture.Source,
            files,
            () =>
            {
                File.WriteAllText(Path.Join(fixture.Source, changedFile), "changed");
                if (sourceSnapshot.Any(entry => !File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value)))
                    throw CliRefusal.Resolution("bridge-source-changed", "The source changed during review.");
            }));

        Assert.Equal("bridge-source-changed", refusal.Code);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate.*.tmp"));
        Assert.Equal("changed", File.ReadAllText(Path.Join(fixture.Source, changedFile)));
    }

    [Fact]
    public void Existing_candidate_destination_is_refused_without_overwrite()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "candidate");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Join(destination, "keep.txt"), "existing");
        var recheckCalled = false;

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishCandidate(
            destination,
            fixture.Source,
            new Dictionary<string, byte[]> { ["shells.json"] = AuthoredJson },
            () => recheckCalled = true));

        Assert.Equal("bridge-output-exists", refusal.Code);
        Assert.False(recheckCalled);
        Assert.Equal("existing", File.ReadAllText(Path.Join(destination, "keep.txt")));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate.*.tmp"));
    }

    [Fact]
    public void Candidate_destination_created_during_recheck_is_not_overwritten()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "candidate");

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishCandidate(
            destination,
            fixture.Source,
            new Dictionary<string, byte[]> { ["shells.json"] = AuthoredJson },
            () =>
            {
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Join(destination, "keep.txt"), "raced destination");
            }));

        Assert.Equal("bridge-output-exists", refusal.Code);
        Assert.Equal("raced destination", File.ReadAllText(Path.Join(destination, "keep.txt")));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate.*.tmp"));
    }

    [Fact]
    public void Candidate_destination_inside_source_is_refused()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Source, "candidate");
        var recheckCalled = false;

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishCandidate(
            destination,
            fixture.Source,
            new Dictionary<string, byte[]> { ["shells.json"] = AuthoredJson },
            () => recheckCalled = true));

        Assert.Equal("bridge-output-exists", refusal.Code);
        Assert.False(recheckCalled);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Source, ".candidate.*.tmp"));
    }

    [Fact]
    public void Candidate_cancellation_during_recheck_cleans_private_stage()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "candidate");
        using var cancellation = new CancellationTokenSource();

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishCandidate(
            destination,
            fixture.Source,
            new Dictionary<string, byte[]> { ["shells.json"] = AuthoredJson },
            cancellation.Cancel,
            cancellation.Token));

        Assert.Equal("bridge-review-required", refusal.Code);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate.*.tmp"));
    }

    [Fact]
    public void Candidate_output_failure_cleans_private_stage_and_redacts_details()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "candidate");

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishCandidate(
            destination,
            fixture.Source,
            new Dictionary<string, byte[]> { ["shells.json"] = AuthoredJson },
            () => throw new IOException("private-path-canary")));

        Assert.Equal("bridge-output-failed", refusal.Code);
        Assert.DoesNotContain("private-path-canary", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Parent, refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate.*.tmp"));
    }

    [Fact]
    public void Unsafe_candidate_file_name_is_refused_and_private_stage_is_cleaned()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "candidate");

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishCandidate(
            destination,
            fixture.Source,
            new Dictionary<string, byte[]> { ["../escape.json"] = AuthoredJson },
            () => { }));

        Assert.Equal("bridge-output-failed", refusal.Code);
        Assert.False(File.Exists(Path.Join(fixture.Parent, "escape.json")));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate.*.tmp"));
    }

    [Fact]
    public void Portable_composition_pair_uses_fixed_nested_names_and_private_permissions()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "portable");
        var input = Path.Join(fixture.Parent, "envelope.json");
        File.WriteAllText(input, "public input stays in place");
        var composition = Encoding.UTF8.GetBytes("portable composition");
        var receipt = Encoding.UTF8.GetBytes("opaque private receipt");

        CompositionFilePublisher.PublishPortableComposition(destination, composition, receipt,
            [fixture.Source, input], () =>
            {
                Assert.False(Directory.Exists(destination));
                var staging = Assert.Single(Directory.GetDirectories(fixture.Parent, ".portable.*.tmp"));
                Assert.Equal(composition, File.ReadAllBytes(Path.Join(staging, "public", "composition.json")));
                Assert.Equal(receipt, File.ReadAllBytes(Path.Join(staging, "private", "input-receipt.json")));
                Assert.Equal(2, Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Length);
                AssertPrivateDirectory(staging);
                AssertPrivateDirectory(Path.Join(staging, "public"));
                AssertPrivateDirectory(Path.Join(staging, "private"));
                AssertPrivateFile(Path.Join(staging, "public", "composition.json"));
                AssertPrivateFile(Path.Join(staging, "private", "input-receipt.json"));
            });

        Assert.Equal(composition, File.ReadAllBytes(Path.Join(destination, "public", "composition.json")));
        Assert.Equal(receipt, File.ReadAllBytes(Path.Join(destination, "private", "input-receipt.json")));
        Assert.Equal("public input stays in place", File.ReadAllText(input));
        Assert.Equal("unchanged", File.ReadAllText(Path.Join(fixture.Source, "source.json")));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".portable.*.tmp"));
    }

    [Fact]
    public void Portable_candidate_pair_contains_only_supported_host_files_and_opaque_receipt()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "candidate-artifact");
        var envelope = Path.Join(fixture.Parent, "envelope.json");
        File.WriteAllText(envelope, "keep this input");
        var files = new Dictionary<string, byte[]>
        {
            ["shells.json"] = Encoding.UTF8.GetBytes("candidate shell"),
            ["appsettings.Production.json"] = Encoding.UTF8.GetBytes("candidate overlay")
        };
        var receipt = Encoding.UTF8.GetBytes("opaque candidate receipt");

        CompositionFilePublisher.PublishPortableCandidate(destination, files, receipt,
            [fixture.Source, envelope], () =>
            {
                Assert.False(Directory.Exists(destination));
                var staging = Assert.Single(Directory.GetDirectories(fixture.Parent, ".candidate-artifact.*.tmp"));
                Assert.Equal(files.Keys.Order(), Directory.GetFiles(Path.Join(staging, "candidate")).Select(Path.GetFileName).Order());
                Assert.Equal(receipt, File.ReadAllBytes(Path.Join(staging, "private", "candidate-receipt.json")));
                Assert.Equal(3, Directory.GetFiles(staging, "*", SearchOption.AllDirectories).Length);
                AssertPrivateDirectory(staging);
                AssertPrivateDirectory(Path.Join(staging, "candidate"));
                AssertPrivateDirectory(Path.Join(staging, "private"));
                foreach (var path in Directory.GetFiles(staging, "*", SearchOption.AllDirectories))
                    AssertPrivateFile(path);
            });

        Assert.Equal(files.Keys.Order(), Directory.GetFiles(Path.Join(destination, "candidate")).Select(Path.GetFileName).Order());
        foreach (var (name, bytes) in files)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Join(destination, "candidate", name)));
        Assert.Equal(receipt, File.ReadAllBytes(Path.Join(destination, "private", "candidate-receipt.json")));
        Assert.False(File.Exists(Path.Join(destination, "candidate", "source.json")));
        Assert.Equal("keep this input", File.ReadAllText(envelope));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate-artifact.*.tmp"));
    }

    [Fact]
    public void Portable_publication_cancellation_after_staging_leaves_no_half_pair()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "portable-cancelled");
        using var cancellation = new CancellationTokenSource();

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishPortableComposition(
            destination, AuthoredJson, AuthoredJson, [fixture.Source], cancellation.Cancel, cancellation.Token));

        Assert.Equal("bridge-review-required", refusal.Code);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".portable-cancelled.*.tmp"));
    }

    [Fact]
    public void Portable_publication_recheck_failure_cleans_staging_without_publishing_either_half()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "portable-drift");

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishPortableComposition(
            destination, AuthoredJson, AuthoredJson, [fixture.Source],
            () => throw CliRefusal.Resolution("bridge-source-changed", "safe drift refusal")));

        Assert.Equal("bridge-source-changed", refusal.Code);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".portable-drift.*.tmp"));
    }

    [Fact]
    public void Portable_candidate_write_failure_cleans_the_partially_written_staging_directory()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "portable-write-failed");
        // The first file is writable; the next supported basename exceeds the filesystem component limit.
        // This fails in FileStream creation during staging, before the publication recheck can run.
        var files = new Dictionary<string, byte[]>
        {
            ["shells.json"] = AuthoredJson,
            [$"appsettings.{new string('x', 300)}.json"] = AuthoredJson
        };
        var recheckCalled = false;

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishPortableCandidate(
            destination, files, AuthoredJson, [fixture.Source], () => recheckCalled = true));

        Assert.Equal("bridge-output-failed", refusal.Code);
        Assert.False(recheckCalled);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".portable-write-failed.*.tmp"));
        Assert.Equal("unchanged", File.ReadAllText(Path.Join(fixture.Source, "source.json")));
    }

    [Fact]
    public void Portable_publication_refuses_existing_output_and_protected_root_overlap()
    {
        using var fixture = new PublisherFixture();
        var existing = Path.Join(fixture.Parent, "existing-portable");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Join(existing, "keep.txt"), "existing");
        var recheckCalled = false;

        var conflict = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishPortableComposition(
            existing, AuthoredJson, AuthoredJson, [fixture.Source], () => recheckCalled = true));
        var overlap = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishPortableCandidate(
            Path.Join(fixture.Source, "candidate"),
            new Dictionary<string, byte[]> { ["shells.json"] = AuthoredJson }, AuthoredJson,
            [fixture.Source], () => recheckCalled = true));

        Assert.Equal("bridge-output-exists", conflict.Code);
        Assert.Equal("bridge-output-exists", overlap.Code);
        Assert.False(recheckCalled);
        Assert.Equal("existing", File.ReadAllText(Path.Join(existing, "keep.txt")));
        Assert.False(Directory.Exists(Path.Join(fixture.Source, "candidate")));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".existing-portable.*.tmp"));
    }

    [Fact]
    public void Portable_publication_requires_a_protected_input_root()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "portable-unprotected");
        var recheckCalled = false;

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishPortableComposition(
            destination, AuthoredJson, AuthoredJson, [], () => recheckCalled = true));

        Assert.Equal("bridge-output-failed", refusal.Code);
        Assert.False(recheckCalled);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".portable-unprotected.*.tmp"));
    }

    [Fact]
    public void Portable_destination_created_during_recheck_is_not_replaced_by_a_partial_pair()
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "portable-race");

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishPortableComposition(
            destination, AuthoredJson, AuthoredJson, [fixture.Source], () =>
            {
                Directory.CreateDirectory(destination);
                File.WriteAllText(Path.Join(destination, "keep.txt"), "raced output");
            }));

        Assert.Equal("bridge-output-exists", refusal.Code);
        Assert.Equal("raced output", File.ReadAllText(Path.Join(destination, "keep.txt")));
        Assert.False(File.Exists(Path.Join(destination, "public", "composition.json")));
        Assert.False(File.Exists(Path.Join(destination, "private", "input-receipt.json")));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".portable-race.*.tmp"));
    }

    [Theory]
    [InlineData("SHELLS.JSON")]
    [InlineData("nested/shells.json")]
    [InlineData("unsupported.json")]
    public void Portable_candidate_rejects_unsafe_or_case_colliding_names_before_staging(string alternateName)
    {
        using var fixture = new PublisherFixture();
        var destination = Path.Join(fixture.Parent, "candidate-names");
        var files = new Dictionary<string, byte[]> { ["shells.json"] = AuthoredJson };
        files[alternateName] = AuthoredJson;

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFilePublisher.PublishPortableCandidate(
            destination, files, AuthoredJson, [fixture.Source], () => { }));

        Assert.Equal("bridge-output-failed", refusal.Code);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetDirectories(fixture.Parent, ".candidate-names.*.tmp"));
    }

    private static void AssertPrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));
    }

    private static void AssertPrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        var mode = File.GetUnixFileMode(path);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
            mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
    }

    private sealed class PublisherFixture : IDisposable
    {
        private readonly string _root = Path.Join(Path.GetTempPath(), "elsa-composition-publisher-" + Guid.NewGuid().ToString("N"));

        public PublisherFixture()
        {
            Parent = _root;
            Source = Path.Join(_root, "host");
            Directory.CreateDirectory(Source);
            File.WriteAllText(Path.Join(Source, "source.json"), "unchanged");
        }

        public string Parent { get; }
        public string Source { get; }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
