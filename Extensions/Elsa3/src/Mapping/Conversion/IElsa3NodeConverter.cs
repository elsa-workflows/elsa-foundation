using Elsa.Workflows.Design.Core.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Converts Elsa 3 nodes one at a time, for graph conversion (#2705) to call per Flowchart child; graph-level members
/// (connections, start, join and fork) are left to the caller. Single implementation per application.
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
/// A graph-level node (a Flowchart, or a member only graph conversion can place) does not stop the rest of the
/// definition from being prepared: it is deferred, reported in <see cref="Elsa3NodeConversionContext.Deferred"/>, and
/// its children are prepared like any other node. Never throws for input or catalog content.
/// </para>
/// </remarks>
public interface IElsa3NodeConverter
{
    /// <summary>
    /// Prepares <paramref name="request"/>'s definition for node conversion. Fails with every diagnostic when anything
    /// other than a deferred graph-level node is unconvertible; on success the diagnostics are the warnings.
    /// </summary>
    ValueTask<Elsa3MigrationResult<Elsa3NodeConversionContext>> PrepareAsync(
        Elsa3ConversionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts <paramref name="node"/> (matched by node id within the prepared definition) and its non-graph
    /// descendants. Fails with the node's diagnostics when the node or a descendant is graph-level, so a graph never
    /// loses a child silently; on success the diagnostics are the node's warnings.
    /// </summary>
    /// <exception cref="ArgumentException">The node is not part of the definition <paramref name="context"/> was prepared from.</exception>
    ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(
        Elsa3NodeConversionContext context,
        Elsa3Activity node,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A definition prepared for node conversion by <see cref="IElsa3NodeConverter.PrepareAsync"/>: normalized and
/// lowered definition-wide. Only a node converter creates one.
/// </summary>
public sealed class Elsa3NodeConversionContext
{
    internal Elsa3NodeConversionContext(
        Elsa3ConversionRequest request,
        Elsa3ConversionPlan plan,
        IReadOnlyList<Elsa3MigrationDiagnostic> deferred)
    {
        Request = request;
        Plan = plan;
        Deferred = deferred;
    }

    /// <summary>The request the definition was prepared from.</summary>
    public Elsa3ConversionRequest Request { get; }

    /// <summary>
    /// The graph-level nodes left to the caller, one <c>ELS3-ACTIVITY-GRAPH-CONVERSION-PENDING</c> diagnostic each, with
    /// the node id in its metadata.
    /// </summary>
    public IReadOnlyList<Elsa3MigrationDiagnostic> Deferred { get; }

    internal Elsa3ConversionPlan Plan { get; }
}
