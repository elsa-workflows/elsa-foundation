using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Runtime.Bindings;

namespace Elsa.Etl.Tests;

public sealed class HostFileBindingsTests : IDisposable
{
    private static readonly EtlScope Scope = new("tenant-a", "partition-a");
    private readonly string _root = Path.Combine(Path.GetTempPath(), "elsa-etl-bindings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Pins_original_output_revision_when_current_alias_is_retargeted()
    {
        var original = Output("v1", Path.Combine(_root, "original"));
        var pin = new HostFileBindings([original]).Resolve(Scope, "output", EtlFileRole.MainOutput);
        var registry = new HostFileBindings([original with { IsCurrent = false }, Output("v2", Path.Combine(_root, "new"))]);

        Assert.Equal(original.Path, registry.GetOutputRoot(pin));
        Assert.Equal("v2", registry.Resolve(Scope, "output", EtlFileRole.MainOutput).Revision);
        // Even reusing a revision name cannot redirect its retained opaque handle.
        var replaced = new HostFileBindings([original with { Path = Path.Combine(_root, "replacement") }]);
        Assert.Equal("etl.binding-unavailable", Assert.Throws<EtlBindingException>(() => replaced.GetOutputRoot(pin)).Code);
        Assert.DoesNotContain(original.Path, JsonSerializer.Serialize(pin), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Input_revisions_have_expected_content_identity_and_open_only_in_their_own_scope()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "source.csv");
        var bytes = "id\n1\n"u8.ToArray();
        await File.WriteAllBytesAsync(path, bytes);
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var binding = new HostFileBinding(Scope, "source", EtlFileRole.Source, "v1", path, true, expected, ImmutableInput: true);
        var registry = new HostFileBindings([binding]);
        var pin = registry.Resolve(Scope, "source", EtlFileRole.Source);
        using var input = registry.OpenInput(pin);
        using var copy = new MemoryStream();
        await input.CopyToAsync(copy);

        Assert.Equal(bytes, copy.ToArray());
        Assert.Equal(expected, pin.ExpectedSha256);
        Assert.Throws<EtlBindingException>(() => registry.OpenInput(pin with { Scope = new EtlScope("tenant-b", "partition-a") }));
        Assert.Throws<EtlBindingException>(() => registry.Resolve(new EtlScope("tenant-a", "partition-b"), "source", EtlFileRole.Source));
        Assert.Throws<EtlBindingException>(() => registry.Resolve(Scope, "source", EtlFileRole.Lookup));
    }

    [Theory]
    [InlineData("../path")]
    [InlineData("/absolute")]
    [InlineData("C:\\source")]
    [InlineData("with space")]
    public void Authored_paths_cannot_become_aliases(string alias)
    {
        var error = Assert.Throws<EtlBindingException>(() => new HostFileBindings([Output("v1", _root) with { Alias = alias }]));
        Assert.Equal("etl.binding-invalid", error.Code);
        Assert.DoesNotContain(alias, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_mutable_inputs_missing_digests_or_nonatomic_outputs()
    {
        var input = new HostFileBinding(Scope, "source", EtlFileRole.Source, "v1", _root, true);
        Assert.Equal("etl.binding-input-not-immutable", Assert.Throws<EtlBindingException>(() => new HostFileBindings([input])).Code);
        Assert.Throws<EtlBindingException>(() => new HostFileBindings([input with { ImmutableInput = true, ExpectedSha256 = "wrong" }]));
        Assert.Throws<EtlBindingException>(() => new HostFileBindings([Output("v1", _root) with { AtomicPublication = false }]));
    }

    [Fact]
    public void Listings_are_scope_filtered_and_contain_only_safe_alias_and_role_metadata()
    {
        var registry = new HostFileBindings([Output("v1", _root), Output("v1", _root) with { Scope = new EtlScope("other", "partition-a"), Alias = "hidden" }]);
        var metadata = Assert.Single(registry.List(Scope));
        Assert.Equal("output", metadata.Alias);
        Assert.Equal(EtlFileRole.MainOutput, metadata.Role);
        Assert.DoesNotContain(_root, JsonSerializer.Serialize(metadata), StringComparison.Ordinal);
        Assert.Empty(registry.List(new EtlScope("tenant-a", "other")));
    }

    [Fact]
    public void Duplicate_current_aliases_or_revision_identities_are_refused()
    {
        var output = Output("v1", _root);
        Assert.Equal("etl.binding-ambiguous", Assert.Throws<EtlBindingException>(() => new HostFileBindings([output, output with { Revision = "v2" }])).Code);
        Assert.Throws<EtlBindingException>(() => new HostFileBindings([output, output with { IsCurrent = false }]));
    }

    private static HostFileBinding Output(string revision, string path) =>
        new(Scope, "output", EtlFileRole.MainOutput, revision, path, IsCurrent: true, AtomicPublication: true);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
