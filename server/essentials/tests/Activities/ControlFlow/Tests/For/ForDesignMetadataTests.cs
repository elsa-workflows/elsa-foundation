using Elsa.Activities.Design.Reconciliation.Clr.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ForActivity = Elsa.Activities.For.Activities.For;

namespace Elsa.Activities.For.Tests;

public sealed class ForDesignMetadataTests
{
    [Fact]
    public void ClrScan_ExposesInputsInPresentationOrder_WithStepDefault()
    {
        var scanner = new ClrAssemblyScanner(
            new ActivityTypeVersionResolver(),
            new ActivityTypeCategoryResolver(),
            NullLogger<ClrAssemblyScanner>.Instance);

        var model = scanner.Scan(AppContext.BaseDirectory).Single(m => m.ActivityTypeKey == typeof(ForActivity).FullName);
        var orderedNames = model.Inputs
            .OrderBy(input => input.Order)
            .ThenBy(input => input.Name, StringComparer.Ordinal)
            .Select(input => input.Name)
            .ToArray();
        var inputs = model.Inputs.ToDictionary(input => input.Name, StringComparer.Ordinal);

        Assert.Equal(new[] { "Start", "End", "Step", "EndInclusive" }, orderedNames);
        Assert.Equal(0, inputs["Start"].Order);
        Assert.Equal(10, inputs["End"].Order);
        Assert.Equal(20, inputs["Step"].Order);
        Assert.Equal(30, inputs["EndInclusive"].Order);
        Assert.Equal(1, inputs["Step"].DefaultValue?.GetInt32());
        Assert.Equal("Literal", inputs["Step"].DefaultSyntax);
    }
}
