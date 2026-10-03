using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api.Models;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class ExpressionToolingContextTransportTests
{
    [Fact]
    public void Context_response_transports_catalog_revisions_and_rich_symbol_shapes_without_new_wire_fields()
    {
        var returnShape = new ExpressionValueShape("Any", ExpressionValueKind.Unknown);
        var symbol = new ExpressionSymbol(
            "javascript:profile:JSON.parse",
            "JSON.parse",
            ExpressionSymbolKind.Function,
            new("parse(text): Any", ExpressionValueKind.Function, false),
            "Parses a JSON string.",
            [new("parse(text)", ["text"], returnShape)]);
        var document = new ExpressionAuthoringDocument("document", "draft", "node", "text", "JavaScript", "document-r1");
        var context = new ExpressionAuthoringContext(
            ExpressionToolingContractVersion.V1,
            document,
            "context-r1",
            "catalog-r1",
            [symbol],
            new(),
            PolicyFingerprint: "policy-r2",
            PermissionRevision: "permission-r3");
        var response = new ExpressionToolingContextResponse(
            ExpressionToolingOutcome<ExpressionAuthoringContext>.Success(context, ExpressionToolingContractVersion.V1, "document-r1", "context-r1"));

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var payload = JsonDocument.Parse(json);
        var contextJson = payload.RootElement.GetProperty("result").GetProperty("payload");
        var symbolJson = Assert.Single(contextJson.GetProperty("rootSymbols").EnumerateArray());
        var signatureJson = Assert.Single(symbolJson.GetProperty("signatures").EnumerateArray());

        Assert.Equal("catalog-r1", contextJson.GetProperty("symbolCatalogRevision").GetString());
        Assert.Equal("policy-r2", contextJson.GetProperty("policyFingerprint").GetString());
        Assert.Equal("permission-r3", contextJson.GetProperty("permissionRevision").GetString());
        Assert.Equal("javascript:profile:JSON.parse", symbolJson.GetProperty("symbolId").GetString());
        Assert.Equal("Parses a JSON string.", symbolJson.GetProperty("documentation").GetString());
        Assert.Equal("parse(text)", signatureJson.GetProperty("display").GetString());
        Assert.Equal("text", Assert.Single(signatureJson.GetProperty("parameters").EnumerateArray()).GetString());
        Assert.Equal("Any", signatureJson.GetProperty("returnShape").GetProperty("displayName").GetString());
    }
}
