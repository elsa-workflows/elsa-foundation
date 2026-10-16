using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>The default node converter's context: the definition normalized and lowered definition-wide.</summary>
internal sealed class Elsa3PreparedNodeConversionContext(
    Elsa3ConversionRequest request,
    Elsa3ConversionPlan plan,
    IReadOnlyList<Elsa3MigrationDiagnostic> deferred) : Elsa3NodeConversionContext(request, deferred)
{
    public Elsa3ConversionPlan Plan { get; } = plan;
}
