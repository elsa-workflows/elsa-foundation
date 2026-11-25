using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Studio.Dashboard.Core.Models;
using Xunit;

namespace Elsa.Weaver.Dashboard.Tests;

public sealed class DashboardWeaverHashGoldenTests
{
    private const string Freeze = "JSON hashes freeze at Elsa 4's first stable release; before then regenerate goldens and fixtures together, never add an old-hash verifier.";

    public static IEnumerable<object[]> Vectors()
    {
        using var stream = typeof(DashboardWeaverHashGoldenTests).Assembly.GetManifestResourceStream("S9DashboardWeaverHashVectors.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.EnumerateArray().Select(item => new object[] { item.Clone() }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Actual_fingerprint_pins_JSON_and_both_owner_contracts(JsonElement vector)
    {
        var source = vector.GetProperty("json").GetString()!;
        using var json = JsonDocument.Parse(source);
        var actual = vector.GetProperty("consumer").GetString() switch
        {
            "question" => DashboardWeaverProtocol.Fingerprint(JsonSerializer.Deserialize<DashboardQueryRequest>(source, JsonBaseline.Options)!),
            "dataset" => DashboardWeaverProtocol.Fingerprint(JsonSerializer.Deserialize<DashboardDatasetDeclaration>(source, JsonBaseline.Options)!),
            _ => DashboardWeaverProtocol.Fingerprint(json.RootElement)
        };
        Assert.True(actual == vector.GetProperty("sha256").GetString(), vector.GetProperty("name").GetString() + ": " + Freeze);
    }

    [Fact]
    public void Wire_JSON_retains_raw_data_and_explicit_null_before_hashing()
    {
        const string raw = "{ \"OwnerKey\": \"Ren\\u00e9\", \"n\": 1.0, \"nil\": null }";
        using var document = JsonDocument.Parse(raw);
        var carried = DashboardWeaverProtocol.Element(new { Data = document.RootElement });
        Assert.Equal(raw, carried.GetProperty("data").GetRawText());
        Assert.Equal(JsonValueKind.Null, carried.GetProperty("data").GetProperty("nil").ValueKind);
        Assert.Equal(JsonValueKind.String, DashboardWeaverProtocol.InstalledGuidance().Content!.Value.ValueKind);
    }
}
