using Elsa.Modularity.Planning.Bridge;
using System.Text;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class PortableCompositionCaptureTests
{
    [Fact]
    public void Inputs_are_frozen_before_context_is_bound_and_the_bundle_is_bound_once()
    {
        using var fixture = new CaptureFixture();
        using var capture = PortableCompositionCapture.OpenInputs(fixture.ExplicitInputs);
        var originalEnvelope = capture.ReadBytes(fixture.Envelope);
        var originalReceipt = capture.ReadBytes(fixture.Receipt);
        Assert.Equal("portable-input-invalid", Assert.Throws<CliRefusal>(capture.VerifyUnchanged).Code);

        var snapshot = capture.BindPrivateBundle(fixture.Host, "default", "Production");

        Assert.Equal(fixture.SupportedHostNames.Order(StringComparer.Ordinal), snapshot.FileNames.Order(StringComparer.Ordinal));
        Assert.Equal(originalEnvelope, capture.ReadBytes(fixture.Envelope));
        Assert.Equal(originalReceipt, capture.ReadBytes(fixture.Receipt));
        Assert.Equal(Encoding.UTF8.GetBytes("private shells"), snapshot.CopyBytes("shells.json"));
        Assert.Same(snapshot, capture.PrivateBundleSnapshot);

        var refusal = Assert.Throws<CliRefusal>(() => capture.BindPrivateBundle(fixture.Host, "other", "Production"));
        Assert.Equal("portable-input-invalid", refusal.Code);
        Assert.DoesNotContain(fixture.Host, refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("mutate")]
    public void Recheck_detects_every_supported_bundle_inventory_change(string change)
    {
        using var fixture = new CaptureFixture();
        using var capture = PortableCompositionCapture.OpenInputs(fixture.ExplicitInputs);
        capture.BindPrivateBundle(fixture.Host, "default", "Production");

        fixture.ChangeSupportedBundle(change);

        var refusal = Assert.Throws<CliRefusal>(capture.VerifyUnchanged);
        Assert.Equal("bridge-source-changed", refusal.Code);
        Assert.DoesNotContain(fixture.Host, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private-canary", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recheck_maps_explicit_input_drift_to_the_portable_drift_code_and_keeps_frozen_bytes()
    {
        using var fixture = new CaptureFixture();
        using var capture = PortableCompositionCapture.OpenInputs(fixture.ExplicitInputs);
        capture.BindPrivateBundle(fixture.Host, "default", "Production");
        var original = capture.ReadBytes(fixture.Envelope);
        File.WriteAllText(fixture.Envelope, "changed public bytes");

        Assert.Equal(original, capture.ReadBytes(fixture.Envelope));
        var refusal = Assert.Throws<CliRefusal>(capture.VerifyUnchanged);
        Assert.Equal("bridge-source-changed", refusal.Code);
        Assert.DoesNotContain("composition-input-changed", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Envelope, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Supplied_files_and_private_bundle_have_independent_file_collection_bounds()
    {
        using var fixture = new CaptureFixture();
        foreach (var path in fixture.ExplicitInputs)
            File.Delete(path);
        fixture.ExplicitInputs.Clear();
        for (var i = 0; i < CompositionFileReader.MaximumFiles; i++)
        {
            var path = Path.Join(fixture.Root.Path, $"input-{i}.json");
            File.WriteAllText(path, "{}");
            fixture.ExplicitInputs.Add(path);
        }

        for (var i = 0; i < 27; i++)
            File.WriteAllText(Path.Join(fixture.Host, $"shells.Env{i}.json"), "{}");

        var reader = new CompositionFileReader(_ => { }, File.OpenRead);
        using var capture = PortableCompositionCapture.OpenInputs(fixture.ExplicitInputs, reader);
        var snapshot = capture.BindPrivateBundle(fixture.Host, "default", "Production");

        Assert.Equal(CompositionFileReader.MaximumFiles, fixture.ExplicitInputs.Count);
        Assert.Equal(CompositionFileReader.MaximumFiles, snapshot.FileNames.Length);
        capture.VerifyUnchanged();
    }

    private sealed class CaptureFixture : IDisposable
    {
        public CaptureFixture()
        {
            Host = Path.Join(Root.Path, "host");
            Directory.CreateDirectory(Host);
            File.WriteAllText(Path.Join(Host, "shells.json"), "private shells");
            File.WriteAllText(Path.Join(Host, "appsettings.json"), "private settings");
            File.WriteAllText(Path.Join(Host, "shells.Production.json"), "selected shell overlay");
            File.WriteAllText(Path.Join(Host, "shells.Staging.json"), "unselected shell overlay");
            File.WriteAllText(Path.Join(Host, "appsettings.Staging.json"), "unselected settings overlay");
            File.WriteAllText(Path.Join(Host, "notes.txt"), "unsupported sibling");

            Envelope = Path.Join(Root.Path, "composition.json");
            Receipt = Path.Join(Root.Path, "receipt.json");
            var draft = Path.Join(Root.Path, "draft.json");
            var review = Path.Join(Root.Path, "review.json");
            var catalog = Path.Join(Root.Path, "catalog.json");
            var profile = Path.Join(Root.Path, "profile.json");
            var overlay = Path.Join(Root.Path, "intended-overlay.json");
            ExplicitInputs = [Envelope, Receipt, draft, review, catalog, profile, overlay];
            foreach (var (path, text) in ExplicitInputs.Select((path, index) => (path, $"input {index}")))
                File.WriteAllText(path, text);
        }

        public TempDirectory Root { get; } = new("elsa-portable-capture-");
        public string Host { get; }
        public string Envelope { get; }
        public string Receipt { get; }
        public List<string> ExplicitInputs { get; }
        public IReadOnlyList<string> SupportedHostNames =>
        ["appsettings.Staging.json", "appsettings.json", "shells.Production.json", "shells.Staging.json", "shells.json"];

        public void ChangeSupportedBundle(string change)
        {
            var selected = Path.Join(Host, "shells.Staging.json");
            var replacement = Path.Join(Host, "shells.New.json");
            switch (change)
            {
                case "add":
                    File.WriteAllText(replacement, "new supported sibling");
                    break;
                case "delete":
                    File.Delete(selected);
                    break;
                case "rename":
                    File.Move(selected, replacement);
                    break;
                case "mutate":
                    File.WriteAllText(selected, "changed private canary");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        public void Dispose() => Root.Dispose();
    }
}
