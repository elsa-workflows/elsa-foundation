using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class ActivityBindingConversionPreviewerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Incomplete_profile_and_invalid_limits_leave_the_input_editable(bool incompleteProfile)
    {
        var request = incompleteProfile
            ? new AuthoredValueConversionRequest(AuthoredValueConversionMode.Profile, new("contract", ""))
            : new AuthoredValueConversionRequest(Limits: new(MaxDepth: 0));
        var previewer = new ActivityBindingConversionPreviewer(new ValueConversionPlanResolver());

        var preview = previewer.Preview(new("Double"), ValueRepresentation.TypedValue, new("String"), request);

        Assert.Equal("InvalidConversionSettings", preview.ReasonCode);
        Assert.Null(preview.Operation);
        Assert.NotNull(preview.Rejection);
    }
}
