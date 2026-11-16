using Elsa.Activities.Bpmn.Internal;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnXmlIdentityTests
{
    [Fact]
    public void Every_XML10_fifth_edition_start_range_keeps_both_boundaries()
    {
        (int First, int Last)[] ranges =
        [
            ('A', 'Z'), ('_', '_'), ('a', 'z'), (0xC0, 0xD6), (0xD8, 0xF6), (0xF8, 0x2FF),
            (0x370, 0x37D), (0x37F, 0x1FFF), (0x200C, 0x200D), (0x2070, 0x218F),
            (0x2C00, 0x2FEF), (0x3001, 0xD7FF), (0xF900, 0xFDCF), (0xFDF0, 0xFFFD), (0x10000, 0xEFFFF)
        ];
        foreach (var (first, last) in ranges)
        foreach (var scalar in new[] { first, last })
        {
            var character = char.ConvertFromUtf32(scalar);
            Assert.True(BpmnXmlIdentity.IsValid(character + "Name"), $"U+{scalar:X} must start an NCName");
            Assert.True(BpmnXmlIdentity.IsValid("A" + character), $"U+{scalar:X} must continue an NCName");
        }
        Assert.True(BpmnXmlIdentity.IsValid("xmlns"));
    }

    [Fact]
    public void Continuation_only_ranges_are_accepted_after_a_start_and_never_as_the_start()
    {
        foreach (var scalar in new[] { '-', '.', '0', '9', 0xB7, 0x300, 0x36F, 0x203F, 0x2040 })
        {
            var character = char.ConvertFromUtf32(scalar);
            Assert.False(BpmnXmlIdentity.IsValid(character + "Name"), $"U+{scalar:X} cannot start an NCName");
            Assert.True(BpmnXmlIdentity.IsValid("A" + character + "B"), $"U+{scalar:X} must continue an NCName");
        }
    }

    [Fact]
    public void Gaps_colons_and_malformed_surrogates_are_rejected_without_replacement_or_normalization()
    {
        foreach (var scalar in new[] { 0, 0x20, ':', '/', 0xB6, 0xD7, 0xF7, 0x37E, 0x200B, 0x200E,
                     0x203E, 0x2041, 0x206F, 0x2190, 0x2BFF, 0x2FF0, 0x3000, 0xF8FF, 0xFDD0,
                     0xFDEF, 0xFFFE, 0xFFFF, 0xF0000, 0x10FFFF })
            Assert.False(BpmnXmlIdentity.IsValid("A" + char.ConvertFromUtf32(scalar)), $"U+{scalar:X} is outside the NCName ranges");
        foreach (var malformed in new[] { "", "A:B", "\uD800", "\uDC00", "A\uD800B", "A\uDC00B", "\uD800\uD800", "\uDC00\uD800" })
            Assert.False(BpmnXmlIdentity.IsValid(malformed));
    }
}
