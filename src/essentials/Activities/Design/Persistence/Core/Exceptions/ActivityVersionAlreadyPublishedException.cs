namespace Elsa.Activities.Design.Persistence.Core.Exceptions;

/// <summary>
/// Thrown when a publication commit finds the activity version already published, or loses a uniqueness race to a
/// concurrent commit of the same rows. Either way another writer published first, so a caller that can recognise its
/// own publication may compare what is stored with what it meant to write (#2189). Every other refusal of a
/// publication remains a plain <see cref="InvalidOperationException"/>, which this type derives from so existing
/// handlers keep catching it.
/// </summary>
public sealed class ActivityVersionAlreadyPublishedException(string definitionVersionId, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string DefinitionVersionId { get; } = definitionVersionId;
}
