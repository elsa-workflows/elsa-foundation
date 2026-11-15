using System.Text.Json;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Weaver.Persistence.EntityFrameworkCore;

// This application is a bounded transport into the existing tooling contract.
// It supplies no finalization algorithm, fleet, migrations or protected-row codec.
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
try
{
    if (args is not ["--owned-root", var ownedRoot, "--database", var database])
        throw new InvalidDataException("Explicit owned root and database are required.");
    CheckPath(ownedRoot, directory: true);
    CheckPath(database, directory: false);
    var rootPrefix = ownedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    var publish = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
    if (!database.StartsWith(rootPrefix, StringComparison.Ordinal) || ownedRoot == publish
        || ownedRoot.StartsWith(publish + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || publish.StartsWith(rootPrefix, StringComparison.Ordinal)
        || database.Length != database.TrimEnd().Length
        || database.IndexOfAny([';', '"', '\r', '\n']) >= 0)
        throw new InvalidDataException("Runtime database must be outside the immutable publish.");

    await using var request = new MemoryStream();
    var buffer = new byte[4096];
    using var stdin = Console.OpenStandardInput();
    while (true)
    {
        var count = await stdin.ReadAsync(buffer, deadline.Token);
        if (count == 0) break;
        if (request.Length + count > 64 * 1024) throw new InvalidDataException("Request is too large.");
        await request.WriteAsync(buffer.AsMemory(0, count), deadline.Token);
    }
    request.Position = 0;
    using var document = await JsonDocument.ParseAsync(request, new JsonDocumentOptions { MaxDepth = 8 }, deadline.Token);
    NoDuplicates(document.RootElement);
    var json = document.RootElement;
    var requestVersion = json.GetProperty("version");
    if (requestVersion.ValueKind != JsonValueKind.Number || !requestVersion.TryGetInt32(out var envelopeVersion)
        || envelopeVersion != EfToolingContract.Version
        || json.GetProperty("command").GetString() is not ("hold" or "release" or "status")
        || json.GetProperty("provider").GetString() != "Sqlite"
        || json.GetProperty("connection").GetString() != "Data Source=" + database)
        throw new InvalidDataException("Only owned SQLite finalization operations are admitted.");
    var selection = json.GetProperty("selection");
    var modules = selection.GetProperty("modules");
    var finalization = json.GetProperty("finalization");
    if (selection.GetProperty("kind").GetString() != "modules" || modules.ValueKind != JsonValueKind.Array
        || modules.GetArrayLength() != 1 || modules[0].GetString() != "Weaver"
        || finalization.GetProperty("family").GetString() != "Weaver"
        || finalization.TryGetProperty("version", out var version) && version.ValueKind != JsonValueKind.Null)
        throw new InvalidDataException("Only the Weaver family-wide hold/status scope is admitted.");
    request.Position = 0;
    // The existing closed contract validates all other request fields and owns
    // revision checks, holds, release and status. This call never forces finalization.
    return await EfToolingHost.RunAsync(request, Console.OpenStandardOutput(), [typeof(WeaverEfModule).Assembly], deadline.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("{\"operatorRefused\":true,\"code\":\"operator-cancelled\"}");
    return 3;
}
catch (Exception failure) when (failure is InvalidDataException or JsonException or KeyNotFoundException
    or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
{
    // Never echo request bytes, connection strings, private paths or exception text.
    Console.WriteLine("{\"operatorRefused\":true,\"code\":\"operator-guard-refused\"}");
    return 2;
}

static void CheckPath(string path, bool directory)
{
    if (!Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path || OperatingSystem.IsWindows())
        throw new InvalidDataException("An explicit canonical POSIX owned path is required.");
    FileSystemInfo? current = directory ? new DirectoryInfo(path) : new FileInfo(path);
    if (!current.Exists) throw new InvalidDataException("The caller-owned runtime path must already exist.");
    const UnixFileMode other = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
    var privateDirectory = directory ? path : Path.GetDirectoryName(path)!;
    if ((File.GetUnixFileMode(privateDirectory) & other) != 0)
        throw new InvalidDataException("The caller-owned runtime path must be private.");
    while (current is not null)
    {
        if (current.LinkTarget is not null) throw new InvalidDataException("Owned paths cannot traverse symbolic links.");
        current = current is DirectoryInfo parent ? parent.Parent : ((FileInfo)current).Directory;
    }
}

static void NoDuplicates(JsonElement value)
{
    if (value.ValueKind == JsonValueKind.Object)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate request member.");
            NoDuplicates(property.Value);
        }
    }
    else if (value.ValueKind == JsonValueKind.Array)
        foreach (var item in value.EnumerateArray()) NoDuplicates(item);
}
