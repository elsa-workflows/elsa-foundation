using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// The default runtime secret mask (spec 188, T072, FR-012): a value resolved for one activity execution is replaced by
/// <c>[secret:&lt;name&gt;]</c> in text produced for that execution, as written and JSON-escaped, and in no other
/// execution's text; an empty value is ignored, and every non-empty value is masked however short.
/// </summary>
public sealed class RuntimeSecretMaskTests
{
    private const string Execution = "actexec-1";
    private const string OtherExecution = "actexec-2";
    private const string ReferenceName = "payments.api-key";
    private const string Marker = "[secret:payments.api-key]";

    private readonly DefaultRuntimeSecretMask _mask = new();
    private readonly string _value = $"canary{Guid.NewGuid():N}";

    [Fact]
    public void A_registered_value_is_replaced_by_its_reference_marker_wherever_it_occurs()
    {
        _mask.Register(Execution, ReferenceName, _value);

        var masked = _mask.Mask(Execution, $"before {_value} middle {_value} after");

        Assert.Equal($"before {Marker} middle {Marker} after", masked);
        Assert.True(_mask.HasRegistrations(Execution));
    }

    [Fact]
    public void A_value_is_masked_in_the_json_the_default_encoder_writes()
    {
        // The default encoder writes '+' and '"' as + and ", so the value is not in the text as written.
        var value = $"{_value}+\"quoted";
        _mask.Register(Execution, ReferenceName, value);
        var json = JsonSerializer.Serialize(new { token = value });
        Assert.DoesNotContain(value, json, StringComparison.Ordinal);

        var masked = _mask.Mask(Execution, json);

        Assert.Equal($$"""{"token":"{{Marker}}"}""", masked);
    }

    [Fact]
    public void A_value_is_masked_in_the_json_the_relaxed_encoder_writes()
    {
        // The relaxed encoder writes '"' as \" and leaves '+' alone: a third form, unlike the raw and the default one.
        var value = $"{_value}+\"quoted";
        _mask.Register(Execution, ReferenceName, value);
        var json = JsonSerializer.Serialize(new { token = value }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.DoesNotContain(value, json, StringComparison.Ordinal);

        var masked = _mask.Mask(Execution, json);

        Assert.Equal($$"""{"token":"{{Marker}}"}""", masked);
    }

    [Fact]
    public void An_empty_value_is_ignored()
    {
        _mask.Register(Execution, ReferenceName, string.Empty);
        const string text = "nothing to mask here";

        Assert.False(_mask.HasRegistrations(Execution));
        Assert.Same(text, _mask.Mask(Execution, text));
    }

    [Fact]
    public void A_one_character_value_is_masked_wherever_it_occurs()
    {
        _mask.Register(Execution, "short", "q");

        Assert.Equal("[secret:short]ui[secret:short]", _mask.Mask(Execution, "quiq"));
    }

    [Fact]
    public void Text_without_a_registered_value_is_returned_unchanged()
    {
        _mask.Register(Execution, ReferenceName, _value);
        const string text = "an unrelated failure";

        Assert.Same(text, _mask.Mask(Execution, text));
    }

    [Fact]
    public void A_value_registered_for_one_execution_is_never_applied_to_another()
    {
        _mask.Register(Execution, ReferenceName, _value);
        var text = $"other execution saw {_value}";

        Assert.Same(text, _mask.Mask(OtherExecution, text));
        Assert.False(_mask.HasRegistrations(OtherExecution));
        Assert.Equal($"other execution saw {Marker}", _mask.Mask(Execution, text));
    }

    [Fact]
    public void Release_forgets_one_execution_and_keeps_the_others()
    {
        var otherValue = $"canary{Guid.NewGuid():N}";
        _mask.Register(Execution, ReferenceName, _value);
        _mask.Register(OtherExecution, "payments.webhook-key", otherValue);

        _mask.Release(Execution);

        Assert.False(_mask.HasRegistrations(Execution));
        Assert.Equal($"released {_value}", _mask.Mask(Execution, $"released {_value}"));
        Assert.True(_mask.HasRegistrations(OtherExecution));
        Assert.Equal("kept [secret:payments.webhook-key]", _mask.Mask(OtherExecution, $"kept {otherValue}"));
    }

    [Fact]
    public void An_inserted_marker_is_never_masked_again()
    {
        // "secret" is part of every marker; a second pass over the text would mask inside the first marker.
        _mask.Register(Execution, ReferenceName, _value);
        _mask.Register(Execution, "other", "secret");

        Assert.Equal($"{Marker} and [secret:other]", _mask.Mask(Execution, $"{_value} and secret"));
    }

    [Fact]
    public void The_longest_value_wins_where_two_registered_values_overlap()
    {
        // Registered shortest first, so registration order cannot be what decides.
        var longer = $"{_value}tail";
        _mask.Register(Execution, "short", _value);
        _mask.Register(Execution, "long", longer);

        Assert.Equal("x [secret:long] y [secret:short]", _mask.Mask(Execution, $"x {longer} y {_value}"));
    }
}
