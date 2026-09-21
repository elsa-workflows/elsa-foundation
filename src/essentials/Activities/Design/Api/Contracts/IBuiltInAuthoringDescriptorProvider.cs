using Elsa.Activities.Design.Api.Models;

namespace Elsa.Activities.Design.Api.Contracts;

/// <summary>
/// Contributes built-in authoring-catalog descriptors that are owned in code rather than persisted as
/// reconciled CLR/JSON catalog rows. Engine intrinsics (Set Variable, Set Output, …) have no CLR activity
/// type and no persisted catalog version, yet the authoring client must still be able to offer them from
/// the palette. A provider yields their descriptors so the catalog handler can merge them into the
/// available-activity list. Enumerable: independent features may each contribute their own built-ins.
/// </summary>
public interface IBuiltInAuthoringDescriptorProvider
{
    IReadOnlyCollection<ActivityAuthoringDescriptorView> GetDescriptors();
}
