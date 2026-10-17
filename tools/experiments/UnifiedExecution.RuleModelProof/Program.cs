namespace Elsa.UnifiedExecution.RuleModelProof;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "proof")
            return await ProofHarness.RunAsync();
        return args[0] switch
        {
            "--start" => await ChildCommands.RunStartAsync(args),
            "--rule-worker" => await ChildCommands.RunWorkerAsync(args),
            "--resume" => await ChildCommands.RunResumeAsync(args),
            _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
        };
    }
}
