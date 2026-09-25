using Xunit;
using Elsa.Modularity.Planning.Bridge;
using Elsa.Modularity.Planning.Json;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Cli.Tests;

public sealed class CompositionFileSourceTests
{
    [Fact]
    public void Open_freezes_supported_siblings_and_rechecks_unselected_files()
    {
        using var fixture = new LocalFixture();
        var source = CompositionFileSource.Open(fixture.Directory, "default", "Production");

        Assert.Equal(6, source.Snapshot.FileNames.Length);
        Assert.Contains("shells.Staging.json", source.Snapshot.FileNames);
        source.VerifyUnchanged();

        File.AppendAllText(Path.Join(fixture.Directory, "shells.Staging.json"), " ");
        var refusal = Assert.Throws<CliRefusal>(source.VerifyUnchanged);
        Assert.Equal("bridge-source-changed", refusal.Code);
    }

    [Fact]
    public void Open_rechecks_the_selected_overlay_after_preview()
    {
        using var fixture = new LocalFixture();
        var source = CompositionFileSource.Open(fixture.Directory, "default", "Production");

        File.AppendAllText(Path.Join(fixture.Directory, "shells.Production.json"), " ");

        var refusal = Assert.Throws<CliRefusal>(source.VerifyUnchanged);
        Assert.Equal("bridge-source-changed", refusal.Code);
    }

    [Fact]
    public void Open_requires_the_named_shell_overlay_without_echoing_the_host_path()
    {
        using var fixture = new LocalFixture();

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFileSource.Open(fixture.Directory, "default", "Absent"));

        Assert.Equal("bridge-source-missing", refusal.Code);
        Assert.DoesNotContain(fixture.Directory, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_rejects_a_symbolic_link_in_the_supported_bundle()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new LocalFixture();
        var link = Path.Join(fixture.Directory, "shells.Staging.json");
        File.Delete(link);
        File.CreateSymbolicLink(link, Path.Join(fixture.Directory, "shells.Production.json"));

        var refusal = Assert.Throws<CliRefusal>(() => CompositionFileSource.Open(fixture.Directory, "default", "Production"));

        Assert.Equal("bridge-source-unreadable", refusal.Code);
    }

    [Fact]
    public void Disposable_workbench_files_import_without_portable_setting_values_or_host_activation()
    {
        using var fixture = new LocalFixture();
        fixture.UseWorkbenchFiles();
        var source = CompositionFileSource.Open(fixture.Directory, "default", "Production");
        var snapshot = source.Snapshot;
        var draft = new SelectionCatalog("1", "workbench-compatibility", "1", "elsa-foundation", new string('0', 64), [], []);
        var catalog = draft with { Digest = SelectionDigest.ComputeCatalogDigest(draft) };

        var imported = CompositionImporter.Import(
            snapshot.ReadText("shells.json"),
            snapshot.ReadText(snapshot.Selection.ShellOverlayFileName),
            snapshot.ReadText("appsettings.json"),
            snapshot.Selection.AppsettingsOverlayFileName is { } appOverlay ? snapshot.ReadText(appOverlay) : null,
            "default", "Production", catalog);

        Assert.NotEmpty(imported.Authored.Add);
        Assert.Null(imported.Authored.Settings);
        Assert.Contains("shells.baseline.json", snapshot.FileNames);
        Assert.All(imported.Preview.Settings, setting => Assert.Null(setting.PortableValue));
        source.VerifyUnchanged();
    }

    private sealed class LocalFixture : IDisposable
    {
        public string Directory { get; } = Path.Join(Path.GetTempPath(), "elsa-composition-source-" + Guid.NewGuid().ToString("N"));

        public LocalFixture()
        {
            System.IO.Directory.CreateDirectory(Directory);
            var source = Path.Join(RepoRoot, "tests", "essentials", "Cli", "Tests", "Fixtures", "CompositionBridge");
            foreach (var file in System.IO.Directory.GetFiles(source, "*.json"))
                File.Copy(file, Path.Join(Directory, Path.GetFileName(file)));
        }

        public void UseWorkbenchFiles()
        {
            foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json"))
                File.Delete(file);
            var workbench = Path.Join(RepoRoot, "src", "apps", "Elsa.Workbench");
            foreach (var file in System.IO.Directory.GetFiles(workbench, "*.json"))
                if (Path.GetFileName(file).StartsWith("shells", StringComparison.Ordinal) ||
                    Path.GetFileName(file).StartsWith("appsettings", StringComparison.Ordinal))
                    File.Copy(file, Path.Join(Directory, Path.GetFileName(file)));
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);

        private static string RepoRoot
        {
            get
            {
                var directory = new DirectoryInfo(AppContext.BaseDirectory);
                while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                    directory = directory.Parent;
                return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
            }
        }
    }
}
