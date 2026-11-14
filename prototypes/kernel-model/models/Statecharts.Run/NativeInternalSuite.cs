internal static class NativeInternalSuite
{
    internal static IEnumerable<string> Names => NativeInternalWitness.Names;
    internal static void Run() => NativeInternalWitness.Run();
}
