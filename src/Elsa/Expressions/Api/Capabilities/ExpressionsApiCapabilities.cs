using Elsa.Api.Capabilities.Models;

namespace Elsa.Expressions.Api.Capabilities;

public static class ExpressionsApiCapabilities
{
    public const string CapabilityId = "elsa.api.expressions";
    public const string SourceFeatureId = "ExpressionsApi";

    public static ApiCapabilityDeclaration StaticDeclaration { get; } = new(
        CapabilityId,
        1,
        [
            new("expression-descriptors", "expressions/descriptors"),
            new("variable-types", "expressions/variable-types")
        ],
        SourceFeatureId);
}
