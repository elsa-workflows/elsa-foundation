# PR2315 review round 1

Reviewed head: `26dc39845c0545d89690a9c120c876894e16525b`. Actual Copilot review5391935827 has two valid findings; CodeQuality review5391908402 has ten comments requiring explanations. The existing published head has all checks terminal and green/nonblocking, but is not merge-ready while the two defects remain.

This ledger records root dispositions. Planned replies become direct GitHub replies after the fix is pushed; pending verification is not a pass.

- [4165807027](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807027) — **explain**; `github-code-quality[bot]`, `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Fixtures/WorkerOidcHost/Program.cs`:110
  - Planned reply: CreateApp has a nonnullable WebApplication return and returns builder.Build(), or throws. The nullable app variable exists for cleanup when construction throws before assignment. No dereference is reachable without successful assignment.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807064](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807064) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerActivationGuard.cs`:54
  - Planned reply: Activation deliberately sanitizes collaborator failures into the fixed configuration refusal. Cancellation is identified by the supplied token, checked immediately inside the catch; an unrelated OperationCanceledException with a live activation token must not leak collaborator text.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807084](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807084) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`:43
  - Planned reply: The approved bearer contract requires arbitrary prior-callback failures to become fixed event-stage refusals before the handler can expose or log raw values. Catching only AuthenticationException would change that behavior. RequestAborted is checked immediately in the catch.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807104](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807104) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`:66
  - Planned reply: TokenValidated callbacks are host code and can throw arbitrary exception types. The approved bearer contract requires a fixed refusal for them; narrowing to InvalidOperationException would expose other failures through framework handling. Observed RequestAborted propagates.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807127](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807127) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`:91
  - Planned reply: IOptionsMonitor can surface OptionsValidationException and arbitrary host configure/validator exceptions; these are not limited to InvalidOperationException. This owned boundary deliberately returns the fixed configuration refusal and checks RequestAborted before doing so.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807144](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807144) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`:102
  - Planned reply: The host-supplied persistence accessor can throw independently of RequireScope. The approved boundary sanitizes arbitrary accessor failures into the fixed scope refusal before mapping access, while propagating observed RequestAborted.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807159](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807159) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`:119
  - Planned reply: IClaimMappingStore implementations can throw provider or other dependency exceptions, not just InvalidOperationException. The approved contract requires all non-aborted mapping failures to use the fixed mapping-unavailable refusal; RequestAborted is checked immediately in the catch.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807176](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807176) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`:133
  - Planned reply: The selected normalizer can throw arbitrary exceptions. The approved contract sanitizes them, including an unrelated OperationCanceledException when RequestAborted remains live; only observed request cancellation propagates. Narrowing the catch would change the failure and information-exposure contract.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807198](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807198) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`:147
  - Planned reply: Output admission enumerates host-supplied principal and identity implementations, whose enumeration may throw arbitrary exceptions. Such malformed results must become the fixed result-invalid refusal. Existing malformed-enumeration controls and request-abort checks cover this boundary.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165807216](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165807216) — **explain**; `github-code-quality[bot]`, `src/essentials/Foundation/Identity/Oidc/OidcBearerNormalizationEvents.cs`:171
  - Planned reply: The catch already calls RequestAborted.ThrowIfCancellationRequested before producing a fixed callback refusal, so observed request cancellation is not swallowed. A filtered catch could rethrow the callback exception instead of the request cancellation and alter the approved cancellation semantics.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165827519](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165827519) — **fix**; `Copilot`, `src/essentials/Foundation/Identity/Oidc/OidcBearerOptionsValidator.cs`:84
  - Planned reply: Freeze the first accepted final MetadataAddress under the existing validator lock, including absent/null values. Preserve initial custom discovery and trusted validation delegates, accept unchanged options recreation, and refuse changed discovery without fresh activation. Add a stock-handler two-issuer options-reload regression.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

- [4165827587](https://github.com/elsa-workflows/elsa-foundation/pull/2315#discussion_r4165827587) — **fix**; `Copilot`, `src/essentials/Foundation/Identity/Oidc/OidcBearerActivationGuard.cs`:33
  - Planned reply: Use await using with CreateAsyncScope inside the existing failure guard. Add real ServiceProvider controls for completed async-only cleanup, fixed refusal on arbitrary disposal exceptions, and observed cancellation during disposal; retain existing synchronous scope controls.
  - Reply/resolution: the linked GitHub thread is authoritative after fix publication.

Contract authority: [bearer normalization](contracts/bearer-normalization.md), including frozen trust boundaries, arbitrary dependency-failure sanitization and observed RequestAborted cancellation.

Independent round1 source review confirmed final MetadataAddress freezing and identified async disposal must remain inside the exception guard and precede the final cancellation check. Root moved the declaration inside the existing try; regression controls will cover disposal failures and cancellation as part of the same fix. No new trust model or supported layout is introduced.

Root verified the restored patch: Identity446, actual Worker actor2 and Architecture635 passed with zero failures/skips; maps and filters are fresh. Three byte-restored production mutations detected the actual second-issuer admission, synchronous-disposal startup failure, and unguarded disposal exception/cancellation behavior. See [implementation evidence](implementation-evidence.md#review-round-1--discovery-reload-and-async-cleanup) for exact commands, source identities and limitations. New-head automated review/check convergence remains required after publication.
