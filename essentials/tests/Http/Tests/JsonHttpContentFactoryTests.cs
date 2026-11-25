using System.Text.Json;
using CShells;
using CShells.Features;
using Elsa.Http.Core.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Http.Tests;

public sealed class JsonHttpContentFactoryTests
{
    [Fact]
    public async Task Json_body_preserves_raw_fragments_and_selects_the_clr_contract_for_changed_values()
    {
        var services = new ServiceCollection();
        new HttpFeature(new ShellFeatureContext(new ShellSettings { Id = new ShellId("json-request-tests") }, [])).ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetServices<IHttpContentFactory>().Single(value => value.SupportedContentTypes.Contains("application/json"));
        const string raw = "{ \"UpperKey\": \"\\u0061\", \"nil\": null, \"n\": 1.0 }";
        using var document = JsonDocument.Parse(raw);
        using var rawBody = factory.CreateHttpContent(document.RootElement, "application/json");
        Assert.Equal(raw, await rawBody.ReadAsStringAsync());
        using var authoredBody = factory.CreateHttpContent(raw, "application/json");
        Assert.Equal(raw, await authoredBody.ReadAsStringAsync());
        using var typedBody = factory.CreateHttpContent(new Invoice("café <a>", null, document.RootElement), "application/json");
        var text = await typedBody.ReadAsStringAsync();
        using var typed = JsonDocument.Parse(text);
        Assert.Equal("café <a>", typed.RootElement.GetProperty("invoice_id").GetString());
        Assert.Equal(JsonValueKind.Null, typed.RootElement.GetProperty("note").ValueKind);
        Assert.Equal(raw, typed.RootElement.GetProperty("data").GetRawText());
        Assert.Contains("café <a>", text, StringComparison.Ordinal);
    }

    [JsonConversion(MemberMappings = ["invoice_id=InvoiceId"])]
    private sealed record Invoice(string InvoiceId, string? Note, JsonElement Data);
}
