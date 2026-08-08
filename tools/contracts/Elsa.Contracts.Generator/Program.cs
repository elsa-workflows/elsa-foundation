// Consumer-contract fragment generator (spec 149 / RFC #1191).
//
// Usage:
//   dotnet run --project tools/contracts/Elsa.Contracts.Generator -- merge [--configuration Release] [--output <dir>]
//   dotnet run --project tools/contracts/Elsa.Contracts.Generator -- check [--configuration Release]
//   dotnet run --project tools/contracts/Elsa.Contracts.Generator -- emit --assembly <dll> [--references <rsp>] --output <dir>
//   dotnet run --project tools/contracts/Elsa.Contracts.Generator -- claims [--configuration Release]
//
// merge  — project every contributing src assembly and write docs/contracts/ (fragments, submit-schema, manifest).
// check  — regenerate to a scratch directory and byte-compare against the committed docs/contracts/ (CI gate; exit 1 = stale).
// emit   — project a single assembly (the standalone mode consumers run against their own activity packages).
// claims — gate docs/consumer-guide/claims.json against its [ConsumerContract] pins, both directions (CI gate).
//
// Exit codes: 0 success/fresh, 1 diagnostics or stale artifacts, 2 usage error.

using Elsa.Contracts.Generator;

var diagnostics = new Diagnostics();
try
{
    return args switch
    {
        ["merge", .. var rest] => RunMerge(Options.Parse(rest)),
        ["check", .. var rest] => new ContractsFreshness(diagnostics).Run(RepoRoot(), Options.Parse(rest).Configuration),
        ["emit", .. var rest] => RunEmit(Options.Parse(rest)),
        ["claims", .. var rest] => new ConsumerClaimsGate(diagnostics).Run(RepoRoot(), Options.Parse(rest).Configuration),
        _ => Usage()
    };
}
catch (UsageException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

int RunMerge(Options options)
{
    var output = options.Output ?? Path.Combine(RepoRoot(), "docs", "contracts");
    return new ContractsMerge(diagnostics).Run(RepoRoot(), options.Configuration, output);
}

int RunEmit(Options options)
{
    if (options.Assembly is null || options.Output is null)
        throw new UsageException("emit requires --assembly <dll> and --output <dir>.");

    var referencePaths = options.References is not null
        ? File.ReadAllLines(options.References).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
        : Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(options.Assembly))!, "*.dll").ToArray();
    TargetAssembly.AddProbeDirectory(Path.GetDirectoryName(Path.GetFullPath(options.Assembly))!);

    var fragment = new FragmentProjector(diagnostics).Project(options.Assembly, referencePaths);
    if (diagnostics.HasErrors)
        return 1;
    if (!fragment.HasContributions)
    {
        Console.WriteLine($"'{fragment.Assembly}' contributes no consumer-visible authoring surface; no fragment emitted.");
        return 0;
    }

    var path = Path.Combine(options.Output, fragment.Assembly + ".json");
    DeterministicJson.WriteFile(path, DeterministicJson.SerializeToBytes(fragment));
    Console.WriteLine($"Wrote {path}.");
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("Usage: Elsa.Contracts.Generator <merge|check|emit|claims> [options] (see Program.cs header).");
    return 2;
}

static string RepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    for (var current = directory; current is not null; current = current.Parent)
    {
        if (File.Exists(Path.Combine(current.FullName, "Elsa.Server.slnx")))
            return current.FullName;
    }

    throw new UsageException("Repository root (Elsa.Server.slnx) not found above the generator binary.");
}

internal sealed record Options(string Configuration, string? Assembly, string? References, string? Output)
{
    public static Options Parse(IReadOnlyList<string> arguments)
    {
        string configuration = "Release";
        string? assembly = null, references = null, output = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            string Next() => index + 1 < arguments.Count
                ? arguments[++index]
                : throw new UsageException($"Option '{arguments[index]}' requires a value.");

            switch (arguments[index])
            {
                case "--configuration" or "-c": configuration = Next(); break;
                case "--assembly": assembly = Next(); break;
                case "--references": references = Next(); break;
                case "--output": output = Next(); break;
                default: throw new UsageException($"Unknown option '{arguments[index]}'.");
            }
        }

        return new Options(configuration, assembly, references, output);
    }
}

internal sealed class UsageException(string message) : Exception(message);
