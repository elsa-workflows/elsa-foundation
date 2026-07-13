using Elsa.Api.Capabilities.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Api.Capabilities.Events;

/// <summary>
/// Framework contribution event for modules that need to append conditional declarations while a
/// capability document is assembled. Prefer <c>IApiCapabilitySource</c> for independently testable
/// contributions; this event is the cross-feature extension seam.
/// </summary>
public sealed record CollectingApiCapabilities(ICollection<ApiCapabilityDeclaration> Declarations) : IEvent;
