using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>Unregistered, read-only BusinessMessage canonical adapter. No writer or capability is enabled.</summary>
internal sealed class BusinessMessageCanonicalCodec : RuntimeIngressCanonicalCodec
{
    internal override int HeaderVersion => BusinessMessageAcceptedHeader.Version;
    internal override int ParticipantVersion => 1;
    internal override IReadOnlyList<int> ClaimableSourceStatuses => [BusinessMessageSourceState.Open];
    internal override IReadOnlyList<RuntimeDeliverySupportedWork> Work => [new(BusinessMessageDefinitionWork.Kind, RuntimeDeliveryWorkRole.Plan)];

    internal override RuntimeDeliveryCanonicalHeader ReadHeader(string contentJson)
    {
        var source = BusinessMessageSourceCodec.Decode(contentJson); var header = source.Header;
        // Accepted header v1 never mutates: independently expect immutable HeaderRevision=1. The common reader
        // compares the actual row and work binding. Mutable row Revision is a separate CAS, never echoed as proof.
        var snapshot = new RuntimeDeliverySourceSnapshot(header.Address, HeaderVersion, BusinessMessageSourceState.ImmutableHeaderRevision,
            BusinessMessageAcceptedHeaderCodec.Fingerprint(header), BusinessMessageWire.Element(BusinessMessageAcceptedHeaderCodec.Encode(header)),
            header.Context.Authority, header.Context.TenantId, header.Context.RunKind, header.Context.TestScope);
        return new(snapshot, header.Context, source.Status);
    }

    internal override RuntimeDeliveryCanonicalWork ReadWork(string contentJson)
    {
        var work = BusinessMessageDefinitionWork.Decode(contentJson);
        return new(work.Source, work.WorkId, BusinessMessageDefinitionWork.Kind, RuntimeDeliveryWorkRole.Plan,
            BusinessMessageWire.Element(work.Encode()), work.Fingerprint(), work.NotBefore);
    }

    internal override void ValidateClaimable(string contentJson, RuntimeDeliveryCanonicalWork work)
    {
        var source = BusinessMessageSourceCodec.Decode(contentJson);
        var canonical = BusinessMessageDefinitionWork.Decode(work.Content.GetRawText());
        if (work.Source != canonical.Source || work.WorkId != canonical.WorkId || work.Kind != BusinessMessageDefinitionWork.Kind ||
            work.Role != RuntimeDeliveryWorkRole.Plan || work.Fingerprint != canonical.Fingerprint() || work.NotBefore != canonical.NotBefore)
            throw BusinessMessageSourceState.Invalid();
        canonical.ValidatePlanningBinding(source);
    }
}
