# Contract: Dispatch Runtime API Remediation

- Rejected redrive uses the existing API error response shape and a safe reason code.
- List and detail views expose equivalent allowlisted failure information.
- Unknown or malformed incident classification is represented by a safe known fallback.
- Incident and dead-letter identifiers are derived from dispatch identity or accepted only when they exactly match it.
- Retry projections expose attempt count and next scheduled availability without raw exception or payload data.
