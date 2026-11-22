using System.Text;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>The opaque actor metadata budget, expressed in UTF-8 bytes before provider encoding.</summary>
internal static class HeaderLimits
{
    internal const int MaxTypeBytes = Durable.FoundationMetadataLimits.NameBytes;
    internal const int MaxTypeDataBytes = 16_384;
    internal const int MaxLabels = 4;
    internal const int MaxLabelBytes = 1_024;
    internal const int VariableHeaderReserve = MaxTypeDataBytes + 2 * MaxLabels * MaxLabelBytes;

    internal static bool FitsStart(string type, string typeData) =>
        FitsType(type) && FitsTypeData(typeData);

    internal static void ValidateStart(string type, string typeData)
    {
        if (!FitsType(type))
            throw new ArgumentException("An actor type may occupy at most 1,024 UTF-8 bytes.", nameof(type));
        ValidateTypeData(typeData);
    }

    private static bool FitsType(string type) => Durable.FoundationMetadataLimits.FitsName(type);

    private static bool FitsTypeData(string typeData) => Encoding.UTF8.GetByteCount(typeData) <= MaxTypeDataBytes;

    internal static void ValidateTypeData(string typeData)
    {
        if (!FitsTypeData(typeData))
            throw new ArgumentException("An actor may store at most 16 KiB of UTF-8 type data.", nameof(typeData));
    }
}
