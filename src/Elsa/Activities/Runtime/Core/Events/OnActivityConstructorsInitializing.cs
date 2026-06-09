using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Events.Core.Contracts;

namespace Elsa.Activities.Runtime.Core.Events;

/// <summary>
/// Contribution event published once at startup by <c>ActivityConstructorsStartupTask</c>. Each
/// kind-owning feature contributes by registering an <see cref="IActivityConstructor"/>; the single
/// aggregating handler adds them to <see cref="Constructors"/>, and the startup task flushes the
/// collection into the <see cref="IActivityConstructorRegistry"/>. Canonical §2.6.1 Registry +
/// StartUp Task sub-pattern. Declared in Runtime.Core — no Design dependency.
/// </summary>
public sealed record OnActivityConstructorsInitializing(
    ICollection<IActivityConstructor> Constructors)
    : IEvent;
