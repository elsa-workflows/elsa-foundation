// The Elsa 3 workflows migration CLI (spec 1237). Run from a clone:
// dotnet run --project extensions/Elsa3/src/Migration/Cli -- workflows inventory <export> [--out <dir>] [--all-versions]
using Elsa3.Migration.Cli;

return await MigrationCommandLine.RunAsync(args, Console.Out, Console.Error, CliHostWiring.Create(), CancellationToken.None);
