using System.Text;

namespace Kernel.Foundation.InMemory;

/// <summary>The opaque actor metadata budget, expressed in UTF-8 bytes before provider encoding.</summary>
internal static class HeaderLimits
{
    internal const int MaxTypeDataBytes = 16_384;
    internal const int MaxLabels = 4;
    internal const int MaxLabelBytes = 1_024;
    internal const int VariableHeaderReserve = MaxTypeDataBytes + 2 * MaxLabels * MaxLabelBytes;

    internal static bool FitsTypeData(string typeData) => Encoding.UTF8.GetByteCount(typeData) <= MaxTypeDataBytes;

    internal static void ValidateTypeData(string typeData)
    {
        if (!FitsTypeData(typeData))
            throw new ArgumentException("An actor may store at most 16 KiB of UTF-8 type data.", nameof(typeData));
    }
}
