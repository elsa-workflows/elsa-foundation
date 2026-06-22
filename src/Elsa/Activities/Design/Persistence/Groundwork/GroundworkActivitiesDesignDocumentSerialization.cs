using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Entities;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Persistence.Groundwork;

/// <summary>
/// JSON serialization settings for the <b>rich</b> <c>ActivityDefinitionVersion</c> Groundwork document. It
/// applies the shared <see cref="GroundworkDocumentSerialization"/> domain-projection model with the
/// activity-version specifics: the authored projection collections (<see cref="InputDefinition"/>,
/// <see cref="OutputDefinition"/>, <see cref="ActivityDesignFacet"/>) are delegated to
/// <see cref="IPayloadSerializer"/> so their polymorphic expression graphs round-trip with the same type
/// metadata the relational provider uses; the EF shadow source strings
/// (<c>DescriptorPayloadSource</c>/<c>InputsSource</c>/<c>OutputsSource</c>/<c>DesignFacetsSource</c>), the
/// navigation property (<c>Definition</c>) and the relational <c>RowNumber</c> are excluded.
/// <para>
/// <c>DescriptorPayload</c> is a <see cref="JsonElement"/> and is serialized natively — it is already opaque
/// JSON, so it needs neither exclusion nor payload delegation.
/// </para>
/// </summary>
public static class GroundworkActivitiesDesignDocumentSerialization
{
    private static readonly string[] ExcludedMembers =
    [
        nameof(Entity.RowNumber),
        "DescriptorPayloadSource",
        "InputsSource",
        "OutputsSource",
        "DesignFacetsSource",
        "Definition",
    ];

    private static readonly Type[] PayloadDelegatedTypes =
    [
        typeof(IEnumerable<InputDefinition>),
        typeof(IEnumerable<OutputDefinition>),
        typeof(IEnumerable<ActivityDesignFacet>),
    ];

    /// <summary>Creates options for <c>ActivityDefinitionVersion</c>, bound to the host's payload serializer.</summary>
    public static JsonSerializerOptions Create(IPayloadSerializer payloadSerializer) =>
        GroundworkDocumentSerialization.Create(payloadSerializer, ExcludedMembers, PayloadDelegatedTypes);
}
