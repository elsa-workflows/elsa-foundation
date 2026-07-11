using System.Reflection;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Xunit;

namespace Elsa.Activities.Http.Tests;

public sealed class HttpEndpointMetadataTests
{
    [Theory]
    [InlineData(nameof(HttpEndpoint.CanStartWorkflow), "Simple", 0)]
    [InlineData(nameof(HttpEndpoint.Path), "Simple", 10)]
    [InlineData(nameof(HttpEndpoint.SupportedMethods), "Simple", 20)]
    [InlineData(nameof(HttpEndpoint.ResponseMode), "Simple", 30)]
    [InlineData(nameof(HttpEndpoint.Authorize), "Advanced", 100)]
    [InlineData(nameof(HttpEndpoint.Policy), "Advanced", 110)]
    [InlineData(nameof(HttpEndpoint.RequestTimeout), "Advanced", 120)]
    [InlineData(nameof(HttpEndpoint.RequestSizeLimit), "Advanced", 130)]
    public void Input_HasExpectedCategoryAndOrder(string propertyName, string category, float order)
    {
        var property = typeof(HttpEndpoint).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(property);
        var metadata = Assert.IsType<ActivityInputAttribute>(property.GetCustomAttribute<ActivityInputAttribute>());

        Assert.Equal(category, metadata.Category);
        Assert.Equal(order, metadata.Order);
    }
}
