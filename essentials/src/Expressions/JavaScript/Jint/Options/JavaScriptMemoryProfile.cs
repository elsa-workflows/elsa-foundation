namespace Elsa.Expressions.JavaScript.Jint.Options;

/// <summary>
/// How many bytes one JavaScript evaluation may allocate (issue #5818). A host picks a profile; there is no free byte
/// value and no way to switch the limit off, so configuration cannot lift the cap past <see cref="Maximum"/>.
/// </summary>
/// <remarks>
/// The budget counts every byte the script's thread allocates, garbage included: neither .NET nor Jint can tell how much
/// memory one script holds. A script that polls the clock creates a little garbage on every pass, more on a faster
/// machine, so the profiles are sized against the execution timeout: <see cref="Maximum"/> covers polling for the whole
/// default 5-second timeout on a fast machine, <see cref="Medium"/> most of it.
/// </remarks>
public enum JavaScriptMemoryProfile
{
    /// <summary>256 MiB.</summary>
    Low,

    /// <summary>512 MiB. The default.</summary>
    Medium,

    /// <summary>1 GiB, the most any host can allow.</summary>
    Maximum,
}
