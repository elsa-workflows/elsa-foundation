using Elsa.Groundwork.StorePerformance.AdapterHost;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

// The matrix runner spawns this host once per process in a cohort. Every failure path exits non-zero with
// the contract message on stderr: the runner treats a child that cannot honour its request as a blocked
// run, and a host that degraded to a partial result would publish numbers describing something else.
try
{
    var command = args.Length > 0 ? args[0] : "";
    return command switch
    {
        "probe-provider" => ProbeProvider(args),
        _ => throw new PerformanceContractException(
            $"Unknown adapter-host command '{command}'. Supported: probe-provider.")
    };
}
catch (PerformanceContractException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}

// Reads the provider's own identity off a live connection. ValidateCorrectness binds the observed
// provider configuration to the requested one entry for entry, so these values must be read rather than
// guessed — this is the command whose output the operator pastes into capture-plan and matrix.
static int ProbeProvider(string[] args)
{
    var provider = HostArguments.Require(args, "probe-provider", "--provider");
    var connectionString = ProviderConnections.RequireConnectionString(provider);
    using var connection = ProviderConnections.Open(provider, connectionString);
    Console.WriteLine($"provider={provider}");
    Console.WriteLine($"connection-opened={connection.GetType().Name}");
    return 0;
}
