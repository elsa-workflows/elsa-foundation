# Contract: Startup Profiles

**Status**: Reviewed design; acceptance remains governed by [spec.md](../spec.md). This contract uses the canonical [host and shell terms](../../../docs/glossary/elsa.md).

All three adapters use the opt-in root `IShellActivationRunner`. An enabled profile starts at most one run when its activation phase is reached and joins it through the retained stop operation. Disabled profiles and warmup still waiting for ApplicationStarted do not create a run. Repeated successful Start calls must not duplicate lifecycle subscriptions or retry workers. The runner owns attempts, delays, and cancellation/join tracking; the adapter owns its phase, target selection, Elsa policy, diagnostics, and telemetry. No adapter adds a parallel scheduler, attempt counter, or poll loop.

## Foundation: retrying pre-listen activation

- Preserve default-on activation and explicit opt-out. Read configuration using the existing validation, defaults and fallback behavior before starting a run.
- Use the configured shell order. Await the serial initial pass before hosted startup completes and the server listens.
- Continue the initial pass after an ordinary target failure. After the pass, retry failed targets independently using the existing capped exponential/jitter policy. EF refusal retains its existing maximum-interval checks and operator guidance; attempts do not exhaust.
- Keep the existing Attention/health projection and JSON shape. The live registry remains the readiness authority; run snapshots and terminal startup outcomes do not replace it.
- Classify the existing fatal set separately from ordinary failures. An initial fatal cancels owned work, prevents later initial targets/retries, and is rethrown as its original exception after cleanup. A background fatal stops only its target; other targets keep recovering. At shutdown, join first, retain all captured fatal failures, then rethrow the first by configured target order.
- Forward the caller's startup token only while `StartAsync` is pending. Dispose the forwarding registration before successful return. Subsequent retries use host-owned cancellation; later caller-token cancellation cannot stop them.
- Preserve failure projection using callback `AttemptNumber` and one immutable retry decision as defined in [Data Model](../data-model.md). An observed Active lifecycle notification or owned success clears the row/base. A raw-Active failure check suppresses a new row; without notification it hides, but does not delete, a prior row. Rebase that prior row so the suppressed callback does not increment the next visible count.

## Workbench: opt-in one-shot eager activation

- Preserve the default-off behavior. When enabled, keep existing all/named target selection, configured order, and deduplication.
- Await one serial pre-listen pass. Continue after activation failures, log them using existing Workbench behavior, and do not schedule retries.
- Propagate cancellation of the supplied token. Other exceptions, including exceptions in Foundation's fatal set, retain Workbench's log-and-continue boundary; do not apply Foundation fatal policy globally.
- Use this profile independently from Workbench warmup. If both address the same shell, preserve the registry's single underlying activation while recording warmup's separate phase outcome and telemetry.

## Workbench: post-listen default-shell warmup

- Preserve optional disablement and nonblocking `StartAsync`: the existing warmup service waits for `ApplicationStarted`, performs feature discovery, then starts its one-shot default-shell activation run.
- Keep feature discovery before activation, with current discovery/activation/overall telemetry and Ready, Failed, Disabled, and Cancelled state transitions.
- Do not retry. Supplied-token cancellation records cancellation; other activation errors are logged and represented as failed warmup, then the host continues.
- On accepted success, record the generation returned by that activation, including a supported custom registry. Do not substitute a later raw-current lookup or an invented generation. A custom registry without the optional settled-current capability keeps the existing separate readiness behavior; startup-run completion does not resolve that open readiness choice.

## Startup, terminal state, and shutdown

- Foundation's initial pass is a startup gate; Workbench eager's pass is a gate only when opted in; Workbench warmup is post-listen and must not delay hosted-service startup.
- A successful owned activation or runner-observed external settlement ends startup recovery for that target. Later deactivation remains visible through live readiness and does not restart the completed run. Preserve the last selected retry interval/deadline as diagnostic history only; operator text must not promise a retry after terminal recovery.
- A cancellation-aware stop cancels and joins normally. A bounded wait may return while cancellation-ignoring work remains owned. A later stop call joins the same retained work. Cancellation-callback failure does not skip the join; independent shutdown failures remain observable. For multiple background fatals, selection happens only after join and uses configured target order.
- Initial and background fatal boundaries are different. Foundation's fatal set is `OutOfMemoryException`, `StackOverflowException`, `AccessViolationException`, `AppDomainUnloadedException`, and `BadImageFormatException`. An uncancelled `OperationCanceledException` is an ordinary retryable Foundation activation failure; cancellation of the supplied token is cancellation. Workbench keeps its own exception policy above.

## Out of scope

This contract does not alter startup configuration defaults, host composition, shell ownership, Nuplane reload profiles, readiness observation, physical package pruning/unloading, or health JSON. In particular, optional settled-current observation, EF package-generation readability, host-library composition extraction, and package unload remain separate work units. Final adoption still requires the stable package families, coherent locks, actual-host evidence, and resulting-main gates described by the spec and program.
