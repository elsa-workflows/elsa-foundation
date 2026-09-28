using Elsa.Versioning.Calculator;

// Computes every package's version, the affected set and each package's input fingerprint (spec 150), and optionally
// the MSBuild file that packs them (#2080). See README.md.
//
// Usage: dotnet run --project tools/versioning/Elsa.Versioning.Calculator -- \
//          (--record <file> | --record-ref <revision> [--record-path <path>]) [--commit <revision>] [--repo <dir>] [--output <file>] \
//          [--pack-properties <file> --branch <name>] [--force-advance <package-id>[,<package-id>...] --force-reason <text>]
//
// Exit codes: 0 computed; 1 a publish gate refused (FR-012 monotonicity, FR-021 forward-only); 2 invalid input or usage.

const string Usage =
    "Usage: (--record <file> | --record-ref <revision> [--record-path <path>]) [--commit <revision>] [--repo <dir>] [--output <file>] " +
    "[--pack-properties <file> --branch <name>] [--force-advance <package-id>[,<package-id>...] --force-reason <text>]";

try
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < args.Length; index += 2)
    {
        if (args[index] is not ("--record" or "--record-ref" or "--record-path" or "--commit" or "--repo" or "--output" or "--pack-properties" or "--branch"
                or "--force-advance" or "--force-reason") ||
            index + 1 >= args.Length)
            throw new ArgumentException($"Unexpected argument '{args[index]}'. {Usage}");
        if (!options.TryAdd(args[index], args[index + 1]))
            throw new ArgumentException($"{args[index]} was given twice. {Usage}");
    }

    if (options.ContainsKey("--pack-properties") != options.ContainsKey("--branch"))
        throw new ArgumentException($"--pack-properties and --branch go together: the branch chooses the packages' prerelease label. {Usage}");

    if (options.ContainsKey("--force-advance") != options.ContainsKey("--force-reason"))
        throw new ArgumentException($"--force-advance and --force-reason go together: the output records why each named package advances. {Usage}");

    var git = new GitRepository(options.GetValueOrDefault("--repo") ?? Directory.GetCurrentDirectory());
    var record = (options.GetValueOrDefault("--record"), options.GetValueOrDefault("--record-ref")) switch
    {
        ({ } file, null) when !options.ContainsKey("--record-path") => PublishedVersions.Load(file),
        (null, { } revision) => PublishedVersions.Load(git, revision, options.GetValueOrDefault("--record-path") ?? PublishedVersions.DefaultPath),
        _ => throw new ArgumentException($"Name the last-published record with exactly one of --record or --record-ref. {Usage}")
    };

    // Rendered before anything is written, so a branch that makes no label leaves neither output behind.
    var forced = options.TryGetValue("--force-advance", out var forcedIds) ? ForcedAdvance.Parse(forcedIds, options["--force-reason"]) : null;
    var computation = VersionCalculator.Compute(git, options.GetValueOrDefault("--commit") ?? "HEAD", record, forced);
    var packProperties = options.TryGetValue("--pack-properties", out var packPropertiesFile) ? PackProperties.Render(computation, options["--branch"]) : null;

    var json = computation.ToJson();
    if (options.GetValueOrDefault("--output") is { } output)
        File.WriteAllText(output, json);
    else
        Console.Out.Write(json);

    if (packProperties is not null)
        File.WriteAllText(packPropertiesFile!, packProperties);

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
