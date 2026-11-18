namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that an activity type meets the background execution criteria of ADR 0093, so a workflow author may place
/// its nodes in the background.
/// </summary>
/// <remarks>
/// <para>
/// This is an eligibility declaration, not a scheduling implementation: the workflow author still chooses the placement
/// per node, and the host must enable background activities.
/// </para>
/// <para>
/// By declaring it, the activity author promises the two criteria no code can check:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>C7, tolerates running more than once.</b> After a worker is lost the body runs again. Every external effect is
/// idempotent, or keyed on the stable invocation identity (<c>ActivityExecutionContext.InvocationId</c>), so a re-run
/// does not repeat it.
/// </description></item>
/// <item><description>
/// <b>C8, cancels cooperatively.</b> The body observes its cancellation token and stops when it is signalled. A body that
/// ignores it cannot change the outcome, but it keeps a worker busy.
/// </description></item>
/// </list>
/// <para>
/// The engine checks the other criteria at publication and again at run time, and refuses a background placement that
/// breaks one with <c>VF-BG-001</c> naming it: C1 a leaf (a CLR activity, no intrinsic, no reusable boundary, no
/// children, not structural), C2 completes in one go (not stateful, no checkpoint participant), C3 a typed result (not
/// <c>object</c> or <c>Any</c>), C4 a durable result, C5 no hidden (sensitive or encryption-required) result, C6 no
/// coordinator effects from the body (no workflow dispatch, no published stimuli; checked after the body ran), C9
/// variables only through inputs and results (it does not read variables by name), and C10 worker-bound inputs that
/// have a durable representation, can be materialized on a worker and keep their protection (bind a sensitive value to
/// a secret reference, which resolves at worker activation).
/// </para>
/// <para>
/// Before marking an activity, also check two things no criterion catches: an activity whose result is only delivered
/// while the workflow drains inline (for example an HTTP response written during the request) and a trigger activity
/// (a worker gets no trigger payload) do not work in the background, so they are not marked.
/// </para>
/// <para>
/// The declaration is inherited: a subclass of a marked activity is marked too, and its author makes the same promise.
/// See ADR 0093, <c>docs/adr/0093-background-execution-eligibility-is-one-set-of-criteria.md</c>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class BackgroundExecutionSafeAttribute : Attribute;
