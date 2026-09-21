using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// The deps file is the only statement of what a host pins, and the manifest's package facts are built from
/// it (FR-049). Reading the wrong target, or mapping an assembly to the wrong package, would put a version
/// in a deployment manifest that the host never loads.
/// </summary>
public sealed class HostDepsFileTests : IDisposable
{
    private readonly TempDirectory directory = new("elsa-cli-deps-");

    public void Dispose() => directory.Dispose();

    [Fact]
    public void An_assembly_maps_to_the_package_whose_runtime_asset_carries_it()
    {
        var deps = Read("""
            {
              "runtimeTarget": { "name": "net10.0" },
              "targets": {
                "net10.0": {
                  "Contoso.Widgets/2.1.0": { "runtime": { "lib/net10.0/Contoso.Widgets.dll": {} } }
                }
              }
            }
            """);

        Assert.Equal(new PackageFacts("Contoso.Widgets", "2.1.0", "host-deps-file"), deps.ForAssembly("Contoso.Widgets"));
        Assert.Equal(new PackageFacts("Contoso.Widgets", "2.1.0", "host-deps-file"), deps.ForPackage("contoso.widgets"));
        Assert.Null(deps.ForAssembly("Contoso.Other"));
    }

    /// <summary>
    /// A published, runtime-identifier-specific app carries two targets; the runtime picks the one
    /// <c>runtimeTarget</c> names, and so must this, or the manifest records versions the host never loads.
    /// </summary>
    [Fact]
    public void The_target_the_runtime_would_use_is_the_one_read()
    {
        var deps = Read("""
            {
              "runtimeTarget": { "name": "net10.0/osx-arm64" },
              "targets": {
                "net10.0": { "Contoso.Widgets/1.0.0": { "runtime": { "lib/net10.0/Contoso.Widgets.dll": {} } } },
                "net10.0/osx-arm64": { "Contoso.Widgets/2.0.0": { "runtime": { "lib/net10.0/Contoso.Widgets.dll": {} } } }
              }
            }
            """);

        Assert.Equal("2.0.0", deps.ForAssembly("Contoso.Widgets")!.Version);
    }

    [Fact]
    public void A_package_with_no_runtime_asset_is_still_resolvable_by_package_id()
    {
        var deps = Read("""
            {
              "targets": { "net10.0": { "Contoso.Analyzers/3.0.0": {} } }
            }
            """);

        Assert.Equal("3.0.0", deps.ForPackage("Contoso.Analyzers")!.Version);
        Assert.Empty(deps.AssemblyNames);
    }

    [Theory]
    [InlineData("{}", "host-deps-file-invalid")]
    [InlineData("""{"targets":{}}""", "host-deps-file-invalid")]
    [InlineData("not json", "host-deps-file-invalid")]
    public void A_deps_file_this_build_cannot_read_is_refused_rather_than_read_as_empty(string content, string code)
    {
        Assert.Equal(code, Assert.Throws<WorkerRefusal>(() => Read(content)).Code);
    }

    [Fact]
    public void A_missing_deps_file_is_refused_by_path()
    {
        Assert.Equal("host-deps-file-unreadable", Assert.Throws<WorkerRefusal>(() => HostDepsFile.Read(directory.File("nowhere.deps.json"))).Code);
    }

    private HostDepsFile Read(string content)
    {
        var path = directory.File("Contoso.Host.deps.json");
        File.WriteAllText(path, content);
        return HostDepsFile.Read(path);
    }
}
