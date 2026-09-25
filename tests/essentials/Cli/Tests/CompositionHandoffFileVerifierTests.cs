using System.Text;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CompositionHandoffFileVerifierTests
{
    [Fact]
    public void Accepts_only_the_exact_published_candidate_bytes()
    {
        using var fixture = new Fixture();

        CompositionHandoffFileVerifier.Verify(fixture.Directory, fixture.Files);

        File.AppendAllText(Path.Join(fixture.Directory, "shells.json"), "changed");
        var refusal = Assert.Throws<CliRefusal>(() => CompositionHandoffFileVerifier.Verify(fixture.Directory, fixture.Files));
        Assert.Equal("candidate-changed", refusal.Code);
        Assert.DoesNotContain("CONNECTION_CANARY", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("directory")]
    public void Refuses_an_incomplete_or_extra_published_entry(string mutation)
    {
        using var fixture = new Fixture();
        switch (mutation)
        {
            case "missing": File.Delete(Path.Join(fixture.Directory, "appsettings.json")); break;
            case "extra": File.WriteAllText(Path.Join(fixture.Directory, "extra.json"), "{}"); break;
            case "directory": Directory.CreateDirectory(Path.Join(fixture.Directory, "extra.json")); break;
        }

        var refusal = Assert.Throws<CliRefusal>(() => CompositionHandoffFileVerifier.Verify(fixture.Directory, fixture.Files));

        Assert.Equal("candidate-changed", refusal.Code);
        Assert.DoesNotContain(fixture.Directory, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_published_symlink()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var fixture = new Fixture();
        var path = Path.Join(fixture.Directory, "shells.json");
        File.Delete(path);
        File.CreateSymbolicLink(path, Path.Join(fixture.Directory, "appsettings.json"));

        var refusal = Assert.Throws<CliRefusal>(() => CompositionHandoffFileVerifier.Verify(fixture.Directory, fixture.Files));

        Assert.Equal("candidate-changed", refusal.Code);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDirectory _temp = new("elsa-composition-handoff-");

        public Fixture()
        {
            Directory = _temp.File("candidate");
            System.IO.Directory.CreateDirectory(Directory);
            foreach (var (name, bytes) in Files)
                File.WriteAllBytes(Path.Join(Directory, name), bytes);
        }

        public string Directory { get; }
        public IReadOnlyDictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["shells.json"] = Encoding.UTF8.GetBytes("{}"),
            ["appsettings.json"] = Encoding.UTF8.GetBytes("CONNECTION_CANARY")
        };

        public void Dispose() => _temp.Dispose();
    }
}
