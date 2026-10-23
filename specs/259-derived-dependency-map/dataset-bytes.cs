#:project ../../tools/maps/Elsa.Maps.Projects/Elsa.Maps.Projects.csproj
#:property PublishAot=false
#:property RestorePackagesWithLockFile=false

// Spec 259 validation helper (research R8): for each commit named on the command line, derives the dependency-map
// dataset from the commit's git objects with the shared scan and compares it byte for byte with the copy committed in
// that commit. Prints one line per commit: "<commit> same", "<commit> differs <file>", "<commit> no-committed-map" or
// "<commit> underivable <reason>". A derived dataset that differs is written to <out-dir>/<commit>.json for diagnosis.
//
// Usage: dotnet run specs/259-derived-dependency-map/dataset-bytes.cs -- <repo> <out-dir> <commit>...

using System.Diagnostics;
using System.Text;
using Elsa.Maps.Projects;

var (repo, outDirectory, commits) = (args[0], args[1], args[2..]);
Directory.CreateDirectory(outDirectory);
using var blobs = new Blobs(repo);
foreach (var commit in commits)
{
    var tree = new GitTree(repo, commit, blobs);
    if (!tree.Exists(DependencyMapJson.RelativePath))
    {
        Console.WriteLine($"{commit} no-committed-map");
        continue;
    }

    string derived;
    try
    {
        derived = DependencyMapJson.Serialize(tree);
    }
    catch (Exception exception) when (exception is InvalidOperationException or System.Xml.XmlException or System.Text.Json.JsonException)
    {
        Console.WriteLine($"{commit} underivable {exception.Message.ReplaceLineEndings(" ")}");
        continue;
    }

    if (Encoding.UTF8.GetBytes(derived).AsSpan().SequenceEqual(tree.Read(DependencyMapJson.RelativePath)))
    {
        Console.WriteLine($"{commit} same");
        continue;
    }

    var file = Path.Join(outDirectory, $"{commit}.json");
    File.WriteAllText(file, derived);
    Console.WriteLine($"{commit} differs {file}");
}

/// <summary>A commit's tree read from git objects, as the calculator's CommitTree reads it.</summary>
internal sealed class GitTree : IRepositoryTree
{
    private readonly Dictionary<string, string> objects = new(StringComparer.Ordinal);
    private readonly Blobs blobs;

    public GitTree(string repo, string commit, Blobs blobs)
    {
        this.blobs = blobs;
        foreach (var record in Git.Run(repo, "ls-tree", "-r", "-z", "--full-tree", commit).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t', StringComparison.Ordinal);
            objects[record[(tab + 1)..]] = record[..tab].Split(' ')[2];
        }
    }

    public IReadOnlyList<string> ProjectFiles() => objects.Keys.Where(RepoLayout.IsProjectPath).Order(StringComparer.Ordinal).ToArray();

    public bool Exists(string path) => objects.ContainsKey(path);

    public byte[] Read(string path) => blobs.Read(objects[path]);
}

/// <summary>Blobs through one <c>git cat-file --batch</c>, cached by object id.</summary>
internal sealed class Blobs : IDisposable
{
    private readonly Dictionary<string, byte[]> cache = new(StringComparer.Ordinal);
    private readonly Process process;

    public Blobs(string repo)
    {
        var startInfo = new ProcessStartInfo("git") { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var argument in (string[])["-C", repo, "cat-file", "--batch"])
            startInfo.ArgumentList.Add(argument);
        process = Process.Start(startInfo)!;
    }

    public byte[] Read(string objectId)
    {
        if (cache.TryGetValue(objectId, out var cached))
            return cached;

        process.StandardInput.BaseStream.Write(Encoding.ASCII.GetBytes(objectId + "\n"));
        process.StandardInput.BaseStream.Flush();
        var output = process.StandardOutput.BaseStream;
        var header = new List<byte>();
        for (var next = output.ReadByte(); next != '\n'; next = output.ReadByte())
            header.Add((byte)next);
        var content = new byte[int.Parse(Encoding.ASCII.GetString(header.ToArray()).Split(' ')[2])];
        output.ReadExactly(content);
        output.ReadByte();
        return cache[objectId] = content;
    }

    public void Dispose()
    {
        process.StandardInput.Close();
        process.WaitForExit();
    }
}

internal static class Git
{
    public static string Run(string repo, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var argument in (string[])["-C", repo, .. arguments])
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed.");
    }
}
