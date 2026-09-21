using Elsa.Cli;
using Elsa.Cli.Worker;
using System.CommandLine;

var parse = ElsaCli.Build().Parse(args);
if (parse.Errors.Count > 0)
{
    foreach (var error in parse.Errors)
        Console.Error.WriteLine($"error [usage]: {error.Message}");
    Console.Error.WriteLine("Run `dotnet elsa persistence --help` for the commands and flags this build accepts.");
    // A usage error is exit 2, not the parser's own default: every refusal this tool makes, wherever it is
    // decided, uses the one exit-code vocabulary spec 171 assigns.
    return ToolExitCode.Refusal;
}

return await parse.InvokeAsync();
