using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Converts one Elsa 3 workflow definition into Elsa 4 state plus diagnostics. The single-definition import, the
/// collection materializer, graph conversion (#2705) and spec 235 all use it. Single implementation per application.
/// </summary>
/// <remarks>
/// Never throws for input or catalog content: every cause is an <c>ELS3-*</c> or <c>VF-IMP-*</c> diagnostic, complete
/// and ordered by (JSON path, code). Only cancellation escapes. Deterministic for the same input and catalog content.
/// </remarks>
public interface IElsa3DefinitionConverter
{
    ValueTask<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3ConversionRequest request,
        CancellationToken cancellationToken = default);
}
