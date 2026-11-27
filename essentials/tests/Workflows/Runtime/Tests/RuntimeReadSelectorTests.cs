using System.Text.Json;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>The exact-segment member selector of #4078: what each shape of source and path yields.</summary>
public sealed class RuntimeReadSelectorTests
{
    private const string Customer = """
        {"customerId":"c-1","CustomerId":"pascal","address":{"street":"Main","zip":null,"lines":["a","b"]},
         "a.b":"dotted","a":{"b":"nested"},"0":"zero","":"empty","count":3,"active":true,"nothing":null,
         "tags":["x"],"name":"text","map":{"k1":{"v":1},"k 2":{"v":2}}}
        """;

    public static TheoryData<string, string[], string, int, string?> Rows => new()
    {
        // json, segments, outcome, segment index, selected raw JSON
        { Customer, ["customerId"], "Present", 0, "\"c-1\"" },
        { Customer, ["CustomerId"], "Present", 0, "\"pascal\"" },
        { Customer, ["customerid"], "Absent", 0, null },
        { Customer, ["address", "street"], "Present", 1, "\"Main\"" },
        { Customer, ["address"], "Present", 0, """{"street":"Main","zip":null,"lines":["a","b"]}""" },
        { Customer, ["address", "lines"], "Present", 1, """["a","b"]""" },
        { Customer, ["count"], "Present", 0, "3" },
        { Customer, ["active"], "Present", 0, "true" },
        { Customer, ["name"], "Present", 0, "\"text\"" },
        { Customer, ["a.b"], "Present", 0, "\"dotted\"" },
        { Customer, ["a", "b"], "Present", 1, "\"nested\"" },
        { Customer, ["0"], "Present", 0, "\"zero\"" },
        { Customer, [""], "Present", 0, "\"empty\"" },
        { Customer, ["map", "k 2", "v"], "Present", 2, "2" },
        { Customer, ["nothing"], "Null", 0, null },
        { Customer, ["address", "zip"], "Null", 1, null },
        { Customer, ["nothing", "deeper"], "Null", 0, null },
        { Customer, ["missing"], "Absent", 0, null },
        { Customer, ["address", "missing"], "Absent", 1, null },
        { Customer, ["address", "missing", "deeper"], "Absent", 1, null },
        { Customer, ["tags", "0"], "NotAnObject", 1, null },
        { Customer, ["name", "length"], "NotAnObject", 1, null },
        { Customer, ["count", "x"], "NotAnObject", 1, null },
        { Customer, ["active", "x"], "NotAnObject", 1, null },
        { Customer, ["address", "lines", "0"], "NotAnObject", 2, null },
        { """{"k":"first","k":"last"}""", ["k"], "Present", 0, "\"last\"" },
        { """{"k":{"v":"first"},"k":{"w":"last"}}""", ["k", "v"], "Absent", 1, null },
        { """["a"]""", ["0"], "NotAnObject", 0, null },
        { "\"text\"", ["x"], "NotAnObject", 0, null },
        { "null", ["x"], "Null", -1, null }
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void A_selection_yields_its_exact_member_or_says_where_it_stopped(string json, string[] segments, string outcome, int index, string? selected)
    {
        using var document = JsonDocument.Parse(json);

        var result = RuntimeReadSelector.Select(document.RootElement, segments);

        Assert.Equal(outcome, result.Outcome.ToString());
        Assert.Equal(index, result.SegmentIndex);
        if (selected is not null)
            Assert.Equal(selected, result.Value.GetRawText());
    }

    [Fact]
    public void An_explicit_null_source_stops_before_the_first_segment()
    {
        var result = RuntimeReadSelector.Select(null, ["x"]);

        Assert.Equal((RuntimeReadSelectionOutcome.Null, -1), (result.Outcome, result.SegmentIndex));
    }

    /// <summary>
    /// Selection matches names exactly, where hydration of the whole value would not: the activity-value profile
    /// hydrates <c>CustomerName</c> into a <c>CustomerName</c> property whatever the casing, a member read does not.
    /// </summary>
    [Fact]
    public void Selection_is_case_sensitive_where_whole_value_hydration_is_not()
    {
        using var document = JsonDocument.Parse("""{"CustomerName":"pascal"}""");

        var hydrated = document.RootElement.Deserialize<Named>(Elsa.Activities.Runtime.Core.Serialization.ActivityValueJsonProfile.CreateOptions())!;
        var selected = RuntimeReadSelector.Select(document.RootElement, ["customerName"]);

        Assert.Equal("pascal", hydrated.CustomerName);
        Assert.Equal(RuntimeReadSelectionOutcome.Absent, selected.Outcome);
    }

    [Fact]
    public void A_path_of_the_maximum_depth_is_walked_to_its_end()
    {
        var json = string.Concat(Enumerable.Repeat("{\"n\":", 63)) + "\"deep\"" + new string('}', 63);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });

        var result = RuntimeReadSelector.Select(document.RootElement, Enumerable.Repeat("n", 63).ToArray());

        Assert.Equal("\"deep\"", result.Value.GetRawText());
    }

    [Fact]
    public void The_selected_member_outlives_its_source_document()
    {
        RuntimeReadSelectionResult result;
        using (var document = JsonDocument.Parse("""{"a":{"b":[1,2]}}"""))
            result = RuntimeReadSelector.Select(document.RootElement, ["a", "b"]);

        Assert.Equal("[1,2]", result.Value.GetRawText());
    }

    private sealed record Named(string? CustomerName);
}
