using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Workflows.Publishing.Core.Services;

/// <summary>
/// Computes the canonical binding for the reviewed request material associated with one
/// activity-publication operation.
/// </summary>
public static class ActivityPublicationRequestFingerprint
{
    public static string Compute(
        string draftId,
        long expectedDraftRevision,
        string? expectedDefinitionHeadVersionId,
        string requestedVersion,
        string reviewToken)
    {
        ArgumentNullException.ThrowIfNull(draftId);
        ArgumentNullException.ThrowIfNull(requestedVersion);
        ArgumentNullException.ThrowIfNull(reviewToken);
        var fields = new[]
        {
            draftId,
            expectedDraftRevision.ToString(CultureInfo.InvariantCulture),
            expectedDefinitionHeadVersionId ?? "",
            requestedVersion,
            reviewToken
        };
        var material = string.Concat(fields.Select(field =>
            $"{field.Length.ToString(CultureInfo.InvariantCulture)}:{field}"));
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }
}
