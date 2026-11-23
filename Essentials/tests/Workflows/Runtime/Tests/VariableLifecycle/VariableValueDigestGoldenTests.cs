using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.VariableLifecycle;

public sealed class VariableValueDigestGoldenTests
{
    private const string FreezeRule = "First-stable hash freeze: changes require an explicit identity/migration decision.";
    private static readonly ValueTypeDescriptor Type = new("Object");

    public static IEnumerable<object[]> Vectors()
    {
        using var source = typeof(VariableValueDigestGoldenTests).Assembly.GetManifestResourceStream("variable-value-hash-vectors.json")!;
        using var document = JsonDocument.Parse(source);
        return document.RootElement.EnumerateArray().Select(item => new object[] { item.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Existing_variable_digest_pins_the_independent_preimage_and_presence(JsonElement vector)
    {
        var value = vector.GetProperty("presence").GetInt32() switch
        {
            0 => ValueEnvelope.Absent(Type, ValueProtectionPolicy.InstanceInline),
            1 => ValueEnvelope.Null(Type, ValueProtectionPolicy.InstanceInline),
            _ => Parse(vector.GetProperty("json").GetString()!)
        };
        var expected = "sha256:" + vector.GetProperty("sha256").GetString();
        Assert.True(expected == Frame(value).CurrentDigestOf("value"), vector.GetProperty("name").GetString() + ": " + FreezeRule);
        Assert.True(expected == "sha256:" + JsonHashInput.HashJson(vector.GetProperty("canonical").GetString()!), FreezeRule);
    }

    [Theory]
    [InlineData("{\"total\":1.0,\"name\":\"Ren\\u00e9\"}", "{\"name\":\"René\",\"total\":1}", true)]
    [InlineData("{\"x\":-0.0}", "{ \"x\":0e123 }", true)]
    [InlineData("9007199254740993", "9007199254740992", false)]
    [InlineData("{}", "{\"x\":null}", false)]
    [InlineData("[1,2]", "[2,1]", false)]
    [InlineData("0.10000000000000000000000000001", "0.1", false)]
    public void Canonical_equality_agrees_with_existing_digest_and_no_op_commit(string left, string right, bool equal)
    {
        var first = Parse(left);
        var second = Parse(right);
        Assert.Equal(equal, JsonCanonicalForm.Equals(first.InlineValue!.Value, second.InlineValue!.Value));
        var frame = Frame(first);
        Assert.Equal(equal, frame.CurrentDigestOf("value") == Frame(second).CurrentDigestOf("value"));
        var write = frame.Commit("value", second, frame.Revision, writeBase: null, LostUpdateMode.Default);
        Assert.Equal(!equal, write.Decision.ChangesValue);
        if (equal)
        {
            Assert.Same(frame, write.Frame);
            Assert.Equal(frame.Revision, write.Frame.Revision);
            Assert.Equal(frame.VersionOf("value"), write.Frame.VersionOf("value"));
            Assert.Null(write.OverwrittenValue);
        }
    }

    private static ValueEnvelope Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ValueEnvelope.Inline(Type, document.RootElement, ValueProtectionPolicy.InstanceInline);
    }

    private static VariableFrameState Frame(ValueEnvelope value) => new("frame:golden", "workflow", "execution", null,
        VariableFrameKind.Root, new Dictionary<string, ValueEnvelope> { ["value"] = value });
}
