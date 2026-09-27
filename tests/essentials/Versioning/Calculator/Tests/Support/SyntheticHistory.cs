namespace Elsa.Versioning.Calculator.Tests.Support;

/// <summary>
/// A published baseline every synthetic-history suite starts from: a small repository shaped like this one, committed
/// once, with a last-published record naming that commit for every package.
/// </summary>
/// <remarks>
/// <para>Projects, by package id (line, recorded version):</para>
/// <list type="bullet">
/// <item><c>Elsa.Primitives</c> (A, 4.0.3) and <c>Elsa.Events.Core</c> (A, 4.0.3), nested inside <c>Elsa.Events</c> (B, 4.0.2);</item>
/// <item><c>Elsa.Tasks</c> (B, 4.0.7) with a README, an <c>EXTENSION_POINTS.md</c> and a <c>docs/</c> folder, and
/// <c>Elsa.Tasks.Schedules</c> (B, 4.0.1) nested inside it and referencing it;</item>
/// <item><c>Elsa.Http</c> (B, 4.0.4);</item>
/// <item><c>dotnet-elsa</c> (B, 4.0.5), a tool at <c>src/Cli</c> carrying <c>Elsa.Http</c> and the unpackable
/// <c>Elsa.Cli.Worker</c> nested inside it;</item>
/// <item>and the unpackable test project <c>Elsa.Tasks.Tests</c>.</item>
/// </list>
/// <para>
/// <c>Cronos</c> is referenced by <c>Elsa.Tasks</c> alone, <c>Jint</c> by <c>Elsa.Http</c>, <c>xunit</c> by the test
/// project, and <c>Microsoft.OpenApi</c> by nothing: it is a transitive pin.
/// </para>
/// </remarks>
public abstract class SyntheticHistory : IDisposable
{
    protected SyntheticHistory()
    {
        Repo.PackageVersions["Cronos"] = "0.13.0";
        Repo.PackageVersions["Jint"] = "4.9.1";
        Repo.PackageVersions["Microsoft.OpenApi"] = "2.1.0";
        Repo.PackageVersions["System.CommandLine"] = "2.0.0";
        Repo.PackageVersions["xunit"] = "2.9.3";

        Repo.AddProject("src/Primitives/Elsa.Primitives.csproj", line: "A");
        Repo.AddProject("src/Events/Core/Elsa.Events.Core.csproj", line: "A", references: ["src/Primitives/Elsa.Primitives.csproj"]);
        Repo.AddProject("src/Events/Elsa.Events.csproj", references: ["src/Events/Core/Elsa.Events.Core.csproj"],
            body: """  <ItemGroup><Compile Remove="Core/**/*" /></ItemGroup>""");
        Repo.AddProject("src/Tasks/Elsa.Tasks.csproj", references: ["src/Primitives/Elsa.Primitives.csproj"], packages: ["Cronos"],
            body: """  <ItemGroup><Compile Remove="Schedules/**/*" /></ItemGroup>""");
        Repo.AddProject("src/Tasks/Schedules/Elsa.Tasks.Schedules.csproj", references: ["src/Tasks/Elsa.Tasks.csproj"]);
        Repo.AddProject("src/Http/Elsa.Http.csproj", packages: ["Jint"]);
        Repo.AddProject("src/Cli/Elsa.Cli.csproj", packageId: "dotnet-elsa", references: ["src/Cli/Worker/Elsa.Cli.Worker.csproj", "src/Http/Elsa.Http.csproj"],
            packages: ["System.CommandLine"], body: """  <PropertyGroup><PackAsTool>true</PackAsTool></PropertyGroup>""");
        Repo.AddProject("src/Cli/Worker/Elsa.Cli.Worker.csproj", packable: false);
        Repo.AddProject("tests/Tasks/Elsa.Tasks.Tests.csproj", packable: false, references: ["src/Tasks/Elsa.Tasks.csproj"], packages: ["xunit"]);

        Repo.Write("src/Primitives/Primitive.cs", "public sealed class Primitive;");
        Repo.Write("src/Events/Core/Event.cs", "public sealed class Event;");
        Repo.Write("src/Events/Events.cs", "public sealed class Events;");
        Repo.Write("src/Events/README.md", "# Events");
        Repo.Write("src/Tasks/Scheduler.cs", "public sealed class Scheduler;");
        Repo.Write("src/Tasks/README.md", "# Tasks");
        Repo.Write("src/Tasks/EXTENSION_POINTS.md", "# Extension points");
        Repo.Write("src/Tasks/docs/guide.md", "# Guide");
        Repo.Write("src/Tasks/Schedules/Schedule.cs", "public sealed class Schedule;");
        Repo.Write("src/Http/Http.cs", "public sealed class Http;");
        Repo.Write("src/Cli/Program.cs", "return 0;");
        Repo.Write("src/Cli/Worker/Worker.cs", "public sealed class Worker;");
        Repo.Write("tests/Tasks/SchedulerTests.cs", "public sealed class SchedulerTests;");
        Repo.Write("README.md", "# Repository");
        Repo.Write("docs/guide.md", "# Guide");
        Repo.Write("tools/script.sh", "echo tool");

        Baseline = Repo.Commit("baseline");
        (string Id, string Version)[] published =
        [
            ("Elsa.Primitives", "4.0.3-preview"),
            ("Elsa.Events.Core", "4.0.3-preview"),
            ("Elsa.Events", "4.0.2-preview"),
            ("Elsa.Tasks", "4.0.7-preview"),
            ("Elsa.Tasks.Schedules", "4.0.1-preview"),
            ("Elsa.Http", "4.0.4-preview"),
            ("dotnet-elsa", "4.0.5-preview")
        ];
        Record = new PublishedVersions(Baseline, published.Select(entry => new PublishedPackage(entry.Id, PackageVersionNumber.Parse(entry.Version, "test"), Baseline)));
    }

    private protected SyntheticRepository Repo { get; } = new();

    /// <summary>The published baseline commit.</summary>
    protected string Baseline { get; }

    /// <summary>The current revision of the last-published record.</summary>
    protected PublishedVersions Record { get; set; }

    /// <summary>Every package id in the baseline, in the ordinal order the output lists them.</summary>
    protected static string[] AllPackages { get; } =
        ["Elsa.Events", "Elsa.Events.Core", "Elsa.Http", "Elsa.Primitives", "Elsa.Tasks", "Elsa.Tasks.Schedules", "dotnet-elsa"];

    /// <summary>Changes a file's content, differently each time, as an ordinary edit would.</summary>
    protected void Edit(string path) => Repo.Write(path, Repo.Read(path) + "\n// edited");

    /// <summary>Computes at a commit (the head by default) against a record (the current one by default).</summary>
    protected VersionComputation Compute(string? commit = null, PublishedVersions? record = null) =>
        VersionCalculator.Compute(Repo.Calculator, commit ?? Repo.Head, record ?? Record);

    /// <summary>Commits the working tree and computes at the new commit.</summary>
    protected VersionComputation CommitAndCompute(string message = "change") => Compute(Repo.Commit(message));

    /// <summary>
    /// Records packages of a computation as pushed, as a publish's write-back would — every affected package, or only
    /// <paramref name="pushed"/> for a publish whose other pushes failed — and returns the new record revision.
    /// </summary>
    protected PublishedVersions Publish(VersionComputation computation, params string[] pushed)
    {
        IReadOnlyList<string> ids = pushed.Length > 0 ? pushed : computation.Affected;
        return Record = new PublishedVersions(
            computation.Commit,
            Record.Packages
                .Where(entry => !ids.Contains(entry.PackageId, StringComparer.OrdinalIgnoreCase))
                .Concat(ids.Select(id => computation[id]).Select(package =>
                    new PublishedPackage(package.PackageId, package.Version with { Label = "preview" }, computation.Commit))));
    }

    /// <summary>The current record with one package's recorded version replaced.</summary>
    protected PublishedVersions RecordWith(string packageId, string version) =>
        new(Record.LastPublishCommit, Record.Packages.Select(entry =>
            entry.PackageId == packageId ? entry with { Version = PackageVersionNumber.Parse(version, "test") } : entry));

    /// <summary>The package's computed <c>major.minor.patch</c>.</summary>
    protected static string VersionOf(VersionComputation computation, string packageId) => computation[packageId].Version.Numeric;

    public void Dispose()
    {
        Repo.Dispose();
        GC.SuppressFinalize(this);
    }
}
