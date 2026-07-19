# Contract: Dispatch Runtime API Remediation

- Redrive returns the safe disposition response defined by spec 101; accepted and idempotent replay remain distinguishable from not-found, ineligible, and active-conflict results.
- List and detail views expose equivalent allowlisted failure information.
- Unknown or malformed incident classification is represented by a safe known fallback.
- Incident and dead-letter identifiers are derived from dispatch identity or accepted only when they exactly match it.
- Retry projections expose attempt count and next scheduled availability without raw exception or payload data.
