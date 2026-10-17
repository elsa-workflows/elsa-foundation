namespace Elsa.UnifiedExecution.WorkProof;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] == "proof")
            return await ProofHarness.RunAsync(args.Skip(1).ToArray());
        return args[0] switch
        {
            "--worker" => await WorkerCommands.RunWorkerAsync(args),
            "--seed-dispatch-crash" => await WorkerCommands.RunSeedDispatchCrashAsync(args),
            "--before-dispatch-crash" => WorkerCommands.RunBeforeDispatchCrash(args),
            "--lost-commit-reply" => WorkerCommands.RunLostCommitReplyAsync(args),
            "--mutant" => MutationCommands.Run(args),
            _ => throw new ArgumentException($"Unknown command '{args[0]}'.")
        };
    }
}
