namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

/// <summary>
/// What tells one generation of an activation projection state from another (#2265): the revision every switch moves,
/// and the fingerprint of the content it was prepared with. The revision alone does not, because a projection deleted
/// and prepared again starts over at <see cref="ActivationProjectionStateLifecycle.CreationRevision"/>.
/// </summary>
internal interface IActivationProjectionState
{
    long Revision { get; }

    string ProjectionFingerprint { get; }
}
