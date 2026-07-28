namespace Elsa.Workflows.Design.Api.Constants;

internal static class RouteConstants
{
    internal const string DomainPrefix = "design/workflows";

    internal static string GetRoute(string path) => string.Join('/', DomainPrefix, path.TrimStart('/'));

    internal static string Definitions => GetRoute("definitions");

    internal static string Versions => GetRoute("versions");

    internal static string ScopedVariableAnalysis => GetRoute("scoped-variables/analyze");

    internal static string ActivityInputOptions => GetRoute("activities/{activityVersionId}/inputs/{inputName}/options");

    internal static string ExpressionToolingContext => GetRoute("expression-tooling/context");
    internal static string ExpressionToolingDescriptors => GetRoute("expression-tooling/descriptors");
    internal static string ExpressionToolingSymbols => GetRoute("expression-tooling/symbols");
    internal static string ExpressionToolingCompletions => GetRoute("expression-tooling/completions");
    internal static string ExpressionToolingHover => GetRoute("expression-tooling/hover");
    internal static string ExpressionToolingValidate => GetRoute("expression-tooling/validate");
}
