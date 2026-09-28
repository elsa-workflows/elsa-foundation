using Elsa.Versioning.Calculator;
using Elsa.Versioning.Publisher;

// Plans a Packages workflow run, publishes what it packed, and repairs publish-state by hand (spec 150; #2081, #2082,
// #2083). See ../README.md, Publishing.
//
// Usage: dotnet run --project tools/versioning/Elsa.Versioning.Publisher -- plan \
//          --ref <GITHUB_REF> --output <dir> [--commit <revision>] [--bootstrap true|false] [--solution <file>] [--repo <dir>] \
//          [--remote <name>] [--force-advance <ids> --force-reason <text>] [--github-output <file>] [--summary <file>]
//        dotnet run --project tools/versioning/Elsa.Versioning.Publisher -- publish \
//          --ref <GITHUB_REF> --plan-dir <dir> --feed <service index URL> --api-key-env <variable> [--commit <revision>] [--repo <dir>] \
//          [--remote <name>] [--summary <file>]
//        dotnet run --project tools/versioning/Elsa.Versioning.Publisher -- repair \
//          --ref <GITHUB_REF> --reason <text> --actor <github.actor> [--repo <dir>] [--remote <name>] [--summary <file>]
//          (--package-id <id> --feed <service index URL> --api-key-env <variable> | --commit <GITHUB_SHA> --main-commit <commit>)
//
// Exit codes: 0 done; 1 refused or failed (a gate, the bootstrap's preconditions, a moved record, a failed or colliding
// push, a failed write-back, a repair that would lower an entry or names a commit not on main); 2 invalid input or usage.

const string Usage =
    "Usage: plan --ref <ref> --output <dir> [--commit <revision>] [--bootstrap true|false] [--solution <file>] [--repo <dir>] [--remote <name>] " +
    "[--force-advance <ids> --force-reason <text>] [--github-output <file>] [--summary <file>] | " +
    "publish --ref <ref> --plan-dir <dir> --feed <url> --api-key-env <variable> [--commit <revision>] [--repo <dir>] [--remote <name>] [--summary <file>] | " +
    "repair --ref <ref> --reason <text> --actor <name> [--repo <dir>] [--remote <name>] [--summary <file>] " +
    "(--package-id <id> --feed <url> --api-key-env <variable> | --commit <revision> --main-commit <commit>)";

string[] planOptions = ["--ref", "--output", "--commit", "--bootstrap", "--solution", "--repo", "--remote", "--force-advance", "--force-reason", "--github-output", "--summary"];
string[] publishOptions = ["--ref", "--plan-dir", "--feed", "--api-key-env", "--commit", "--repo", "--remote", "--summary"];
string[] repairOptions = ["--ref", "--reason", "--actor", "--package-id", "--main-commit", "--feed", "--api-key-env", "--commit", "--repo", "--remote", "--summary"];

try
{
    var command = args.FirstOrDefault();
    var options = Options(args.Skip(1).ToArray(), command switch
    {
        "plan" => planOptions,
        "publish" => publishOptions,
        "repair" => repairOptions,
        _ => throw new ArgumentException($"Name a command, plan, publish or repair. {Usage}")
    });
    string Required(string name) => options.GetValueOrDefault(name) is { Length: > 0 } value ? value : throw new ArgumentException($"{command} needs {name}. {Usage}");
    var repository = options.GetValueOrDefault("--repo") ?? Directory.GetCurrentDirectory();
    var state = new GitPublishState(repository, options.GetValueOrDefault("--remote") ?? GitPublishState.DefaultRemote);

    if (command == "plan")
    {
        PlanCommand.Run(
            new PlanOptions(
                repository,
                Required("--ref"),
                options.GetValueOrDefault("--commit") ?? "HEAD",
                (options.GetValueOrDefault("--bootstrap") ?? "false") switch
                {
                    "true" => true,
                    "false" or "" => false,
                    var other => throw new ArgumentException($"--bootstrap is true or false, not '{other}'.")
                },
                Forced(options.GetValueOrDefault("--force-advance") ?? string.Empty, options.GetValueOrDefault("--force-reason") ?? string.Empty),
                Required("--output"),
                options.GetValueOrDefault("--solution") ?? "Elsa.Server.slnx",
                options.GetValueOrDefault("--github-output"),
                options.GetValueOrDefault("--summary")),
            state);
        return 0;
    }

    if (command == "repair")
    {
        var (packageId, mainCommit) = (options.GetValueOrDefault("--package-id") ?? string.Empty, options.GetValueOrDefault("--main-commit") ?? string.Empty);
        var reference = Required("--ref");
        var reason = Required("--reason");
        var actor = Required("--actor");
        var summary = options.GetValueOrDefault("--summary");

        RepairReport repaired;
        if (packageId.Length > 0 && mainCommit.Length == 0)
        {
            var repairApiKey = Environment.GetEnvironmentVariable(Required("--api-key-env")) is { Length: > 0 } repairKey
                ? repairKey
                : throw new ArgumentException($"The environment variable {options["--api-key-env"]} holds no API key.");
            using var repairHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            repaired = await RepairCommand.RepairPackageAsync(
                new RepairPackageOptions(repository, reference, packageId, reason, actor, summary),
                new NuGetFeed(repairHttp, new Uri(Required("--feed")), repairApiKey),
                state,
                Console.Out);
        }
        else if (mainCommit.Length > 0 && packageId.Length == 0)
        {
            repaired = await RepairCommand.RepairLastPublishCommitAsync(
                new RepairLastPublishCommitOptions(repository, reference, options.GetValueOrDefault("--commit") ?? "HEAD", mainCommit, reason, actor, summary),
                state,
                Console.Out);
        }
        else
        {
            throw new ArgumentException($"repair needs exactly one of --package-id (with --feed and --api-key-env) or --main-commit. {Usage}");
        }

        Console.WriteLine(repaired.Message);
        return 0;
    }

    var apiKey = Environment.GetEnvironmentVariable(Required("--api-key-env")) is { Length: > 0 } key
        ? key
        : throw new ArgumentException($"The environment variable {options["--api-key-env"]} holds no API key.");
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    var report = await PublishCommand.RunAsync(
        new PublishOptions(repository, Required("--ref"), options.GetValueOrDefault("--commit") ?? "HEAD", Required("--plan-dir"), options.GetValueOrDefault("--summary")),
        new NuGetFeed(http, new Uri(Required("--feed")), apiKey),
        state,
        Console.Out);
    if (report.Failure is { } failure)
        Console.Error.WriteLine(failure);

    return report.Succeeded ? 0 : 1;
}
catch (Exception exception) when (exception is VersionGateException or PublishRefusedException or FeedException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UriFormatException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

static Dictionary<string, string> Options(string[] arguments, string[] known)
{
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (!known.Contains(arguments[index]) || index + 1 >= arguments.Length)
            throw new ArgumentException($"Unexpected argument '{arguments[index]}'. {Usage}");
        if (!options.TryAdd(arguments[index], arguments[index + 1]))
            throw new ArgumentException($"{arguments[index]} was given twice. {Usage}");
    }

    return options;
}

// Empty ids and an empty reason mean no force-advance, so a workflow can pass its dispatch inputs unconditionally.
static ForcedAdvance? Forced(string ids, string reason) =>
    ids.Length == 0 && reason.Length == 0 ? null
    : ids.Length == 0 || reason.Length == 0 ? throw new ArgumentException("--force-advance and --force-reason go together: the computation records why each named package advances.")
    : ForcedAdvance.Parse(ids, reason);
