using BenchmarkDotNet.Running;

namespace Elsa.Activities.Runtime.Benchmarks;

/// <summary>
/// Discovers and runs the activation-scope benchmarks supplied by the value-flow redesign.
/// </summary>
internal static class Program
{
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
