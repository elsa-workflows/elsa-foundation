using System.Security.Cryptography;
using System.Text;

namespace Elsa.Activities.Design.Persistence.Core.Constants;

public static class ActivityDesignPersistenceLockKeys
{
    public static string DraftKey(string draftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        return $"activity-draft:{draftId}";
    }

    /// <summary>
    /// Serializes operations that validate or mutate one activity definition's publication head
    /// or version lifecycle. Runtime lock name only — not a persisted identifier.
    /// </summary>
    public static string PublicationDefinitionKey(string definitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(definitionId)));
        return $"elsa:activities:design:publication:{hash}";
    }
}
