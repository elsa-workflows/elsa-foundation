using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>Disposable source and supplied-input files for candidate inspection against a real host closure.</summary>
internal sealed class CandidateInspectionFixture : IDisposable
{
    public const string ResourceProbeFeatureId = "ResourceProbe";
    public const string ResourceProbeModuleId = "Acme.ResourceProbe";

    private readonly TempDirectory _directory = new("elsa-candidate-inspection-");
    private readonly Dictionary<string, byte[]> _initialInputBytes = new(StringComparer.Ordinal);

    public CandidateInspectionFixture()
    {
        HostAssemblyDirectory = DotnetElsa.Host("ResourceAwareLiveHost");
        SourceDirectory = _directory.File("source");
        InputDirectory = _directory.File("inputs");
        Directory.CreateDirectory(SourceDirectory);
        Directory.CreateDirectory(InputDirectory);

        File.WriteAllText(Path.Join(SourceDirectory, "shells.json"), """
            {"CShells":{"Shells":{"default":{"Name":"default","Features":{"ResourceProbe":{}}}}}}
            """);
        File.WriteAllText(Path.Join(SourceDirectory, "shells.Production.json"), """
            {"CShells":{"Shells":{"default":{"Configuration":{"Elsa":{"Persistence":{"Bindings":{"ResourceProbe":"primary"}}}}}}}}
            """);
        File.WriteAllText(Path.Join(SourceDirectory, "appsettings.json"), """
            {"ConnectionStrings":{"Probe":"Data Source=:memory:"},"Elsa":{"Persistence":{"DefaultResource":"primary","Resources":{"primary":{"Provider":"Sqlite","ConnectionName":"Probe"}}}}}
            """);
        File.WriteAllText(Path.Join(SourceDirectory, "appsettings.Production.json"), "{}");
    }

    /// <summary>The compiled host closure passed to the CLI's --host option; source files live separately.</summary>
    public string HostAssemblyDirectory { get; }

    public string SourceDirectory { get; }

    public string InputDirectory { get; }

    public string ShellId => "default";

    public string Environment => "Production";

    public string InputPath(string name)
    {
        if (Path.GetFileName(name) != name)
            throw new ArgumentException("An input fixture name must be a file name.", nameof(name));
        return Path.Join(InputDirectory, name);
    }

    public void WriteInput(string name, string contents)
    {
        var path = InputPath(name);
        File.WriteAllText(path, contents);
        _initialInputBytes[Path.GetFullPath(path)] = File.ReadAllBytes(path);
    }

    /// <summary>Captures all supported source files, including siblings that must remain unchanged.</summary>
    public CompositionFileSource CaptureSource() => CompositionFileSource.Open(SourceDirectory, ShellId, Environment);

    /// <summary>Asserts that authored/catalog/profile fixture inputs were not changed by an inspection journey.</summary>
    public void AssertInputsUnchanged()
    {
        foreach (var (path, expected) in _initialInputBytes)
            Assert.True(File.Exists(path) && expected.AsSpan().SequenceEqual(File.ReadAllBytes(path)),
                "A supplied candidate-inspection input changed during the test journey.");
    }

    public void Dispose() => _directory.Dispose();
}
