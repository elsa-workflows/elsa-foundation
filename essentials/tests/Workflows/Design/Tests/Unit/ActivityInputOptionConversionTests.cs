using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class ActivityInputOptionConversionTests
{
    [Fact]
    public void Enum_option_values_follow_the_same_contract_as_the_target_input()
    {
        var numeric = new ActivityInputOption("Choice", NumericChoice.Second);
        Assert.Equal(JsonValueKind.Number, numeric.Value.ValueKind);
        Assert.Equal(NumericChoice.Second, JsonConversionContract.Deserialize<NumericChoice>(numeric.Value));
        var named = new ActivityInputOption("Choice", NamedChoice.Second);
        Assert.Equal(JsonValueKind.String, named.Value.ValueKind);
        Assert.Equal(NamedChoice.Second, JsonConversionContract.Deserialize<NamedChoice>(named.Value));
    }

    private enum NumericChoice { First, Second }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    private enum NamedChoice { First, Second }
}
