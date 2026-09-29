using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.NuplaneHostSettings;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps the major version each host declares for a shared Elsa assembly equal to the major that assembly is built with,
/// in a source build and in a build with computed package versions alike (#2150).
/// </summary>
/// <remarks>
/// <para>
/// Nuplane resolves a package's reference to the host's copy of a shared assembly only when a
/// <c>Nuplane:Loading:SharedAssemblies</c> entry matches the reference, and its matcher compares the entry's
/// <c>MajorVersion</c> with the referenced assembly version's major exactly. An entry naming another major never matches,
/// and nothing says so: a package that carries its own copy of the assembly loads that copy, and the types the host and
/// the package exchange through it stop being the same types. Every Elsa entry once declared 0 while Elsa assemblies were
/// built as 1.0.0.0 from source and 4.x with computed versions.
/// </para>
/// <para>
/// <c>PackageVersioning.props</c> now gives every Elsa assembly its line's major as its assembly version, whichever way it
/// is built, and fails a build that overrides it (ELSAPV008). This guard reads what the SDK really computes for each shared
/// project, both ways, and holds each host's declarations to it, so a declaration and a version line cannot move apart.
/// A shared assembly with no project under <c>src/</c> (the <c>CShells.*</c> contracts) is not examined.
/// </para>
/// </remarks>
public sealed class SharedAssemblyMajorVersionGuardTests
{
    /// <summary>Every Elsa project under <c>src/</c>, by assembly name, with its project file.</summary>
    private static IReadOnlyDictionary<string, string> ElsaProjects { get; } =
        ProjectGraph.ElsaProjectPaths(RepoRoot).ToDictionary(path => Path.GetFileNameWithoutExtension(path)!, Path.GetFullPath, StringComparer.OrdinalIgnoreCase);

    /// <summary>The Elsa assemblies any host shares, each with the assembly version a source build gives it.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, Version>> SourceBuild = new(() => BuiltAssemblyVersions(computed: false));

    /// <summary>The same assemblies, each with the assembly version a build with computed package versions gives it.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, Version>> ComputedBuild = new(() => BuiltAssemblyVersions(computed: true));

    public static TheoryData<string> Hosts => [.. HostsThatShareAssemblies()];

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Every_elsa_share_declares_the_major_its_assembly_is_built_with(string host)
    {
        var declared = DeclaredElsaMajors(host);

        Assert.NotEmpty(declared);
        Assert.True(Mismatches(declared, SourceBuild.Value).Count == 0, Report(host, "a source build", Mismatches(declared, SourceBuild.Value)));
        Assert.True(Mismatches(declared, ComputedBuild.Value).Count == 0, Report(host, "a build with computed versions", Mismatches(declared, ComputedBuild.Value)));
    }

    /// <summary>
    /// The theory above compares only what both sides name, so this pins that the evaluation reported a version for every
    /// Elsa share of every host, and that each is the major of the line <c>VersionLines.props</c> puts its project on.
    /// </summary>
    [Fact]
    public void Every_elsa_share_is_evaluated_and_carries_its_lines_major()
    {
        var shared = HostsThatShareAssemblies().SelectMany(host => DeclaredElsaMajors(host).Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lineA = VersionLines.LineAMembers(RepoRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Elsa.Primitives", shared);
        Assert.Contains("Elsa.Persistence.Schema", shared);
        Assert.All(shared, name =>
        {
            var expected = new Version(LineMajor(lineA.Contains(name) ? "ElsaContractsVersion" : "ElsaVersion"), 0, 0, 0);
            Assert.Equal(expected, SourceBuild.Value.GetValueOrDefault(name));
            Assert.Equal(expected, ComputedBuild.Value.GetValueOrDefault(name));
        });
    }

    /// <summary>The detector itself, so the theory's green means "every major matches" rather than "nothing compared".</summary>
    [Fact]
    public void A_share_declaring_another_major_than_its_assembly_is_flagged()
    {
        var built = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Primitives"] = new(4, 0, 0, 0),
            ["Elsa.Caching.Core"] = new(4, 0, 0, 0)
        };

        Assert.Equal(
            ["Elsa.Caching.Core declares no major; its assembly is 4.0.0.0", "Elsa.Primitives declares major 0; its assembly is 4.0.0.0",
             "Elsa.Tasks.Core declares major 4; its assembly was not evaluated"],
            Mismatches(new Dictionary<string, int?> { ["Elsa.Primitives"] = 0, ["Elsa.Caching.Core"] = null, ["Elsa.Tasks.Core"] = 4 }, built));
        Assert.Empty(Mismatches(new Dictionary<string, int?> { ["Elsa.Primitives"] = 4, ["elsa.caching.core"] = 4 }, built));
    }

    /// <summary>Each Elsa share of <paramref name="host"/> that has a project under <c>src/</c>, with the major it declares, if any.</summary>
    private static IReadOnlyDictionary<string, int?> DeclaredElsaMajors(string host) =>
        ReadNuplane(host).GetSection("Loading:SharedAssemblies").GetChildren()
            .Where(entry => entry["Name"] is { } name && ElsaProjects.ContainsKey(name))
            .ToDictionary(
                entry => entry["Name"]!,
                entry => int.TryParse(entry["MajorVersion"], out var major) ? major : (int?)null,
                StringComparer.OrdinalIgnoreCase);

    /// <summary>Every declaration whose major is not the major of the assembly version its project is built with.</summary>
    private static IReadOnlyList<string> Mismatches(IReadOnlyDictionary<string, int?> declared, IReadOnlyDictionary<string, Version> built) =>
    [
        .. declared
            .Select(entry => (Name: entry.Key, Declared: entry.Value, Built: built.GetValueOrDefault(entry.Key)))
            .Where(entry => entry.Built is null || entry.Declared != entry.Built.Major)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .Select(entry =>
                $"{entry.Name} declares {(entry.Declared is { } major ? $"major {major}" : "no major")}; " +
                $"its assembly {(entry.Built is null ? "was not evaluated" : $"is {entry.Built}")}")
    ];

    private static string Report(string host, string build, IReadOnlyList<string> mismatches) =>
        $"{host}'s Nuplane:Loading:SharedAssemblies (src/apps/{host}/appsettings.json) declares majors that {build} does not " +
        "build its Elsa assemblies with. Nuplane's matcher compares the major exactly, so each such share would never match, " +
        "and a package carrying its own copy of the assembly would load that copy instead of the host's (#2150):" +
        string.Concat(mismatches.Select(line => $"{Environment.NewLine}  {line}"));

    /// <summary>The major of a line's <c>major.minor</c> in <c>VersionLines.props</c>.</summary>
    private static int LineMajor(string property) =>
        Version.Parse(XDocument.Load(Path.Join(RepoRoot, "VersionLines.props")).Descendants(property).Single().Value.Trim()).Major;

    /// <summary>
    /// The assembly version the SDK computes for the project of every Elsa assembly a host shares, as a build of the
    /// repository evaluates it: one MSBuild invocation runs the SDK's own <c>GetAssemblyVersion</c> target in each project,
    /// with no build input, or with a pack-properties file shaped as the calculator writes it.
    /// </summary>
    private static IReadOnlyDictionary<string, Version> BuiltAssemblyVersions(bool computed)
    {
        var projects = HostsThatShareAssemblies().SelectMany(host => DeclaredElsaMajors(host).Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => ElsaProjects[name])
            .Order(StringComparer.Ordinal);

        var scratch = Directory.CreateTempSubdirectory(nameof(SharedAssemblyMajorVersionGuardTests));
        try
        {
            var report = Path.Join(scratch.FullName, "report.targets");
            File.WriteAllText(report, """
                <Project>
                  <Target Name="ElsaReportAssemblyVersion" DependsOnTargets="GetAssemblyVersion" Returns="@(_ElsaReportedAssemblyVersion)">
                    <ItemGroup>
                      <_ElsaReportedAssemblyVersion Include="$(AssemblyName)" AssemblyVersion="$(AssemblyVersion)" />
                    </ItemGroup>
                  </Target>
                </Project>
                """);

            var traversal = Path.Join(scratch.FullName, "shares.proj");
            File.WriteAllText(traversal, $"""
                <Project>
                  <ItemGroup>
                {string.Concat(projects.Select(project => $"    <Share Include=\"{project}\" />{Environment.NewLine}"))}  </ItemGroup>
                  <Target Name="Report" Returns="@(Reported)">
                    <MSBuild Projects="@(Share)" Targets="ElsaReportAssemblyVersion" BuildInParallel="true">
                      <Output TaskParameter="TargetOutputs" ItemName="Reported" />
                    </MSBuild>
                  </Target>
                </Project>
                """);

            // Any computed version will do: the assembly version must be the line's major whatever the package version is.
            var versions = Path.Join(scratch.FullName, "package-versions.props");
            File.WriteAllText(versions, """
                <Project>
                  <PropertyGroup>
                    <ElsaVersionComputationCommit>0123456789abcdef0123456789abcdef01234567</ElsaVersionComputationCommit>
                    <ElsaComputedPackageVersion>4.0.9-preview</ElsaComputedPackageVersion>
                    <ElsaInputFingerprint>sha256:0000000000000000000000000000000000000000000000000000000000000000</ElsaInputFingerprint>
                  </PropertyGroup>
                </Project>
                """);

            var result = Path.Join(scratch.FullName, "result.json");
            var (exitCode, output) = ChildProcess.Dotnet(
            [
                "msbuild", traversal, "-t:Report", "-getTargetResult:Report", $"-getResultOutputFile:{result}", "-nologo", "-nodeReuse:false",
                $"-p:CustomAfterMicrosoftCommonTargets={report}",
                .. computed ? [$"-p:CustomBeforeDirectoryBuildProps={versions}"] : Array.Empty<string>()
            ], timeout: TimeSpan.FromMinutes(5));
            Assert.True(exitCode == 0, $"evaluating the shared Elsa projects failed with exit {exitCode}:\n{output}");

            using var document = JsonDocument.Parse(File.ReadAllText(result));
            return document.RootElement.GetProperty("TargetResults").GetProperty("Report").GetProperty("Items").EnumerateArray()
                .ToDictionary(
                    item => item.GetProperty("Identity").GetString()!,
                    item => Version.Parse(item.GetProperty("AssemblyVersion").GetString()!),
                    StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }
}
