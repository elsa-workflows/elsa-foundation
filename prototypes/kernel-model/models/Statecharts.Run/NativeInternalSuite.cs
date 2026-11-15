internal static class NativeInternalSuite
{
    internal static IEnumerable<string> Names => NativeInternalWitness.Names.Concat(NativeGuardWitness.Names)
        .Concat(NativeCheckpointWitness.Names).Concat(NativeLoopWitness.Names).Concat(NativeInternalPhaseWitness.Names)
        .Concat(NativeInternalRegionalWitness.Names).Concat(NativeInternalStructureWitness.Names).Concat(NativePriorityWitness.Names)
        .Concat(NativePreludeWitness.Names).Concat(NativeHeldLoopWitness.Names).Concat(NativeInternalReaders.ReaderNames).Concat(NativeInternalReaders.RefusalNames)
        .Concat(NativeControlCutWitness.Names);

    internal static void Run()
    {
        NativeInternalWitness.Run();
        NativeGuardWitness.Run();
        NativeCheckpointWitness.Run();
        NativeLoopWitness.Run();
        NativeInternalPhaseWitness.Run();
        NativeInternalRegionalWitness.Run();
        NativeInternalStructureWitness.Run();
        NativePriorityWitness.Run();
        NativePreludeWitness.Run();
        NativeHeldLoopWitness.Run();
        NativeInternalReaders.Run();
        NativeControlCutWitness.Run();
    }
}
