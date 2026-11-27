using Elsa.Expressions.JavaScript.Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>The bytes each <see cref="MemoryProfile"/> allows one evaluation to allocate.</summary>
internal static class MemoryBudget
{
    private const long MiB = 1024 * 1024;

    /// <summary>Whether a configured profile is one of the named profiles; a number bound from configuration need not be.</summary>
    public static bool IsDefined(MemoryProfile profile) => Enum.IsDefined(profile);

    public static long Bytes(MemoryProfile profile) => profile switch
    {
        MemoryProfile.Low => 256 * MiB,
        MemoryProfile.Medium => 512 * MiB,
        MemoryProfile.Maximum => 1024 * MiB,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown JavaScript memory profile."),
    };
}
