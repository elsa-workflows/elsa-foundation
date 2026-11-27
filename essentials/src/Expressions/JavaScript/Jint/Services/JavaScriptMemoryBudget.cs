using Elsa.Expressions.JavaScript.Jint.Options;

namespace Elsa.Expressions.JavaScript.Jint.Services;

/// <summary>The bytes each <see cref="JavaScriptMemoryProfile"/> allows one evaluation to allocate.</summary>
internal static class JavaScriptMemoryBudget
{
    private const long MiB = 1024 * 1024;

    /// <summary>Whether a configured profile is one of the named profiles; a number bound from configuration need not be.</summary>
    public static bool IsDefined(JavaScriptMemoryProfile profile) => Enum.IsDefined(profile);

    public static long Bytes(JavaScriptMemoryProfile profile) => profile switch
    {
        JavaScriptMemoryProfile.Low => 256 * MiB,
        JavaScriptMemoryProfile.Medium => 512 * MiB,
        JavaScriptMemoryProfile.Maximum => 1024 * MiB,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown JavaScript memory profile."),
    };
}
