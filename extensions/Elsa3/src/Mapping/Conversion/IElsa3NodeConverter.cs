using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Converts Elsa 3 nodes one at a time from a definition prepared as a whole. A flowchart arrives with its connections
/// and start node (spec 239); a FlowFork, FlowJoin or Start has no node of its own and is refused. Single implementation
/// per application.
/// </summary>
/// <remarks>
/// <para>
/// Node conversion follows the same rule as whole-definition conversion: <see cref="PrepareAsync"/> normalizes every
/// node of the definition and lowers its value flow definition-wide, exactly as
/// <see cref="IElsa3DefinitionConverter.ConvertAsync"/> does, and <see cref="ConvertNodeAsync"/> maps a node from that
/// lowered result. An expression is therefore lowered or refused the same way on both paths, and value flow that crosses
/// sibling nodes resolves.
/// </para>
/// <para>
/// Flowchart graph conversion runs during preparation (spec 239). Never throws for input or catalog content.
/// </para>
/// </remarks>
public interface IElsa3NodeConverter
{
    /// <summary>
    /// Prepares <paramref name="request"/>'s definition for node conversion. Fails with every diagnostic when anything
    /// is unconvertible; on success the diagnostics are the warnings.
    /// </summary>
    ValueTask<Elsa3MigrationResult<Elsa3NodeConversionContext>> PrepareAsync(
        Elsa3ConversionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts <paramref name="node"/> (matched by node id within the prepared definition) and its descendants. Fails
    /// with the node's diagnostics when the node or a descendant is unconvertible, so a graph never loses a child
    /// silently, and fails for a FlowFork, FlowJoin or Start, which has no node of its own; on success the diagnostics
    /// are the node's warnings.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="context"/> was not created by this converter, or the node is not part of the definition it was
    /// prepared from.
    /// </exception>
    ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(
        Elsa3NodeConversionContext context,
        Elsa3Activity node,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A definition prepared for node conversion by <see cref="IElsa3NodeConverter.PrepareAsync"/>. Each node converter
/// derives its own context to carry what its <see cref="IElsa3NodeConverter.PrepareAsync"/> computed, and accepts in
/// <see cref="IElsa3NodeConverter.ConvertNodeAsync"/> only the contexts it created.
/// </summary>
public abstract class Elsa3NodeConversionContext
{
    /// <summary>Creates a context for <paramref name="request"/>.</summary>
    protected Elsa3NodeConversionContext(Elsa3ConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Request = request;
    }

    /// <summary>The request the definition was prepared from.</summary>
    public Elsa3ConversionRequest Request { get; }
}
