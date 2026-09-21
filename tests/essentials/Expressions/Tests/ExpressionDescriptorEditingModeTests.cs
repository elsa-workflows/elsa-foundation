using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Expressions.Liquid.Models;
using Elsa.Expressions.Services;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class ExpressionDescriptorEditingModeTests
{
    [Fact]
    public void Built_in_expression_descriptors_declare_their_authoring_mode()
    {
        Assert.Equal(ExpressionEditingMode.Text, new JavaScriptExpressionDescriptor().EditingMode);
        Assert.Equal(ExpressionEditingMode.Text, new LiquidExpressionDescriptor().EditingMode);
        Assert.Equal(ExpressionEditingMode.Reference, new VariableExpressionDescriptor().EditingMode);
    }
}
