using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// The wire codec for <see cref="WorkflowArtifactClosure"/> — the one place the portable envelope's bytes are
/// decided, shared by the exporting engine and the importing one (FR-B-001 / FR-B-010).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a codec rather than "just call the payload serializer".</b> A closure carries
/// <see cref="WorkflowExecutable"/>s, and an executable recomputes <see cref="WorkflowExecutable.Nodes"/> and
/// <see cref="WorkflowExecutable.NodesById"/> from its root in its constructor. Serialized naively those
/// projections ride along, duplicating the entire activity graph in the file and — worse — making an exported
/// artifact's bytes differ from the same artifact's bytes in the durable store, which drops the projections
/// through the Groundwork runtime document serializer. Both sides encoding through one codec is what makes
/// "store-round-tripped and exported artifacts are byte-consistent" true rather than aspirational.
/// </para>
/// <para>
/// Encoding rides <c>IPayloadSerializer</c>'s options — camelCase naming, deterministic member ordering and
/// dictionary sorting, and the host's contributed converters — so the envelope inherits the engine's
/// determinism guarantees instead of asserting its own.
/// </para>
/// <para>
/// <b>Exception discipline.</b> This is a codec, not a feature boundary: a malformed document surfaces as
/// <see cref="System.Text.Json.JsonException"/>. Callers that own an identifier worth naming — the importer owns
/// a file path, the export endpoint owns a version id — wrap it in their own domain exception per §2.23.5.
/// Wrapping here would only strip the identifier the caller has and this codec does not.
/// </para>
/// </remarks>
public interface IWorkflowArtifactClosureSerializer
{
    /// <summary>Encodes <paramref name="closure"/> to the portable envelope's JSON form.</summary>
    string Serialize(WorkflowArtifactClosure closure);

    /// <summary>
    /// Decodes an envelope. Does not apply the format gate — version acceptance is the reader's fail-loud
    /// decision (T050), taken once the document has parsed far enough to read <c>formatVersion</c> at all.
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException">The document is not valid envelope JSON.</exception>
    WorkflowArtifactClosure? Deserialize(string json);
}
