using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Converts one Elsa 3 node and its non-graph descendants into an Elsa 4 <see cref="ActivityNode"/>. Graph conversion
/// (#2705) calls it for each Flowchart child; graph-level members (connections, start, join and fork) are left to the
/// caller. Single implementation per application.
/// </summary>
/// <remarks>
/// The node is converted as it stands: definition-wide value-flow lowering is <see cref="IElsa3DefinitionConverter"/>'s
/// job, so a caller converting a whole definition lowers first. Never throws for input or catalog content.
/// </remarks>
public interface IElsa3NodeConverter
{
    ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(
        Elsa3NodeConversionContext context,
        Elsa3Activity node,
        CancellationToken cancellationToken = default);
}

/// <summary>The conversion scope a node is converted in: its definition, whether it is reusable, and the plan's replacements.</summary>
public sealed record Elsa3NodeConversionContext(Elsa3ConversionRequest Request);
