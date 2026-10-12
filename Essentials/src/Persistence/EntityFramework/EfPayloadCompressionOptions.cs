namespace Elsa.Persistence.EntityFramework;

/// <summary>
/// One module's payload-column encoding. Carried on <see cref="Microsoft.EntityFrameworkCore.DbContextOptions"/>
/// by <see cref="EfPayloadCompressionOptionsExtension"/>, because a derived context is constructed with nothing else.
/// </summary>
public sealed class EfPayloadCompressionOptions
{
    /// <summary>
    /// The length below which a value is written as plaintext without attempting a frame.
    /// <para>
    /// A policy default, open to change: no measurement stands behind it, and none is claimed. It is a cost control
    /// rather than a correctness rule, because <see cref="EfPayloadCodec.Encode"/> already refuses a frame that is
    /// not shorter than its plaintext, so every value of this produces correct output.
    /// </para>
    /// </summary>
    public const int DefaultMinimumLength = 512;

    /// <summary>What this module writes. Reading is unaffected: every frame names its own codec.</summary>
    public EfPayloadCompression Codec { get; init; } = EfPayloadCompression.None;

    /// <summary>See <see cref="DefaultMinimumLength"/>.</summary>
    public int MinimumLength { get; init; } = DefaultMinimumLength;

    /// <summary>What every module ships with, and what a host that configures nothing gets.</summary>
    public static EfPayloadCompressionOptions Plaintext { get; } = new();

    internal void Validate()
    {
        if (!Enum.IsDefined(Codec))
            throw new ArgumentOutOfRangeException(nameof(Codec), Codec, "Unknown payload codec.");
        ArgumentOutOfRangeException.ThrowIfNegative(MinimumLength, nameof(MinimumLength));
    }
}
