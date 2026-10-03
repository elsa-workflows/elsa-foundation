using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Xunit;

namespace Elsa.Expressions.Tests;

public sealed class ExpressionToolingCatalogContractTests
{
    [Fact]
    public void Legacy_provider_uses_the_default_null_declared_catalog()
    {
        IExpressionToolingProvider provider = new LegacyProvider();

        Assert.Null(provider.DeclaredCatalog);
    }

    private sealed class LegacyProvider : IExpressionToolingProvider
    {
        public string ExpressionType => "Legacy";
        public ExpressionToolingContractVersion SupportedVersion => ExpressionToolingContractVersion.V1;
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingCapabilities>> GetCapabilitiesAsync(ExpressionToolingRequestScope scope, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionToolingItems>> GetCompletionsAsync(ExpressionCompletionRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionHover>> GetHoverAsync(ExpressionHoverRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask<ExpressionToolingOutcome<ExpressionDiagnosticSet>> ValidateAsync(ExpressionValidationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
