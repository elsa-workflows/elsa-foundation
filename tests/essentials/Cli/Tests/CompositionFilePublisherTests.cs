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
