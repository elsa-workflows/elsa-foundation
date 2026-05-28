namespace Elsa.Activities.Design.Persistence.Core.Exceptions;

/// <summary>
/// Domain exception raised when the loading handler cannot deserialise the persisted
/// <c>ImplementationDescriptorPayload</c> of an activity definition version. Carries the
/// version id + kind so callers can localise the corruption. Replaces a raw
/// <c>JsonException</c> bubbling out of the serializer at the entity boundary.
/// </summary>
public sealed class ActivityDescriptorDeserialisationException : Exception
{
    public string VersionId { get; }
    public string ImplementationKind { get; }

    public ActivityDescriptorDeserialisationException(string versionId, string implementationKind, string message)
        : base(message)
    {
        VersionId = versionId;
        ImplementationKind = implementationKind;
    }

    public ActivityDescriptorDeserialisationException(string versionId, string implementationKind, string message, Exception inner)
        : base(message, inner)
    {
        VersionId = versionId;
        ImplementationKind = implementationKind;
    }
}
