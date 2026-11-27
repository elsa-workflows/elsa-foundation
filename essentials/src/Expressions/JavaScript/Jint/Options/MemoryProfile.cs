namespace Elsa.Expressions.JavaScript.Jint.Options;

/// <summary>
/// How many bytes one JavaScript evaluation may allocate (issue #5818). A host picks a profile; there is no free byte
/// value and no way to switch the limit off, so configuration cannot lift the cap past <see cref="Maximum"/>.
/// </summary>
/// <remarks>
/// The budget counts every byte the script's thread allocates, garbage included: neither .NET nor Jint can tell how much
/// memory one script holds. A script that polls the clock creates about 100–200 bytes of garbage on every pass, more per
/// second on a faster machine. The Workbench measured at least 128 MiB a second (64 MiB within a 500 ms wait, issue
/// #5818); at that rate <see cref="Medium"/> allows about 4 seconds of polling and <see cref="Maximum"/> the whole default
/// 5-second execution timeout.
/// </remarks>
public enum MemoryProfile
{
    /// <summary>256 MiB.</summary>
    Low,

    /// <summary>512 MiB. The default.</summary>
    Medium,

    /// <summary>1 GiB, the most any host can allow.</summary>
    Maximum,
}
