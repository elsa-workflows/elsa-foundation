using System.Text;
using System.Text.Json;
using Elsa.Rules.Core;

namespace Elsa.Rules.Persistence.Core;

public static partial class RuleRecordCodec
{
    /// <summary>Computes the versioned digest of the complete canonical native result address.</summary>
    public static string DestinationIdentity(RuleNativeResultAddress destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (destination.FormatVersion != 1)
            throw new NotSupportedException("rules.destination-schema");
        var bytes = RuleJson.Write(writer => WriteDestination(writer, destination), RuleLimits.MaxStateMetadataBytes);
        var prefix = Encoding.UTF8.GetBytes("rules.destination:1:");
        var framed = new byte[prefix.Length + bytes.Length];
        prefix.CopyTo(framed, 0);
        bytes.CopyTo(framed, prefix.Length);
        return Hash(framed);
    }

    /// <summary>Writes the bounded canonical durable proof retained by a delivery acknowledgement.</summary>
    public static byte[] DeliveryAcknowledgementBytes(RuleDeliveryProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        if (!Enum.IsDefined(proof.Kind))
            throw new InvalidDataException("rules.delivery-proof-kind");
        return RuleJson.Write(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("kind", proof.Kind.ToString());
            writer.WriteString("destinationIdentity", proof.DestinationIdentity);
            writer.WriteString("terminalId", proof.TerminalId);
            writer.WriteString("payloadSha256", proof.PayloadSha256);
            writer.WriteString("parentCommitOrTerminalId", proof.ParentCommitOrTerminalId);
            writer.WriteEndObject();
        }, RuleLimits.MaxStateMetadataBytes);
    }

    /// <summary>Reads a bounded canonical proof; callers must separately establish native durability.</summary>
    public static RuleDeliveryProof ReadDeliveryAcknowledgement(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > RuleLimits.MaxStateMetadataBytes)
            throw new InvalidDataException("rules.delivery-proof-too-large");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var root = document.RootElement;
        var proof = new RuleDeliveryProof(
            Enum.Parse<RuleDeliveryProofKind>(String(root, "kind"), ignoreCase: false),
            String(root, "destinationIdentity"),
            String(root, "terminalId"),
            String(root, "payloadSha256"),
            String(root, "parentCommitOrTerminalId"));
        Require(Enum.IsDefined(proof.Kind), "rules.delivery-proof-kind");
        RequireCanonical(bytes, DeliveryAcknowledgementBytes(proof));
        return proof;
    }

}
