using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Every build file <c>Directory.Build.props</c> imports must reach a Docker build before <c>dotnet restore</c>
/// runs, or restore silently resolves the wrong graph and only the later <c>--no-restore publish</c> fails, with
/// MSB4019 - and only in CI's Docker Images workflow, on main. It has happened twice: #2087 added a root
/// <c>VersionLines.props</c> and #2114 a root <c>PackageRanges.targets</c>, each missing from both Dockerfiles'
/// restore-layer <c>COPY</c>, and NuGet restore treats a missing <c>&lt;Import&gt;</c> as a no-op rather than a
/// failure, so nothing local caught either one.
/// </summary>
/// <remarks>
/// <para>
/// The required set is computed, not restated: starting from <c>Directory.Build.props</c>,
/// <c>Directory.Packages.props</c> and (if it exists) <c>Directory.Build.targets</c>, every
/// <c>&lt;Import Project="$(MSBuildThisFileDirectory)…"/&gt;</c> is followed transitively. Any other import
/// form found in a reachable file is not skipped - it fails the test, naming the file, because this guard
/// cannot resolve where it points and must be extended to. Per Dockerfile, the same walk also starts from
/// the application project(s) it restores or publishes - the <c>.csproj</c> named on its <c>dotnet restore</c>
/// line(s) - because a csproj's own imports (for example a sibling <c>.targets</c> file, #2126) are just as
/// load-bearing for restore as the repo-root ones, and MSBuild treats a missing one as an error, not the
/// no-op it treats a missing repo-root import as.
/// </para>
/// <para>
/// For each Dockerfile under <c>src/</c>, every required file must be copied - by a non-<c>--from=</c>
/// <c>COPY</c> whose source patterns reach it - before the first <c>RUN dotnet restore</c>, and land at the
/// file's own repo-relative directory: without <c>--parents</c>, the destination must equal that directory;
/// with <c>--parents</c>, Docker reproduces the source's path under the destination, so the destination must
/// be the root instead. It must also not be excluded by the repo-root <c>.dockerignore</c>, matched the way
/// Docker itself matches it: last pattern wins, and a pattern excludes a path via itself or any ancestor
/// directory.
/// </para>
/// <para>
/// The Workbench's publish is held to the same rule for its computed package versions (#2084): the restore it relies
/// on must be given every property it is given, the pack-properties file among them.
/// </para>
/// </remarks>
public sealed class DockerBuildContextGuardTests
{
    private const string ThisFileDirectory = "$(MSBuildThisFileDirectory)";

    /// <summary>The required set and any import this guard could not resolve, computed once against the real repo.</summary>
    private static (IReadOnlyList<string> Files, IReadOnlyList<string> Unresolvable) Required { get; } = ResolveRequiredFiles(RepoRoot);

    [Fact]
    public void The_required_set_is_fully_resolved_and_non_vacuous()
    {
        Assert.True(
            Required.Unresolvable.Count == 0,
            "The guard cannot resolve an import and must be extended to handle it:" +
            Environment.NewLine + string.Join(Environment.NewLine, Required.Unresolvable));

        Assert.Contains("Directory.Build.props", Required.Files);
        Assert.Contains("Directory.Packages.props", Required.Files);
        Assert.Contains("VersionLines.props", Required.Files);
    }

    [Fact]
    public void Every_dockerfile_copies_every_required_build_file_before_restore()
    {
        var dockerfiles = Directory.EnumerateFiles(Path.Join(RepoRoot, "src"), "Dockerfile", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(dockerfiles.Length >= 2, $"Expected at least two Dockerfiles under src/; found {dockerfiles.Length}.");

        var violations = new List<string>();
        foreach (var dockerfile in dockerfiles)
        {
            var label = RelativePath(dockerfile);
            var text = File.ReadAllText(dockerfile);
            var (copies, missingRestore) = CopyInstructionsBeforeRestore(text);
            if (missingRestore is not null)
            {
                violations.Add($"{label}: {missingRestore}");
                continue;
            }

            var applicationProjects = ApplicationProjectsOf(text);
            if (applicationProjects.Count == 0)
            {
                violations.Add($"{label}: no dotnet restore/publish line names a .csproj, so the application-project " +
                    "guard would check nothing for it");
                continue;
            }

            var (applicationFiles, unresolvable) = ResolveRequiredFilesForDockerfile(RepoRoot, text);
            violations.AddRange(unresolvable.Select(entry => $"{label}: {entry}"));

            var requiredFiles = Required.Files
                .Concat(applicationFiles)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            violations.AddRange(UncoveredRequiredFiles(copies, requiredFiles)
                .Select(file => $"{label}: {file} is not copied before dotnet restore"));
        }

        Assert.True(violations.Count == 0, Report(violations));
    }

    [Fact]
    public void Dockerignore_does_not_exclude_a_required_build_file()
    {
        var lines = File.ReadAllLines(Path.Join(RepoRoot, ".dockerignore"));
        var violations = Required.Files
            .Where(file => IsDockerIgnored(lines, file))
            .Select(file => $".dockerignore: {file} is excluded from the Docker build context")
            .ToArray();

        Assert.True(violations.Length == 0, Report(violations));
    }

    /// <summary>
    /// The same silent failure one level down (#2084, #2126). A host image built with computed package versions gives its
    /// no-restore publish the calculator's pack-properties file, and <c>deps.json</c> takes a project reference's version
    /// from the restore, not the build: a publish whose restore was not given the file too stamps the computed versions
    /// into the assemblies and still records the dev versions in <c>deps.json</c>, which is what Nuplane checks a feed
    /// package's range against. So the <c>RUN</c> that publishes restores first, with every property it publishes with -
    /// true of both hosts that carry computed versions, Elsa.Workbench and Elsa.Foundation.Host.
    /// </summary>
    [Theory]
    [InlineData("Elsa.Workbench")]
    [InlineData("Elsa.Foundation.Host")]
    public void The_host_publish_restores_with_every_property_it_publishes_with(string host)
    {
        var dockerfile = File.ReadAllText(Path.Join(RepoRoot, "src", "apps", host, "Dockerfile"));

        Assert.Contains("-p:CustomBeforeDirectoryBuildProps=", PublishInstruction(dockerfile), StringComparison.Ordinal);
        Assert.Empty(PublishPropertiesItsRestoreLacks(dockerfile));
    }

    // ---- Mutation proofs: same predicates, synthetic inputs ----------------------------------------------------

    [Theory]
    [InlineData("-r \"$RID\" -p:PublishReadyToRun=true \"$@\"", null)]
    [InlineData("-r \"$RID\" -p:PublishReadyToRun=true", "\"$@\"")]
    [InlineData("-r \"$RID\" \"$@\"", "-p:PublishReadyToRun=true")]
    public void A_publish_whose_restore_lacks_one_of_its_properties_is_flagged(string restoreArguments, string? lacking)
    {
        var dockerfile = $"""
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            RUN set -- -p:CustomBeforeDirectoryBuildProps=/tmp/elsa/package-versions.props \
                && dotnet restore src/apps/Fixture/Fixture.csproj {restoreArguments} \
                && dotnet publish src/apps/Fixture/Fixture.csproj -c Release --no-restore -p:PublishReadyToRun=true "$@" -o /app/publish
            """;

        Assert.Equal(lacking is null ? [] : [lacking], PublishPropertiesItsRestoreLacks(dockerfile));
    }

    /// <summary>
    /// The gap this finding fixes (#2126): a Dockerfile that copies its application project but never the
    /// sibling <c>.targets</c> file the project imports must be flagged, the same way a missing repo-root
    /// import is - even though nothing outside the project graph itself names that file.
    /// </summary>
    [Fact]
    public void A_dockerfile_restoring_a_project_that_imports_an_uncopied_sibling_targets_file_is_flagged()
    {
        var root = Path.Join(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Join(root, "src", "apps", "Fixture"));
        File.WriteAllText(Path.Join(root, "src", "apps", "Sibling.targets"), "<Project />");
        File.WriteAllText(Path.Join(root, "src", "apps", "Fixture", "Fixture.csproj"), """
            <Project>
              <Import Project="$(MSBuildThisFileDirectory)../Sibling.targets" />
            </Project>
            """);

        try
        {
            const string dockerfile = """
                FROM mcr.microsoft.com/dotnet/sdk:10.0
                COPY src/apps/Fixture/Fixture.csproj src/apps/Fixture/
                RUN dotnet restore src/apps/Fixture/Fixture.csproj
                """;

            var (copies, missingRestore) = CopyInstructionsBeforeRestore(dockerfile);
            Assert.Null(missingRestore);

            var (applicationFiles, unresolvable) = ResolveRequiredFilesForDockerfile(root, dockerfile);
            Assert.Empty(unresolvable);
            Assert.Contains("src/apps/Sibling.targets", applicationFiles);
            Assert.Contains("src/apps/Sibling.targets", UncoveredRequiredFiles(copies, applicationFiles));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_dockerfile_copying_root_files_except_one_before_restore_is_flagged_for_the_missing_one()
    {
        const string dockerfile = """
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
            COPY Directory.Build.props Directory.Packages.props NuGet.config ./
            RUN dotnet restore src/apps/Fixture/Fixture.csproj
            """;

        var (copies, missingRestore) = CopyInstructionsBeforeRestore(dockerfile);
        Assert.Null(missingRestore);
        Assert.Equal(
            ["VersionLines.props"],
            UncoveredRequiredFiles(copies, ["Directory.Build.props", "Directory.Packages.props", "VersionLines.props"]));
    }

    [Theory]
    [InlineData("COPY *.props NuGet.config ./", true)]
    [InlineData("COPY *.props *.targets NuGet.config ./", false)]
    public void A_copy_pattern_either_covers_or_misses_a_required_targets_file(string copyInstruction, bool expectMissing)
    {
        var missing = UncoveredRequiredFiles([copyInstruction], ["PackageRanges.targets"]);
        Assert.Equal(expectMissing, missing.Contains("PackageRanges.targets"));
    }

    [Fact]
    public void A_comment_between_continuation_lines_does_not_end_the_copy()
    {
        const string dockerfile = """
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            COPY Directory.Build.props \
                # the version-line list
                VersionLines.props ./
            RUN dotnet restore Fixture.csproj
            """;

        var (copies, _) = CopyInstructionsBeforeRestore(dockerfile);
        Assert.Empty(UncoveredRequiredFiles(copies, ["Directory.Build.props", "VersionLines.props"]));
    }

    [Fact]
    public void A_file_copied_only_after_the_restore_run_is_flagged()
    {
        const string dockerfile = """
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            RUN dotnet restore Fixture.csproj
            COPY VersionLines.props ./
            """;

        var (copies, missingRestore) = CopyInstructionsBeforeRestore(dockerfile);
        Assert.Null(missingRestore);
        Assert.Contains("VersionLines.props", UncoveredRequiredFiles(copies, ["VersionLines.props"]));
    }

    /// <summary>
    /// The gap this finding fixes (round 2 of #2126): a <c>dotnet restore</c> line that names a <c>.sln</c>, or
    /// none at all, yields no application project, and the per-Dockerfile guard must not check that Dockerfile
    /// vacuously as a result - <see cref="Every_dockerfile_copies_every_required_build_file_before_restore"/>
    /// flags it by name instead of silently passing.
    /// </summary>
    [Theory]
    [InlineData("RUN dotnet restore Fixture.sln")]
    [InlineData("RUN dotnet restore")]
    public void A_dockerfile_whose_restore_names_no_csproj_yields_no_application_projects(string restoreInstruction)
    {
        var dockerfile = $"""
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            COPY . .
            {restoreInstruction}
            """;

        Assert.Empty(ApplicationProjectsOf(dockerfile));
    }

    [Fact]
    public void A_dockerfile_with_no_restore_run_fails_naming_that_instead_of_scanning_nothing()
    {
        const string dockerfile = """
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            COPY VersionLines.props ./
            RUN dotnet build Fixture.csproj
            """;

        var (_, missingRestore) = CopyInstructionsBeforeRestore(dockerfile);
        Assert.Equal("no RUN instruction runs dotnet restore", missingRestore);
    }

    [Fact]
    public void A_copy_from_another_build_stage_does_not_count()
    {
        const string dockerfile = """
            FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
            COPY --from=build VersionLines.props ./
            RUN dotnet restore Fixture.csproj
            """;

        var (copies, _) = CopyInstructionsBeforeRestore(dockerfile);
        Assert.Contains("VersionLines.props", UncoveredRequiredFiles(copies, ["VersionLines.props"]));
    }

    [Fact]
    public void A_root_file_copied_to_a_non_root_destination_is_flagged()
    {
        const string dockerfile = """
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            COPY *.props src/
            RUN dotnet restore Fixture.csproj
            """;

        var (copies, _) = CopyInstructionsBeforeRestore(dockerfile);
        Assert.Contains("VersionLines.props", UncoveredRequiredFiles(copies, ["VersionLines.props"]));
    }

    [Theory]
    [InlineData("./", false)]
    [InlineData("./staging/", true)]
    public void A_copy_with_parents_is_held_to_the_destination_root_not_a_root_file_directory(string destination, bool expectMissing)
    {
        var (copies, _) = CopyInstructionsBeforeRestore($"""
            FROM mcr.microsoft.com/dotnet/sdk:10.0
            COPY --parents *.props {destination}
            RUN dotnet restore Fixture.csproj
            """);

        Assert.Equal(expectMissing, UncoveredRequiredFiles(copies, ["VersionLines.props"]).Contains("VersionLines.props"));
    }

    [Fact]
    public void Dockerignore_pattern_excludes_a_file_and_a_later_negation_reincludes_it()
    {
        Assert.True(IsDockerIgnored(["*.targets"], "PackageRanges.targets"));
        Assert.False(IsDockerIgnored(["*.targets", "!PackageRanges.targets"], "PackageRanges.targets"));
    }

    [Fact]
    public void Dockerignore_directory_glob_matches_a_nested_file_but_not_a_similarly_named_one()
    {
        var lines = new[] { "**/bin/" };
        Assert.True(IsDockerIgnored(lines, "src/x/bin/a.props"));
        Assert.False(IsDockerIgnored(lines, "bin.props"));
    }

    [Fact]
    public void An_import_form_this_guard_cannot_resolve_is_reported_instead_of_skipped()
    {
        var document = XDocument.Parse("""
            <Project>
              <Import Project="$([MSBuild]::GetPathOfFileAbove(Directory.Build.props))" />
            </Project>
            """);

        var unresolved = ImportsOf(document, "").Where(import => import.Unresolved is not null).ToArray();
        Assert.Single(unresolved);
        Assert.Contains("GetPathOfFileAbove", unresolved[0].Unresolved);
    }

    // ---- Required-file resolution -------------------------------------------------------------------------------

    /// <summary>
    /// Follows every <c>$(MSBuildThisFileDirectory)</c> import from <paramref name="repoRoot"/>'s
    /// <c>Directory.Build.props</c>, <c>Directory.Packages.props</c> and (if present) <c>Directory.Build.targets</c>,
    /// transitively. Any other import form is collected as unresolvable rather than skipped.
    /// </summary>
    private static (IReadOnlyList<string> Files, IReadOnlyList<string> Unresolvable) ResolveRequiredFiles(string repoRoot)
    {
        var start = new List<string> { "Directory.Build.props", "Directory.Packages.props" };
        if (File.Exists(Path.Join(repoRoot, "Directory.Build.targets")))
            start.Add("Directory.Build.targets");

        return ResolveTransitiveImports(repoRoot, start);
    }

    /// <summary>
    /// The same transitive-import walk as <see cref="ResolveRequiredFiles"/>, started instead from the
    /// application project(s) <paramref name="dockerfileText"/> restores or publishes - the <c>.csproj</c>
    /// named on its <c>dotnet restore</c>/<c>dotnet publish</c> line(s) (#2126). A csproj is itself a starting
    /// point, not merely a required file reached from one, so its own imports (for example a sibling
    /// <c>.targets</c> file one directory up) are followed too.
    /// </summary>
    private static (IReadOnlyList<string> Files, IReadOnlyList<string> Unresolvable) ResolveRequiredFilesForDockerfile(string repoRoot, string dockerfileText) =>
        ResolveTransitiveImports(repoRoot, ApplicationProjectsOf(dockerfileText));

    /// <summary>The distinct <c>.csproj</c> path(s) named on a <c>dotnet restore</c> or <c>dotnet publish</c> line.</summary>
    private static IReadOnlyList<string> ApplicationProjectsOf(string dockerfileText) =>
        [.. Regex.Matches(string.Join(' ', LogicalInstructions(dockerfileText)), @"dotnet (?:restore|publish) (\S+\.csproj)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Follows every <c>$(MSBuildThisFileDirectory)</c> import from each of <paramref name="start"/>, transitively.
    /// Any other import form is collected as unresolvable rather than skipped.
    /// </summary>
    private static (IReadOnlyList<string> Files, IReadOnlyList<string> Unresolvable) ResolveTransitiveImports(string repoRoot, IEnumerable<string> start)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unresolvable = new List<string>();
        var queue = new Queue<string>(start);

        while (queue.TryDequeue(out var relative))
        {
            if (!seen.Add(relative))
                continue;

            files.Add(relative);
            var document = XDocument.Load(Path.Join(repoRoot, relative));
            foreach (var (resolved, unresolved) in ImportsOf(document, DirectoryOf(relative)))
            {
                if (unresolved is not null)
                    unresolvable.Add($"{relative} imports '{unresolved}', a form this guard cannot resolve and must be extended for.");
                else
                    queue.Enqueue(resolved!);
            }
        }

        return (files, unresolvable);
    }

    /// <summary>
    /// Every <c>&lt;Import&gt;</c> in <paramref name="document"/> (namespace-agnostic), resolved against
    /// <paramref name="directory"/> when it is the <c>$(MSBuildThisFileDirectory)</c> form, or returned as
    /// unresolved otherwise.
    /// </summary>
    private static IEnumerable<(string? Resolved, string? Unresolved)> ImportsOf(XDocument document, string directory) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "Import")
            .Select(element => element.Attribute("Project")?.Value)
            .OfType<string>()
            .Select(project => project.StartsWith(ThisFileDirectory, StringComparison.Ordinal)
                ? ((string?)JoinRelative(directory, project[ThisFileDirectory.Length..]), (string?)null)
                : (null, project));

    // ---- Dockerfile parsing --------------------------------------------------------------------------------------

    /// <summary>
    /// The non-<c>--from=</c> <c>COPY</c> instructions before the first <c>RUN</c> whose command contains
    /// <c>dotnet restore</c>, or an explanation string in place of a Dockerfile that has no such <c>RUN</c>.
    /// </summary>
    private static (IReadOnlyList<string> Copies, string? MissingRestore) CopyInstructionsBeforeRestore(string dockerfileText)
    {
        var instructions = LogicalInstructions(dockerfileText);
        var restoreIndex = instructions.ToList().FindIndex(instruction =>
            instruction.StartsWith("RUN ", StringComparison.OrdinalIgnoreCase) &&
            instruction.Contains("dotnet restore", StringComparison.Ordinal));

        if (restoreIndex < 0)
            return (Array.Empty<string>(), "no RUN instruction runs dotnet restore");

        var copies = instructions.Take(restoreIndex)
            .Where(instruction => instruction.StartsWith("COPY ", StringComparison.OrdinalIgnoreCase))
            .Where(instruction => !instruction.Contains("--from=", StringComparison.Ordinal))
            .ToArray();
        return (copies, null);
    }

    /// <summary>The one <c>RUN</c> instruction that runs <c>dotnet publish</c>.</summary>
    private static string PublishInstruction(string dockerfileText) =>
        LogicalInstructions(dockerfileText).Single(instruction =>
            instruction.StartsWith("RUN ", StringComparison.OrdinalIgnoreCase) &&
            instruction.Contains("dotnet publish", StringComparison.Ordinal));

    /// <summary>
    /// The properties the <c>dotnet publish</c> in <see cref="PublishInstruction"/> is given - its <c>-p:</c> arguments,
    /// and <c>"$@"</c>, which carries the pack-properties file - that the <c>dotnet restore</c> before it in the same
    /// instruction is not given: every one of them when no restore precedes it there.
    /// </summary>
    private static IReadOnlyList<string> PublishPropertiesItsRestoreLacks(string dockerfileText)
    {
        var run = PublishInstruction(dockerfileText);
        var publish = run.IndexOf("dotnet publish", StringComparison.Ordinal);
        var restore = run.LastIndexOf("dotnet restore", publish, StringComparison.Ordinal);
        IReadOnlyList<string> restoreArguments = restore < 0 ? [] : Arguments(run[restore..publish]);

        return
        [
            .. Arguments(run[publish..])
                .Where(argument => argument.StartsWith("-p:", StringComparison.Ordinal) || argument == "\"$@\"")
                .Where(argument => !restoreArguments.Contains(argument))
        ];
    }

    /// <summary>A command's whitespace-separated arguments, up to the <c>&amp;&amp;</c> that ends it.</summary>
    private static IReadOnlyList<string> Arguments(string command) =>
        [.. command.Split(' ', StringSplitOptions.RemoveEmptyEntries).TakeWhile(argument => argument != "&&")];

    /// <summary>
    /// Joins `\`-continued lines into one instruction each. Blank lines and `#` comments are dropped even inside a
    /// continued instruction, as Docker drops them, so a comment between continuation lines does not end it.
    /// </summary>
    private static IReadOnlyList<string> LogicalInstructions(string dockerfileText)
    {
        var instructions = new List<string>();
        var current = new StringBuilder();

        foreach (var line in SignificantLines(dockerfileText.Replace("\r\n", "\n").Split('\n')))
        {
            var continuing = line.EndsWith('\\');
            current.Append(current.Length > 0 ? " " : "").Append(continuing ? line[..^1].TrimEnd() : line);

            if (!continuing)
            {
                instructions.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
            instructions.Add(current.ToString());

        return instructions;
    }

    /// <summary>The required files a set of pre-restore <c>COPY</c> instructions does not reach.</summary>
    private static IReadOnlyList<string> UncoveredRequiredFiles(IReadOnlyList<string> copyInstructions, IReadOnlyList<string> requiredFiles) =>
        requiredFiles.Where(file => !copyInstructions.Any(copy => Covers(copy, file))).ToArray();

    /// <summary>
    /// Whether one <c>COPY</c> instruction reaches <paramref name="requiredFile"/>: some source pattern matches it,
    /// and it lands at the file's own repo-relative directory once normalized - without <c>--parents</c> that means
    /// the destination itself; with <c>--parents</c>, Docker reproduces the source's path under the destination, so
    /// the file lands at <c>&lt;destination&gt;/&lt;requiredFile&gt;</c> and the destination itself must be the root.
    /// </summary>
    private static bool Covers(string copyInstruction, string requiredFile)
    {
        var (flags, sources, destination) = ParseCopy(copyInstruction);
        if (!sources.Any(pattern => GlobToRegex(pattern).IsMatch(requiredFile)))
            return false;

        var normalizedDestination = NormalizeDestination(destination);
        return flags.Contains("--parents", StringComparer.Ordinal)
            ? normalizedDestination == ""
            : normalizedDestination == DirectoryOf(requiredFile);
    }

    /// <summary>
    /// Splits a <c>COPY</c> instruction into its flags, source patterns and destination (the last argument), in
    /// order and without deduplicating - <c>Except</c> would collapse a repeated argument such as <c>COPY ./ ./</c>.
    /// </summary>
    private static (IReadOnlyList<string> Flags, IReadOnlyList<string> Sources, string Destination) ParseCopy(string copyInstruction)
    {
        var tokens = copyInstruction.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        var flags = tokens.Where(token => token.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var arguments = tokens.Where(token => !token.StartsWith("--", StringComparison.Ordinal)).ToArray();
        return (flags, arguments[..^1], arguments[^1]);
    }

    /// <summary>Strips a leading <c>./</c> and trailing <c>/</c>; <c>.</c> or <c>./</c> alone means the root.</summary>
    private static string NormalizeDestination(string destination)
    {
        if (destination is "." or "./")
            return "";

        var trimmed = destination.StartsWith("./", StringComparison.Ordinal) ? destination[2..] : destination;
        return trimmed.EndsWith('/') ? trimmed[..^1] : trimmed;
    }

    // ---- .dockerignore --------------------------------------------------------------------------------------------

    /// <summary>
    /// Docker's own <c>.dockerignore</c> semantics: blank lines and <c>#</c> comments are skipped, a leading <c>/</c>
    /// and trailing <c>/</c> are stripped, <c>!</c> negates, the last matching pattern wins, and a pattern excludes a
    /// path via the path itself or any of its ancestor directories.
    /// </summary>
    private static bool IsDockerIgnored(IReadOnlyList<string> dockerignoreLines, string path)
    {
        var excluded = false;
        foreach (var line in SignificantLines(dockerignoreLines))
        {
            var negate = line.StartsWith('!');
            var pattern = (negate ? line[1..] : line).TrimStart('/').TrimEnd('/');
            if (pattern.Length == 0)
                continue;

            if (MatchesPathOrAnAncestor(pattern, path))
                excluded = !negate;
        }

        return excluded;
    }

    /// <summary>Trimmed lines, without the blank lines and <c>#</c> comments both Dockerfiles and <c>.dockerignore</c> skip.</summary>
    private static IEnumerable<string> SignificantLines(IEnumerable<string> lines) =>
        lines.Select(line => line.Trim()).Where(line => line.Length != 0 && !line.StartsWith('#'));

    private static bool MatchesPathOrAnAncestor(string pattern, string path)
    {
        var regex = GlobToRegex(pattern);
        var segments = path.Split('/');
        for (var length = segments.Length; length >= 1; length--)
        {
            if (regex.IsMatch(string.Join('/', segments[..length])))
                return true;
        }

        return false;
    }

    // ---- Shared glob support ----------------------------------------------------------------------------------------

    /// <summary>
    /// Converts a Dockerfile/.dockerignore glob to an anchored regex: <c>*</c> and <c>?</c> do not cross <c>/</c>,
    /// <c>**</c> does, and <c>**/</c> additionally matches zero directories (so <c>**/bin</c> also matches a
    /// top-level <c>bin</c>), matching how Docker itself treats it.
    /// </summary>
    private static Regex GlobToRegex(string pattern)
    {
        var regex = new StringBuilder("^");
        var i = 0;
        while (i < pattern.Length)
        {
            if (pattern[i] == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                {
                    regex.Append("(.*/)?");
                    i += 3;
                }
                else
                {
                    regex.Append(".*");
                    i += 2;
                }
            }
            else if (pattern[i] == '*')
            {
                regex.Append("[^/]*");
                i++;
            }
            else if (pattern[i] == '?')
            {
                regex.Append("[^/]");
                i++;
            }
            else
            {
                regex.Append(Regex.Escape(pattern[i].ToString()));
                i++;
            }
        }

        return new Regex(regex.Append('$').ToString());
    }

    private static string DirectoryOf(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? "" : relativePath[..slash];
    }

    /// <summary>
    /// Joins <paramref name="directory"/> and <paramref name="relative"/> and collapses <c>.</c>/<c>..</c>
    /// segments, so a $(MSBuildThisFileDirectory)-relative import that climbs out of its own directory
    /// (<c>../ComputedProductionSettings.targets</c>, #2126) resolves to the file's actual repo-relative path.
    /// </summary>
    private static string JoinRelative(string directory, string relative)
    {
        var segments = new List<string>();
        foreach (var segment in (directory.Length == 0 ? relative : $"{directory}/{relative}").Split('/'))
        {
            if (segment is "." or "")
                continue;
            if (segment == ".." && segments.Count > 0)
                segments.RemoveAt(segments.Count - 1);
            else
                segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string Report(IReadOnlyList<string> violations) =>
        $"{violations.Count} required build file(s) are not reliably in the Docker build context before restore:" +
        Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal));
}
