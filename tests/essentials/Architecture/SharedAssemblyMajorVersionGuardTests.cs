using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Elsa.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Nuplane.Loading;
using Xunit;
using static Elsa.Architecture.Tests.NuplaneHostSettings;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Holds every Elsa share of every host to what Nuplane itself does with it (#2150): each host's
/// <c>Nuplane:Loading:SharedAssemblies</c>, bound and validated by Nuplane as the host binds it, must take, under Nuplane's
/// own matcher, the reference a package built against this repository carries to each shared Elsa assembly, in a source
/// build and in a build with computed package versions alike.
/// </summary>
/// <remarks>
/// <para>
/// Nuplane resolves a package's reference to the host's copy of an assembly only when an entry matches it on name, public
/// key token and major version, the major exactly. An entry that does not match says nothing: a package that carries its
/// own copy of the assembly loads that copy, and the types the host and the package exchange through it stop being the same
/// types. Every Elsa entry once declared major 0 while Elsa assemblies were built as 1.0.0.0 from source and 4.x with
/// computed versions, and before 0.0.11-preview.94 Nuplane dropped every entry whose token was null while binding, so none
/// ever matched.
/// </para>
/// <para>
/// <c>PackageVersioning.props</c> gives every Elsa assembly its line's major as its assembly version, whichever way it is
/// built, and fails a build that overrides it (ELSAPV008). This guard reads what the SDK really computes for each shared
/// project, both ways, and asks Nuplane's matcher whether each host's entries, as Nuplane binds them, take a reference to
/// that version: a source build's from the host's <c>appsettings.json</c>, a computed build's with the
/// <c>ComputedVersions/appsettings.Production.json</c> it ships over it. A shared assembly with no project under
/// <c>src/</c> (the <c>CShells.*</c> contracts) is not examined.
/// </para>
/// </remarks>
public sealed class SharedAssemblyMajorVersionGuardTests
{
    /// <summary>What a build with computed package versions ships as its <c>appsettings.Production.json</c>.</summary>
    private const string ComputedProductionSettings = $"{ComputedVersionsDirectory}/appsettings.Production.json";

    /// <summary>Every Elsa project under <c>src/</c>, by assembly name, with its project file.</summary>
    private static IReadOnlyDictionary<string, string> ElsaProjects { get; } =
        ProjectGraph.ElsaProjectPaths(RepoRoot).ToDictionary(path => Path.GetFileNameWithoutExtension(path)!, Path.GetFullPath, StringComparer.OrdinalIgnoreCase);

    /// <summary>The reference a package compiled against a source build carries to each Elsa assembly any host shares.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, AssemblyName>> SourceBuild = new(() => BuiltReferences(computed: false));

    /// <summary>The same references, for a package compiled against a build with computed package versions.</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, AssemblyName>> ComputedBuild = new(() => BuiltReferences(computed: true));

    public static TheoryData<string> Hosts => [.. HostsThatShareAssemblies()];

    [Theory]
    [MemberData(nameof(Hosts))]
    public void Every_elsa_share_takes_its_assemblys_reference_under_nuplanes_own_matcher(string host)
    {
        var source = NuplaneSharedAssemblyPolicy.Bind(ReadNuplane(host));
        var computed = NuplaneSharedAssemblyPolicy.Bind(ReadNuplane(host, ComputedProductionSettings));

        Assert.NotEmpty(ElsaShares(source));
        Assert.True(Unmatched(source, SourceBuild.Value).Count == 0, Report(host, "a source build", Unmatched(source, SourceBuild.Value)));
        Assert.True(Unmatched(computed, ComputedBuild.Value).Count == 0, Report(host, "a build with computed versions", Unmatched(computed, ComputedBuild.Value)));
    }

    /// <summary>
    /// The theory above compares only what both sides name, so this pins that the evaluation reported a reference for every
    /// Elsa share of every host, and that its version is the major of the line <c>VersionLines.props</c> puts its project on.
    /// </summary>
    [Fact]
    public void Every_elsa_share_is_evaluated_and_carries_its_lines_major()
    {
        var shared = HostsThatShareAssemblies().SelectMany(host => ElsaShares(NuplaneSharedAssemblyPolicy.Bind(ReadNuplane(host))))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lineA = VersionLines.LineAMembers(RepoRoot).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Elsa.Primitives", shared);
        Assert.Contains("Elsa.Persistence.Schema", shared);
        Assert.All(shared, name =>
        {
            var expected = new Version(LineMajor(lineA.Contains(name) ? "ElsaContractsVersion" : "ElsaVersion"), 0, 0, 0);
            Assert.Equal(expected, SourceBuild.Value.GetValueOrDefault(name)?.Version);
            Assert.Equal(expected, ComputedBuild.Value.GetValueOrDefault(name)?.Version);
        });
    }

    /// <summary>
    /// The detector itself, through Nuplane's own binding and matcher, so the theory's green means "every share matches"
    /// rather than "nothing compared": another major, a token an unsigned assembly does not have, and a share no evaluation
    /// reported are each flagged; a match is not, whatever case the entry spells the name in.
    /// </summary>
    [Fact]
    public void A_share_nuplanes_matcher_does_not_take_is_flagged()
    {
        var built = new Dictionary<string, AssemblyName>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Caching.Core"] = Reference("Elsa.Caching.Core", new(4, 0, 0, 0)),
            ["Elsa.Events.Core"] = Reference("Elsa.Events.Core", new(4, 0, 0, 0)),
            ["Elsa.Primitives"] = Reference("Elsa.Primitives", new(4, 0, 0, 0))
        };

        Assert.Equal(
            [
                "Elsa.Caching.Core, declared as major 4 with token 0123456789abcdef: the reference is Elsa.Caching.Core, Version=4.0.0.0, PublicKeyToken=null",
                "Elsa.Primitives, declared as major 0, unsigned: the reference is Elsa.Primitives, Version=4.0.0.0, PublicKeyToken=null",
                "Elsa.Tasks.Core, declared as major 4, unsigned: its project was not evaluated"
            ],
            Unmatched(Policy("""
                [
                  { "Name": "Elsa.Primitives", "PublicKeyToken": null, "MajorVersion": 0 },
                  { "Name": "Elsa.Caching.Core", "PublicKeyToken": "0123456789abcdef", "MajorVersion": 4 },
                  { "Name": "Elsa.Events.Core", "PublicKeyToken": "", "MajorVersion": 4 },
                  { "Name": "Elsa.Tasks.Core", "MajorVersion": 4 }
                ]
                """), built));
        Assert.Empty(Unmatched(Policy("""
            [
              { "Name": "Elsa.Primitives", "PublicKeyToken": null, "MajorVersion": 4 },
              { "Name": "elsa.caching.core", "MajorVersion": 4 }
            ]
            """), built));
    }

    /// <summary>
    /// An entry Nuplane cannot bind stops this guard exactly as it stops the host, naming the entry, rather than leaving
    /// the host with one share fewer than it lists.
    /// </summary>
    [Fact]
    public void A_share_nuplane_cannot_bind_fails_as_the_host_fails_to_start()
    {
        var refusal = Assert.Throws<OptionsValidationException>(() => Policy("""[ { "Name": "Elsa.Primitives", "PublicKeyToken": null } ]"""));

        Assert.Contains("'Nuplane:Loading:SharedAssemblies:0' could not be bound", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The Elsa assemblies with a project under <c>src/</c> that <paramref name="policy"/> has an entry for.</summary>
    private static IEnumerable<string> ElsaShares(NuplaneSharedAssemblyPolicy policy) =>
        policy.Entries.Select(entry => entry.Name).Where(ElsaProjects.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every Elsa share of <paramref name="policy"/> whose reference, as <paramref name="built"/> gives it, Nuplane's matcher does not take.</summary>
    private static IReadOnlyList<string> Unmatched(NuplaneSharedAssemblyPolicy policy, IReadOnlyDictionary<string, AssemblyName> built) =>
    [
        .. ElsaShares(policy)
            .Select(name => (Name: name, Reference: built.GetValueOrDefault(name)))
            .Where(share => share.Reference is null || !policy.Shares(share.Reference))
            .OrderBy(share => share.Name, StringComparer.OrdinalIgnoreCase)
            .Select(share =>
                $"{share.Name}, declared as " +
                string.Join(" and as ", policy.Entries.Where(entry => string.Equals(entry.Name, share.Name, StringComparison.OrdinalIgnoreCase)).Select(Describe)) +
                (share.Reference is null ? ": its project was not evaluated" : $": the reference is {share.Reference.FullName}"))
    ];

    private static string Describe(SharedAssemblyPolicyEntry entry) =>
        $"major {entry.MajorVersion}{(entry.PublicKeyToken.Length == 0 ? ", unsigned" : $" with token {entry.PublicKeyToken}")}";

    private static string Report(string host, string build, IReadOnlyList<string> unmatched) =>
        $"{host}'s Nuplane:Loading:SharedAssemblies (src/apps/{host}/appsettings.json) has Elsa shares that Nuplane's matcher " +
        $"does not take for the reference a package built against {build} carries. Such a share never matches, so a package " +
        "carrying its own copy of the assembly loads that copy instead of the host's (#2150):" +
        string.Concat(unmatched.Select(line => $"{Environment.NewLine}  {line}"));

    /// <summary><paramref name="entries"/>, a JSON array, as a host's <c>Nuplane:Loading:SharedAssemblies</c>, bound by Nuplane.</summary>
    private static NuplaneSharedAssemblyPolicy Policy(string entries) =>
        NuplaneSharedAssemblyPolicy.Bind(new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes($$"""{ "Nuplane": { "Loading": { "SharedAssemblies": {{entries}} } } }""")))
            .Build()
            .GetSection("Nuplane"));

    /// <summary>A reference to an unsigned assembly, as a package compiled against it records it.</summary>
    private static AssemblyName Reference(string name, Version version)
    {
        var reference = new AssemblyName(name) { Version = version };
        reference.SetPublicKeyToken([]);
        return reference;
    }

    /// <summary>The major of a line's <c>major.minor</c> in <c>VersionLines.props</c>.</summary>
    private static int LineMajor(string property) =>
        Version.Parse(XDocument.Load(Path.Join(RepoRoot, "VersionLines.props")).Descendants(property).Single().Value.Trim()).Major;

    /// <summary>
    /// The reference a package compiled against a build of the repository carries to the project of every Elsa assembly a
    /// host shares: one MSBuild invocation runs the SDK's own <c>GetAssemblyVersion</c> target in each project, with no build
    /// input, or with a pack-properties file shaped as the calculator writes it. A project that signs its assembly fails
    /// here, because its reference carries a token this guard cannot derive and each host's entry would have to name.
    /// </summary>
    private static IReadOnlyDictionary<string, AssemblyName> BuiltReferences(bool computed)
    {
        var projects = HostsThatShareAssemblies().SelectMany(host => ElsaShares(NuplaneSharedAssemblyPolicy.Bind(ReadNuplane(host))))
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
                      <_ElsaReportedAssemblyVersion Include="$(AssemblyName)" AssemblyVersion="$(AssemblyVersion)" SignAssembly="$(SignAssembly)" />
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
                    item =>
                    {
                        var name = item.GetProperty("Identity").GetString()!;
                        Assert.False(
                            string.Equals(item.GetProperty("SignAssembly").GetString(), "true", StringComparison.OrdinalIgnoreCase),
                            $"{name} signs its assembly, so a package's reference to it carries a public key token each host's " +
                            "Nuplane:Loading:SharedAssemblies entry would have to name, and this guard would have to derive (#2150).");
                        return Reference(name, Version.Parse(item.GetProperty("AssemblyVersion").GetString()!));
                    },
                    StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }
}
