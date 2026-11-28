// The Elsa 3 workflows migration CLI (spec 1237). Run from a clone:
// dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory <export> [--out <dir>] [--all-versions]
using Elsa3.Migration.Cli;
using Elsa3.Migration.Tooling;

// Ctrl+C cancels the run instead of ending the process, so the run removes its temporary folder, database and staging folder.
using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler cancel = (_, press) =>
{
    press.Cancel = true;
    cancellation.Cancel();
};
Console.CancelKeyPress += cancel;
try
{
    HostWiring wiring;
    try
    {
        wiring = CliHostWiring.Create();
    }
    catch (InvalidOperationException exception)
    {
        Console.Error.WriteLine($"elsa3-migration: failed: the CLI build is incomplete ({exception.GetType().Name}); rebuild it.");
        return MigrationCommandLine.FailedExitCode;
    }

    return await MigrationCommandLine.RunAsync(args, Console.Out, Console.Error, wiring, cancellation.Token);
}
finally
{
    Console.CancelKeyPress -= cancel;
}
