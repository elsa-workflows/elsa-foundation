using Elsa.Versioning.Calculator;

// Computes every package's version, the affected set and each package's input fingerprint (spec 150). See README.md.
//
// Usage: dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
//          (--record <file> | --record-ref <revision> [--record-path <path>]) [--commit <revision>] [--repo <dir>] [--output <file>]
//
// Exit codes: 0 computed; 1 a publish gate refused (FR-012 monotonicity, FR-021 forward-only); 2 invalid input or usage.

const string Usage =
    "Usage: (--record <file> | --record-ref <revision> [--record-path <path>]) [--commit <revision>] [--repo <dir>] [--output <file>]";

try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index += 2)
    {
        if (args[index] is not ("--record" or "--record-ref" or "--record-path" or "--commit" or "--repo" or "--output") || index + 1 >= args.Length)
            throw new ArgumentException($"Unexpected argument '{args[index]}'. {Usage}");
        if (!options.TryAdd(args[index], args[index + 1]))
            throw new ArgumentException($"{args[index]} was given twice. {Usage}");
    }

    var git = new GitRepository(options.GetValueOrDefault("--repo") ?? Directory.GetCurrentDirectory());
    var record = (options.GetValueOrDefault("--record"), options.GetValueOrDefault("--record-ref")) switch
    {
        ({ } file, null) when !options.ContainsKey("--record-path") => PublishedVersions.Load(file),
        (null, { } revision) => PublishedVersions.Load(git, revision, options.GetValueOrDefault("--record-path") ?? PublishedVersions.DefaultPath),
        _ => throw new ArgumentException($"Name the last-published record with exactly one of --record or --record-ref. {Usage}")
    };

    var json = VersionCalculator.Compute(git, options.GetValueOrDefault("--commit") ?? "HEAD", record).ToJson();
    if (options.GetValueOrDefault("--output") is { } output)
        File.WriteAllText(output, json);
    else
        Console.Out.Write(json);

    return 0;
}
catch (VersionGateException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
