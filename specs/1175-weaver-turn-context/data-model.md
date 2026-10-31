# Data and lifecycle

Reuse existing Agent context, request, target, client binding, diagnostic, proposal and receipt models. Exact additive wire/API changes will be recorded in [the contract note](contracts/context-lifecycle.md) after causal experiments.

| Concept | Existing ownership | Validation and transitions |
|---|---|---|
| Request identity/source | Studio dispatch and Agent message | Preserve explicit correlation/source; one terminal admission disposition. Pending collection may cancel or time out; stale completion cannot advance it. |
| Active surface | Studio request/session metadata | Refresh for a new request; it is a hint for installed sources, never authority or a replacement invocation target. |
| Caller attachment | Module-provided opaque payload | Owner/schema must match installed admission; caller origin, scopes and redaction assertions are untrusted. |
| Admitted attachment | Owning backend source plus generic sanitizer/policy | Transport-safe bounded snapshot, owner/provenance/revision and diagnostics; recheck eligibility before later disclosure. |
| Collection outcome | Source plus generic collection lifecycle | Complete, optional limitation, missing required evidence, cancellation or deadline. A late source cannot mutate an accepted outcome. |
| Prepared invocation | Owning tool plus generic review lifecycle | Original target/revision/client generation persist; navigation and changed authority cause explicit refusal when required. |
| Accepted effect receipt | Owning tool/result persistence | Effect truth stays immutable; later disclosure is authorized separately. Continuation is mutation-free and never repeats execution. |
| Resource-derived history | Agent messages with originating evidence | Current disclosure must be established before provider use; unavailable authority produces bounded withholding/refusal without rewriting truthful audit. |

Installed module guidance remains distinct from resource data; only the installed source can confer that provenance. Module-specific data stays opaque in shared presentation and transport.
