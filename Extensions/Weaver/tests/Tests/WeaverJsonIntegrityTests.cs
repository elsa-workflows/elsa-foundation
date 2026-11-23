using System.Text.Json;
using Elsa.Weaver.Core.Models;
using Elsa.Weaver.Services;
using Elsa.Weaver.Api;
using Elsa.Weaver.Api.Models;

namespace Elsa.Weaver.Tests;

public sealed class WeaverJsonIntegrityTests
{
    [Fact]
    public void Api_attachment_input_and_output_keep_the_same_raw_data_contract()
    {
        const string raw = """{ "OrderID":9007199254740993, "nil":null, "text":"Ren\u00e9", "amount":1.00 }""";
        var context = new WeaverJsonContext(Elsa.Serialization.Core.JsonBaseline.CreateCopy());
        var body = "{\"id\":\"raw\",\"content\":" + raw + "}";
        var request = Assert.IsType<WeaverContextAttachmentRequest>(JsonSerializer.Deserialize(body,
            context.GetTypeInfo(typeof(WeaverContextAttachmentRequest))!));
        Assert.Equal(raw, request.Content!.Value.GetRawText());
        var response = request.ToDomain().ToResponse();
        Assert.Equal(raw, response.Content!.Value.GetRawText());
        var wire = JsonSerializer.SerializeToElement(response, Elsa.Serialization.Core.JsonBaseline.Options);
        Assert.Equal(raw, wire.GetProperty("content").GetRawText());
        Assert.Equal(9007199254740993L, wire.GetProperty("content").GetProperty("OrderID").GetInt64());
    }

    [Fact]
    public void Retained_disclosure_rebuilds_enums_and_raw_data_with_the_same_value_digest()
    {
        const string raw = "{ \"UpperKey\": \"\\u0061\", \"number\": 100000000000000000000000000000000000001.0, \"nil\": null }";
        var json = JsonDocument.Parse(raw).RootElement.Clone();
        var target = new WeaverResourceTarget("module", "resource", "id", "1");
        var attachment = new WeaverContextAttachment("context", "source", "id", "Label", "application/json",
            WeaverContextSensitivity.Internal, "selection", "Summary", json, new Dictionary<string, string>())
        {
            ModuleId = "module", Origin = WeaverContextOrigin.AuthoritativeRead, Target = target,
            Client = new("client", "generation")
        };
        var result = new WeaverToolResult("call", "tool", true, "observed", new("payload", "1", json))
        {
            Receipt = new("invocation", target, target with { Revision = "2" }, WeaverEffectScope.ServerPersisted, new("evidence", "1", json)),
            Access = new("module", "1", target) { InvocationId = "invocation", DisclosureSourceId = "source" }
        };
        var snapshot = new WeaverDisclosureSnapshot([attachment], [result]);
        var record = WeaverDisclosureHistory.Record(snapshot);
        var restored = Assert.IsType<WeaverDisclosureSnapshot>(WeaverDisclosureHistory.Rebuild(record));
        Assert.Equal(record.Digest, WeaverInvocationIntegrity.Digest(snapshot));
        Assert.Equal(record.Digest, WeaverInvocationIntegrity.Digest(restored));
        Assert.Equal(attachment.Sensitivity, Assert.Single(restored.Context).Sensitivity);
        Assert.Equal(attachment.Origin, Assert.Single(restored.Context).Origin);
        Assert.Equal(raw, Assert.IsType<JsonElement>(Assert.Single(restored.Context).Content).GetRawText());
        Assert.Equal(raw, Assert.Single(restored.ToolResults).Receipt!.Evidence.Data.GetRawText());
        var changed = record with { Canonical = record.Canonical.Replace("000000001.0", "000000002.0", StringComparison.Ordinal) };
        Assert.Null(WeaverDisclosureHistory.Rebuild(changed));
    }

    [Fact]
    public void Retained_typed_numbers_remain_readable_and_hash_equivalent()
    {
        var value = new WeaverTurnStarted("turn", 10);
        var retained = WeaverInvocationIntegrity.Canonical(value);
        Assert.Contains("\"maxSteps\":10", retained, StringComparison.Ordinal);
        Assert.Equal(value, JsonSerializer.Deserialize<WeaverTurnStarted>(retained, WeaverInvocationIntegrity.SnapshotOptions));
        Assert.Equal(WeaverInvocationIntegrity.Digest(value), WeaverInvocationIntegrity.DigestCanonical(retained));
    }
}
