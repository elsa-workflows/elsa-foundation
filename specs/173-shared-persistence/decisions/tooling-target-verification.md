# Decision request: verify the live resource connection

Status: proposed; owner decision requested before adoption. Tracking: #1967.

## Why this decision matters

The shared-resource contract should prevent migration tooling from selecting the right provider and module names while applying them to the wrong database. A connection name alone cannot prove the supplied live connection value matches the selected resource.

[ADR 0076 D7](../../../docs/adr/0076-persistence-tooling-runs-inside-the-host-closure.md#d7--connection-by-environment-variable-or-stdin-only) limits live connection input to environment/stdin. Its exact source section remains authoritative; this proposal does not change accepted behavior until approved.

## Recommended amendment for resource mode

- Actual database operations still receive their connection only through the existing explicit environment/stdin input. No raw connection argument is introduced.
- Inside the target host closure, allow the resource-aware tooling adapter to resolve the selected resource's named connection from the explicitly selected target-host configuration context solely as the expected value for comparison.
- Compare the expected value and the supplied live value using the existing strict target equality convention. Refuse a mismatch or an unresolved expected reference before opening any database.
- Do not use the expected value as an alternate connection if the operator omitted env/stdin input. Do not read ambient tool-process configuration as though it were the runtime's context.
- Keep both values inside the trusted process. Never return them in a protocol response, plan, manifest, exception or log. No secret-bearing configuration snapshot travels through the front-end protocol.
- Offline planning never needs or resolves a live connection value; it reports reference and context prerequisites separately from proven live target checks.
- Legacy commands keep their current behavior. Equivalent-context parity does not claim knowledge of an independently running host's unobserved environment.

This extends the trust boundary to reading an expected configured value for verification. It does not add another way to supply the connection actually used by a live operation.

## Alternative

Keep D7 unchanged and prove only provider/resource/connection-reference agreement from the tool's explicit context. Mark connection-value parity unverified when an independent expected value is unavailable. A separate explicit verification mechanism would then be needed before the program could claim full target agreement or report a layout ready.

## Required verification if approved

1. Right provider and resource name with the wrong supplied connection refuses before database access.
2. Unset expected connection refuses; it does not substitute a default or adopt the supplied value as its own expectation.
3. Correct explicit env/stdin value agrees and operates only on the selected target's module set.
4. Canary secrets never appear in request/response output, manifest, stdout/stderr, diagnostic, exception, or argv.
5. Legacy requests remain unchanged, and an old host refuses the negotiated resource contract clearly.

The downstream plan remains open on this choice; no production implementation is authorized by this draft alone.
