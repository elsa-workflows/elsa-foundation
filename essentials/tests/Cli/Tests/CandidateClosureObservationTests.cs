using System.Security.Cryptography;
using System.Text;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CandidateClosureObservationTests
{
    private readonly Dictionary<string, byte[]?> files = new(StringComparer.Ordinal);
    private IReadOnlyList<InstalledPackage> packages = [];
    private readonly string root = Path.GetFullPath("candidate-closure-fixture");

    [Fact]
    public void Unchanged_required_and_absent_files_are_admitted()
    {
        var observer = Create();
        Set("Host.deps.json", "{}");
        observer.ObserveFile(PathOf("Host.deps.json"), true);
        observer.ObserveFile(PathOf("absent.json"), false);
        observer.VerifyUnchanged();
    }

    [Theory]
    [InlineData("Host.deps.json")]
    [InlineData("Host.runtimeconfig.json")]
    [InlineData("store-state.json")]
    public void Changed_observed_bytes_refuse_without_disclosing_private_values(string name)
    {
        var observer = Create();
        Set(name, "initial");
        observer.ObserveFile(PathOf(name), true);
        Set(name, "secret-drift-canary");
        var refusal = Assert.Throws<WorkerRefusal>(() => observer.VerifyUnchanged());
        Assert.Equal("candidate-closure-changed", refusal.Code);
        Assert.DoesNotContain("canary", refusal.Message);
        Assert.Empty(refusal.Details);
    }

    [Fact]
    public void Previously_absent_state_or_manifest_cannot_appear_during_loading()
    {
        var observer = Create();
        observer.ObserveFile(PathOf("state.json"), false);
        Set("state.json", "{}");
        Assert.Equal("candidate-closure-changed", Assert.Throws<WorkerRefusal>(() => observer.VerifyUnchanged()).Code);
    }

    [Fact]
    public void Missing_required_input_refuses_before_loading()
    {
        var observer = Create();
        Assert.Equal("candidate-host-unavailable", Assert.Throws<WorkerRefusal>(() =>
            observer.ObserveFile(PathOf("Host.deps.json"), true)).Code);
    }

    [Fact]
    public void Captured_deps_are_compared_to_the_bytes_that_were_parsed()
    {
        var observer = Create();
        Set("Host.deps.json", "changed");
        observer.ObserveCapturedFile(PathOf("Host.deps.json"), Encoding.UTF8.GetBytes("initial"));
        Assert.Equal("candidate-closure-changed", Assert.Throws<WorkerRefusal>(() => observer.VerifyUnchanged()).Code);
    }

    [Theory]
    [InlineData(".nuplane-ready")]
    [InlineData("nuplane.json")]
    [InlineData("elsa-package.json")]
    [InlineData("build/elsa-package.json")]
    public void Selected_install_marker_and_manifest_changes_refuse(string name)
    {
        var observer = Create();
        var package = Package("Widgets", "1", "install");
        packages = [package];
        var path = Path.Join(package.InstallPath, name);
        files[path] = Hash("initial");
        observer.ObservePackages(packages, new[] { root });
        files[path] = Hash("changed");
        Assert.Equal("candidate-closure-changed", Assert.Throws<WorkerRefusal>(() => observer.VerifyUnchanged()).Code);
    }

    [Fact]
    public void Probe_set_changes_refuse_and_order_alone_does_not()
    {
        var observer = Create();
        packages = [Package("A", "1", "a"), Package("B", "1", "b")];
        observer.ObservePackages(packages, new[] { root });
        packages = packages.Reverse().ToArray();
        observer.VerifyUnchanged();
        packages = [Package("A", "2", "a"), Package("B", "1", "b")];
        Assert.Equal("candidate-closure-changed", Assert.Throws<WorkerRefusal>(() => observer.VerifyUnchanged()).Code);
    }

    [Fact]
    public void Ambiguous_case_equivalent_package_identity_refuses()
    {
        var observer = Create();
        Assert.Equal("candidate-host-unavailable", Assert.Throws<WorkerRefusal>(() => observer.ObservePackages(new[] { Package("A", "1", "a"), Package("a", "2", "b") }, Array.Empty<string>())).Code);
    }

    [Fact]
    public void Loader_affecting_marker_timestamp_change_refuses_even_when_bytes_are_equal()
    {
        var observer = Create();
        packages = [Package("A", "1", "a")];
        observer.ObservePackages(packages, new[] { root });
        packages = [packages[0] with { InstalledAtUtc = DateTimeOffset.UnixEpoch.AddSeconds(1) }];
        Assert.Equal("candidate-closure-changed", Assert.Throws<WorkerRefusal>(() => observer.VerifyUnchanged()).Code);
    }

    [Fact]
    public void Captured_fingerprints_are_owned_and_read_failures_are_value_free()
    {
        var observer = Create();
        Set("Host.deps.json", "initial");
        observer.ObserveFile(PathOf("Host.deps.json"), true);
        files[PathOf("Host.deps.json")]![0] ^= 1;
        Assert.Equal("candidate-closure-changed", Assert.Throws<WorkerRefusal>(() => observer.VerifyUnchanged()).Code);
        var broken = Create(_ => throw new IOException("private-canary"));
        var refusal = Assert.Throws<WorkerRefusal>(() => broken.ObserveFile(PathOf("x"), true));
        Assert.DoesNotContain("canary", refusal.Message);
    }

    private CandidateClosureObservation Create(Func<string, byte[]?>? fingerprint = null) =>
        new(fingerprint ?? (path => files.GetValueOrDefault(path)), _ => packages);

    [Fact]
    public void Reader_domain_failures_are_reclassified_without_private_diagnostics()
    {
        var observer = Create(_ => throw WorkerRefusal.Resolution("legacy-loader", "private-reader-canary", ["canary"]));
        var refusal = Assert.Throws<WorkerRefusal>(() => observer.ObserveFile(PathOf("x"), true));
        Assert.Equal("candidate-host-unavailable", refusal.Code);
        Assert.DoesNotContain("canary", refusal.Message);
        Assert.Empty(refusal.Details);
    }

    [Fact]
    public void Malformed_private_package_metadata_has_a_fixed_domain_refusal()
    {
        var observer = Create();
        var refusal = Assert.Throws<WorkerRefusal>(() => observer.ObservePackages(
            [Package("A", "1", "a") with { FeedName = null! }], []));
        Assert.Equal("candidate-host-unavailable", refusal.Code);
    }

    private string PathOf(string name) => Path.Join(root, name);
    private InstalledPackage Package(string id, string version, string path) => new(id, version, PathOf(path), "feed", DateTimeOffset.UnixEpoch);
    private void Set(string name, string content) => files[PathOf(name)] = Hash(content);
    private static byte[] Hash(string content) => SHA256.HashData(Encoding.UTF8.GetBytes(content));
}
