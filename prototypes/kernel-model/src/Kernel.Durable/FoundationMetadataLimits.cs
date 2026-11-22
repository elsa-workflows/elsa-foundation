using System.Text;

namespace Kernel.Durable;

internal static class FoundationMetadataLimits
{
    internal const int NameBytes = 1_024;
    internal const int KindBytes = 256;

    // The default UTF-8 fallback counts an unpaired UTF-16 code unit as three bytes. The permitted
    // key paths preserve the original code units separately; ordinary text has to round-trip.
    internal static bool FitsName(string value, bool preservesCodeUnits = false) =>
        Fits(value, NameBytes, preservesCodeUnits);

    internal static bool FitsKind(string value) => Fits(value, KindBytes, preservesCodeUnits: false);

    internal static void RequireName(string value, string parameter, bool preservesCodeUnits = false)
    {
        if (!FitsName(value, preservesCodeUnits)) throw new MetadataLimitExceeded(parameter, NameBytes);
    }

    internal static void RequireKind(string value, string parameter)
    {
        if (!FitsKind(value)) throw new MetadataLimitExceeded(parameter, KindBytes);
    }

    private static bool Fits(string value, int limit, bool preservesCodeUnits)
    {
        if (value.Length > limit || Encoding.UTF8.GetByteCount(value) > limit) return false;
        if (preservesCodeUnits) return true;

        for (var index = 0; index < value.Length; index++)
            if (char.IsSurrogate(value[index]) &&
                (!char.IsHighSurrogate(value[index]) || index + 1 == value.Length || !char.IsLowSurrogate(value[++index])))
                return false;
        return true;
    }
}

internal sealed class MetadataLimitExceeded(string parameter, int limit) :
    ArgumentException($"Metadata must be representable in its text encoding and fit in {limit:N0} bytes.", parameter);
