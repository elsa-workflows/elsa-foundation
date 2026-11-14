internal static class NativeInternalSuite
{
    internal static IEnumerable<string> Names => NativeInternalWitness.Names.Concat(NativeGuardWitness.Names)
        .Concat(NativeCheckpointWitness.Names).Concat(NativePreludeWitness.Names).Concat(NativeControlCutWitness.Names);

    internal static void Run()
    {
        NativeInternalWitness.Run();
        NativeGuardWitness.Run();
        NativeCheckpointWitness.Run();
        NativePreludeWitness.Run();
        NativeControlCutWitness.Run();
    }
}
