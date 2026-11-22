using System.Text;
using Contract = Kernel.Foundation.Contracts;
using Durable = Kernel.Durable;

namespace Kernel.Foundation.InMemory;

/// <summary>The opaque actor metadata budget, expressed in UTF-8 bytes before provider encoding.</summary>
internal static class HeaderLimits
{
    internal const int MaxTypeBytes = Durable.FoundationMetadataLimits.NameBytes;
    internal const int MaxTypeDataBytes = Durable.FoundationMetadataLimits.TypeDataBytes;
    internal const int MaxLabels = 4;
    internal const int MaxLabelBytes = 1_024;
    internal const int VariableHeaderReserve = MaxTypeDataBytes + 2 * MaxLabels * MaxLabelBytes;

    internal static bool FitsStart(string type, string typeData, string? input) =>
        FitsType(type) && FitsTypeData(typeData) && Durable.FoundationMetadataLimits.FitsContent(input, Durable.FoundationMetadataLimits.StartContentBytes);

    internal static void ValidateStart(string type, string typeData, string? input)
    {
        RequireName(type, nameof(type));
        ValidateTypeData(typeData);
        RequireContent(input, nameof(input), Durable.FoundationMetadataLimits.StartContentBytes);
    }

    private static bool FitsType(string type) => Durable.FoundationMetadataLimits.FitsName(type);

    private static bool FitsTypeData(string typeData) => Encoding.UTF8.GetByteCount(typeData) <= MaxTypeDataBytes;

    internal static void ValidateTypeData(string typeData)
    {
        if (!FitsTypeData(typeData))
            throw new ArgumentException("An actor may store at most 16 KiB of UTF-8 type data.", nameof(typeData));
    }

    internal static void RequireName(string value, string parameter, bool preservesCodeUnits = false)
    {
        if (!Durable.FoundationMetadataLimits.FitsName(value, preservesCodeUnits))
            throw new Contract.MetadataLimitExceeded(parameter, Durable.FoundationMetadataLimits.NameBytes);
    }

    internal static void RequireKind(string value, string parameter)
    {
        if (!Durable.FoundationMetadataLimits.FitsKind(value))
            throw new Contract.MetadataLimitExceeded(parameter, Durable.FoundationMetadataLimits.KindBytes);
    }

    internal static void RequireContent(string? value, string parameter, int limit = Durable.FoundationMetadataLimits.MessageContentBytes)
    {
        if (!Durable.FoundationMetadataLimits.FitsContent(value, limit))
            throw new Contract.MetadataLimitExceeded(parameter, limit);
    }
}
