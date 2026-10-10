# Data Model: Shared Startup Runner Adoption

**Status**: Reviewed design; design aid for [spec.md](spec.md), not implementation or release acceptance.

Use the canonical terms for [hosts, shells, and activation](../../docs/glossary/elsa.md) and [lifecycle](../../docs/glossary/root.md). This document records only the state the adapters must own or project.

## Owned startup run

Each startup profile owns at most one `IShellActivationRun` for its configured target set and retains its stop/join operation. Foundation owns the additional lifetime cancellation source needed to detach the caller's startup token; Workbench warmup retains its existing background-service cancellation boundary. The run owns attempt sequencing and retry timing. Its targets are attempted serially for the initial pass; retrying profiles may then recover targets independently. A bounded caller wait does not replace, cancel, or forget the retained join.

The adapter owns a configured target order and phase, plus only the local policy and projection data needed to preserve Elsa behavior. Foundation eager startup and each Workbench phase own distinct runs. An adapter must not add a second attempt counter, timer, polling loop, or retry scheduler beside the runner.

## Attempt and outcome

A runner callback carries target identity, one-based `AttemptNumber`, and the structured activation outcome. For an unsuccessful callback, `AttemptNumber` is the cumulative unsuccessful-attempt sequence for that target in the run: all preceding attempts failed, since a success ends that target's startup recovery and cancellation ends the run before a later callback.

A successful Workbench warmup records the generation from the activation result it accepted. It does not infer one from a later registry read. Built-in verified metadata and the exact returned generation retain their separate meanings; custom-registry success may have no verified generation.

A runner-observed external settlement is terminal for startup recovery. It is not a live readiness certificate. A later deactivation is reported by current readiness and does not restart the completed boot run.

## Failure projection

Foundation retains the existing public failure row and JSON shape. Store, per target and run, only:

- a diagnostic row containing the existing failure classification, projected attempt count, sanitized refusal guidance, first/last failure timestamps and selected retry delay, with its computed advisory deadline; and
- a diagnostic base used to project consecutive visible failures from the runner's `AttemptNumber`.

The first eligible failure after a reset sets `base = AttemptNumber - 1`; the projected count is `AttemptNumber - base`. The adapter selects retry delay once and keeps one immutable per-target decision (attempt number, action, delay) for the runner's retry-policy callback. The policy returns that decision rather than incrementing a second counter or calculating jitter again. Ordinary exception instances are not retained in the projection.

### Reset and no-notification compatibility

An observed Active lifecycle notification or owned activation success clears the row and base. The existing raw-Active check at failure-write time has narrower semantics: it suppresses a new failed write and hides an earlier row while the registry reports Active, but it does not delete that row when no lifecycle notification was received.

If a failed callback is suppressed by that raw-Active check and an earlier row with projected count `C` exists, retain the row and rebase to `AttemptNumber - C`. Select the existing delay for `C + 1` but do not publish that increment. If there is no prior row, retain no row or base; a later eligible callback establishes a fresh first-failure base. This prevents the ignored callback from inflating the next visible count while preserving the old row if the registry later becomes inactive without notification. Do not infer an unobserved reset for custom registries.

## Fatal failure and shutdown

A fatal record is separate from ordinary diagnostic projection. It contains target order and the original exception dispatch information needed to rethrow the original exception after owned work joins. Do not retain fatal records as retry counts or public readiness details.

On Foundation's initial pass, a fatal failure prevents later initial targets and retries and is rethrown from startup after owned cleanup. A background fatal stops that target only; other targets continue. Shutdown joins all owned work and then deterministically selects the first fatal by configured target order, retaining all captured fatal failures. A caller-bounded wait leaves the same join available to a later caller.

## Host profiles

The adapters consume three distinct profiles, specified in [startup-profiles.md](contracts/startup-profiles.md): Foundation retrying pre-listen activation; Workbench opt-in one-shot pre-listen eager activation; and Workbench post-listen default-shell warmup. Their defaults, phases, and host-owned outcomes remain distinct even though all use the same runner.
