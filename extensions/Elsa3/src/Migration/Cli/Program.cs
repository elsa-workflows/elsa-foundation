// The Elsa 3 workflows migration CLI (spec 1237). Run from a clone:
// dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory <export> [--out <dir>] [--all-versions]
using Elsa3.Migration.Cli;

// Ctrl+C cancels the run instead of ending the process, so the run removes its temporary folder, database and staging folder.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, press) =>
{
    press.Cancel = true;
    cancellation.Cancel();
};

return await MigrationCommandLine.RunAsync(args, Console.Out, Console.Error, CliHostWiring.Create(), cancellation.Token);
