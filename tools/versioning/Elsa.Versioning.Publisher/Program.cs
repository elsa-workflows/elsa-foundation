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
//          --ref <GITHUB_REF> --commit <GITHUB_SHA> --reason <text> --actor <github.actor> [--repo <dir>] [--remote <name>] [--summary <file>]
//          (--package-id <id> --feed <service index URL> --api-key-env <variable> [--version <version>] | --main-commit <commit>)
//
// Exit codes: 0 done; 1 refused or failed (a gate, the bootstrap's preconditions, a moved record, a failed or colliding
// push, a failed write-back, a repair that would lower an entry, names a version the feed does not hold, or names a
// commit not on main); 2 invalid input or usage.

const string Usage =
    "Usage: plan --ref <ref> --output <dir> [--commit <revision>] [--bootstrap true|false] [--solution <file>] [--repo <dir>] [--remote <name>] " +
    "[--force-advance <ids> --force-reason <text>] [--github-output <file>] [--summary <file>] | " +
    "publish --ref <ref> --plan-dir <dir> --feed <url> --api-key-env <variable> [--commit <revision>] [--repo <dir>] [--remote <name>] [--summary <file>] | " +
    "repair --ref <ref> --commit <revision> --reason <text> --actor <name> [--repo <dir>] [--remote <name>] [--summary <file>] " +
    "(--package-id <id> --feed <url> --api-key-env <variable> [--version <version>] | --main-commit <commit>)";

string[] planOptions = ["--ref", "--output", "--commit", "--bootstrap", "--solution", "--repo", "--remote", "--force-advance", "--force-reason", "--github-output", "--summary"];
string[] publishOptions = ["--ref", "--plan-dir", "--feed", "--api-key-env", "--commit", "--repo", "--remote", "--summary"];
string[] repairOptions = ["--ref", "--reason", "--actor", "--package-id", "--version", "--main-commit", "--feed", "--api-key-env", "--commit", "--repo", "--remote", "--summary"];

try
{
    var command = args.FirstOrDefault();
    if (command == "recover-exact20")
        return await RunExact20RecoveryAsync(args.Skip(1).ToArray());

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
                new RepairPackageOptions(repository, reference, options.GetValueOrDefault("--commit") ?? "HEAD", packageId, options.GetValueOrDefault("--version"), reason, actor, summary),
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

static async Task<int> RunExact20RecoveryAsync(string[] arguments)
{
    const string usage = "Usage: recover-exact20 [--execute true --confirm <incident confirmation> --approval-comment-id <positive id>]";
    var options = Options(arguments, ["--execute", "--confirm", "--approval-comment-id", "--summary"]);
    var execute = (options.GetValueOrDefault("--execute") ?? "false") switch
    {
        "true" => true,
        "false" => false,
        var value => throw new ArgumentException($"--execute must be true or false, not '{value}'. {usage}")
    };
    if (!execute && (options.ContainsKey("--confirm") || options.ContainsKey("--approval-comment-id")))
        throw new ArgumentException($"--confirm and --approval-comment-id require --execute true. {usage}");

    long? approvalCommentId = null;
    if (options.GetValueOrDefault("--approval-comment-id") is { Length: > 0 } idText)
    {
        if (!long.TryParse(idText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            throw new ArgumentException("--approval-comment-id must be a positive integer.");
        approvalCommentId = parsed;
    }

    var repository = Directory.GetCurrentDirectory();
    var environment = Exact20Environment();
    var runOptions = new Exact20RunOptions(
        repository,
        execute,
        options.GetValueOrDefault("--confirm"),
        approvalCommentId,
        environment,
        options.GetValueOrDefault("--summary") ?? environment.GetValueOrDefault("GITHUB_STEP_SUMMARY"));

    // This validates the true manual-main-run context before the process reads FEEDZ_API_KEY.
    if (execute)
        Exact20RecoveryCommand.ValidateExecutionInvocationForCli(runOptions);

    var githubToken = Environment.GetEnvironmentVariable("GH_TOKEN") is { Length: > 0 } token
        ? token
        : throw new InvalidOperationException("GH_TOKEN must be provided for the fixed GitHub GET requests.");
    using var githubHttp = Exact20GitHub.CreateHttpClient();
    using var feedHttp = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
    var git = new Exact20LocalGit(repository);
    var serviceIndex = new Uri(Exact20RecoveryPins.FeedIndex);
    var readFeed = new NuGetFeed(feedHttp, serviceIndex, string.Empty, requireFeedzResourceHost: true, suppressResponseBodies: true);
    var command = new Exact20RecoveryCommand(
        Exact20RecoveryPins.Incident,
        new Exact20GitHub(githubHttp, githubToken),
        new Exact20Feed(readFeed, () =>
        {
            var key = Environment.GetEnvironmentVariable(Exact20RecoveryPins.FeedKeyName);
            if (string.IsNullOrEmpty(key))
                throw new InvalidOperationException("FEEDZ_API_KEY is required only after the complete exact20 execution preflight succeeds.");
            return new NuGetFeed(feedHttp, serviceIndex, key, requireFeedzResourceHost: true, suppressResponseBodies: true);
        }),
        new GitPublishState(repository),
        git,
        Console.Out);
    var report = await command.RunAsync(runOptions);
    return report.Succeeded ? 0 : 1;
}

static IReadOnlyDictionary<string, string?> Exact20Environment()
{
    string[] names = [
        "GITHUB_ACTIONS", "GITHUB_EVENT_NAME", "GITHUB_REF", "GITHUB_WORKFLOW", "GITHUB_JOB",
        "GITHUB_WORKFLOW_REF", "GITHUB_WORKFLOW_SHA", "GITHUB_REPOSITORY", "GITHUB_RUN_ATTEMPT",
        "GITHUB_RUN_ID", "GITHUB_SHA", "GITHUB_EVENT_PATH", "GITHUB_STEP_SUMMARY"
    ];
    return names.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
}
