namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// XML 1.0 Fifth Edition productions 4/4a, excluding colon for Namespaces 1.0 NCName.
/// XmlConvert.VerifyNCName rejects some Fifth Edition names (including U+203F/U+2040).
/// This validates identity values, without renaming XML or applying reserved-prefix rules.
/// https://www.w3.org/TR/xml/#NT-NameStartChar
/// https://www.w3.org/TR/xml-names/#NT-NCName
/// </summary>
internal static class BpmnXmlIdentity
{
    internal static bool IsValid(string value)
    {
        if (value.Length == 0) return false;
        var first = true;
        for (var offset = 0; offset < value.Length; offset++)
        {
            var scalar = (int)value[offset];
            if (char.IsHighSurrogate(value[offset]))
            {
                if (offset + 1 >= value.Length || !char.IsLowSurrogate(value[offset + 1])) return false;
                scalar = char.ConvertToUtf32(value[offset], value[offset + 1]);
                offset++;
            }
            else if (char.IsLowSurrogate(value[offset])) return false;
            if (!Start(scalar) && (first || !Continuation(scalar))) return false;
            first = false;
        }
        return true;
    }

    private static bool Start(int scalar) => scalar is >= 'A' and <= 'Z' or '_' or >= 'a' and <= 'z'
        or >= 0xC0 and <= 0xD6 or >= 0xD8 and <= 0xF6 or >= 0xF8 and <= 0x2FF
        or >= 0x370 and <= 0x37D or >= 0x37F and <= 0x1FFF or >= 0x200C and <= 0x200D
        or >= 0x2070 and <= 0x218F or >= 0x2C00 and <= 0x2FEF or >= 0x3001 and <= 0xD7FF
        or >= 0xF900 and <= 0xFDCF or >= 0xFDF0 and <= 0xFFFD or >= 0x10000 and <= 0xEFFFF;

    private static bool Continuation(int scalar) => scalar is '-' or '.' or >= '0' and <= '9'
        or 0xB7 or >= 0x300 and <= 0x36F or >= 0x203F and <= 0x2040;
}
