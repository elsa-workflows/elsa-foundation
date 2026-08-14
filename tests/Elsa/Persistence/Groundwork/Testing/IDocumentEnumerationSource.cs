using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Test-only enumeration over a document double's contents. Bounded-query test evaluators filter a whole
/// document kind in memory, which no <see cref="IDocumentStore"/> member offers — the real providers answer
/// that through a compiled route instead. Exposing it as its own seam keeps the evaluators off Groundwork's
/// query surfaces entirely, which is the point: they are what stands in for one.
/// </summary>
public interface IDocumentEnumerationSource
{
    IReadOnlyCollection<DocumentEnvelope> Snapshot(string documentKind);
}
